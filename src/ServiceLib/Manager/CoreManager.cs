namespace ServiceLib.Manager;

/// <summary>
/// Core process processing class
/// </summary>
public class CoreManager
{
    private static readonly Lazy<CoreManager> _instance = new(() => new());
    public static CoreManager Instance => _instance.Value;
    private Config _config;

    [SupportedOSPlatform("windows")]
    private WindowsJobService? _processJob;

    private ProcessService? _processService;

    /// <summary>
    /// True while the core process is actually alive. Used to report a truthful
    /// connection state instead of merely echoing the requested TUN flag.
    /// </summary>
    public bool IsCoreRunning => _processService is { HasExited: false };
    private ProcessService? _processPreService;
    private bool _linuxSudo = false;

    /// <summary>The running core and the config it runs, for deciding on a hot switch.</summary>
    private RunningCoreState? _runningState;

    /// <summary>
    /// <see cref="CoreSwitchPlanner.ReadConfigDigest"/> of the config the running core (and its
    /// pre core) runs, so a background reload can tell nothing changed; null while none runs.
    /// </summary>
    private string? _runningConfigDigest;

    /// <summary>
    /// Whether a Wintun adapter from an earlier core may still be around: until the first TUN
    /// start of this session succeeds, and after a core died on its own.
    /// </summary>
    private bool _tunDeviceMayBeStale = true;
    private Func<bool, string, Task>? _updateFunc;
    private const string _tag = "CoreHandler";

    public async Task Init(Config config, Func<bool, string, Task> updateFunc)
    {
        _config = config;
        _updateFunc = updateFunc;

        //Copy the bin folder to the storage location (for init)
        if (Environment.GetEnvironmentVariable(Global.LocalAppData) == "1")
        {
            var fromPath = Utils.GetBaseDirectory("bin");
            var toPath = Utils.GetBinPath("");
            if (fromPath != toPath)
            {
                FileUtils.CopyDirectory(fromPath, toPath, true, false);
            }
        }

        if (Utils.IsNonWindows())
        {
            var coreInfo = CoreInfoManager.Instance.GetCoreInfo();
            foreach (var it in coreInfo)
            {
                if (it.CoreType == ECoreType.v2rayN)
                {
                    if (Utils.UpgradeAppExists(out var upgradeFileName))
                    {
                        await Utils.SetLinuxChmod(upgradeFileName);
                    }
                    continue;
                }

                foreach (var name in it.CoreExes)
                {
                    var exe = Utils.GetBinPath(Utils.GetExeName(name), it.CoreType.ToString());
                    if (File.Exists(exe))
                    {
                        await Utils.SetLinuxChmod(exe);
                    }
                }
            }
        }
    }

    /// <param name="mainContext">Resolved main context (with pre-socks ports already merged if applicable).</param>
    /// <param name="preContext">Optional pre-socks context passed to <see cref="CoreStartPreService"/>.</param>
    /// <param name="keepIfUnchanged">
    /// True for a reload nobody asked for by hand: when the new config is the one the core
    /// already runs, the core is left alone instead of being restarted.
    /// </param>
    /// <returns>True when the running core was left as it was.</returns>
    public async Task<bool> LoadCore(CoreConfigContext? mainContext, CoreConfigContext? preContext,
        bool keepIfUnchanged = false)
    {
        if (mainContext == null)
        {
            await UpdateFunc(false, ResUI.CheckServerSettings);
            return false;
        }

        var node = mainContext.Node;
        var isTun = mainContext.IsTunEnabled || preContext?.IsTunEnabled == true;
        SwitchTiming.Mark($"load core: core {mainContext.RunCoreType}{(preContext != null ? $" + pre {preContext.RunCoreType}" : "")}, {(isTun ? "tun" : "proxy")}");
        var fileName = Utils.GetBinConfigPath(Global.CoreConfigFileName);
        // Writing the config starts the AmneziaWG relays it points at; the old core keeps its own until it stops.
        AwgRelayManager.Instance.MarkStale();
        var result = await CoreConfigHandler.GenerateClientConfig(mainContext, fileName);
        if (result.Success != true)
        {
            await UpdateFunc(true, result.Msg);
            return false;
        }
        SwitchTiming.Mark("config generated");

        // The pre core's config is generated again when it starts; this copy is only compared.
        var preConfig = preContext is null ? null : (await CoreConfigHandler.GenerateClientConfig(preContext, null)).Data?.ToString();
        var newDigest = CoreSwitchPlanner.ReadConfigDigest(result.Data?.ToString(),
            preContext is null ? null : preConfig ?? string.Empty);
        var coresAlive = IsCoreRunning && (preContext is null
            ? _processPreService is null
            : _processPreService is { HasExited: false });
        if (keepIfUnchanged && CoreSwitchPlanner.CanKeepRunningCore(_runningConfigDigest, coresAlive, newDigest))
        {
            SwitchTiming.End("decision: Keep (config unchanged)");
            Logging.SaveLog("SvoRay: the config is unchanged, the running core is left alone");
            return true;
        }

        var newShape = mainContext.RunCoreType switch
        {
            ECoreType.sing_box => CoreSwitchPlanner.ReadSingboxShape(result.Data?.ToString()),
            ECoreType.Xray => CoreSwitchPlanner.ReadXrayShape(result.Data?.ToString()),
            _ => null,
        };
        var switchKind = CoreSwitchPlanner.Decide(_runningState, IsCoreRunning && _processPreService is null,
            mainContext.RunCoreType, preContext != null, newShape);
        SwitchTiming.Mark($"decision: {switchKind}{(newShape != null ? " (switchable config)" : "")}");
        if (switchKind == ECoreSwitchKind.HotSwitch && await TryHotSwitchAsync(newShape!))
        {
            _runningConfigDigest = newDigest;
            await UpdateFunc(true, $"{node.GetSummary()}");
            return false;
        }

        await UpdateFunc(false, $"{node.GetSummary()}");
        await UpdateFunc(false, $"{Utils.GetRuntimeInfo()}");
        await UpdateFunc(false, string.Format(ResUI.StartService, DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")));
        SwitchTiming.MarkGapStart("stopping old core");
        if (_processService is { HasExited: true })
        {
            // The core died on its own, so it may have left its adapter behind.
            _tunDeviceMayBeStale = true;
        }
        await StopProcesses();
        AwgRelayManager.Instance.StopStale();
        SwitchTiming.Mark("old core stopped");

        var removedTunDevices = false;
        if (Utils.IsWindows() && CoreSwitchPlanner.ShouldRemoveTunDevices(isWindows: true, isTun, _tunDeviceMayBeStale))
        {
            await WindowsUtils.RemoveTunDevice();
            removedTunDevices = true;
            SwitchTiming.Mark("tun devices removed (pnputil)");
        }

        await CoreStart(mainContext);
        SwitchTiming.Mark("core process started");

        if (preContext is null)
        {
            var ready = await WaitForCoreReady(mainContext);
            if (!ready && !IsCoreRunning && isTun && Utils.IsWindows() && !removedTunDevices)
            {
                // A Wintun adapter left from an earlier run can make the new one fail to come up.
                SwitchTiming.Mark("core exited while starting; removing tun devices and retrying");
                _tunDeviceMayBeStale = true;
                await StopProcesses();
                await WindowsUtils.RemoveTunDevice();
                await CoreStart(mainContext);
                ready = await WaitForCoreReady(mainContext);
            }
            SwitchTiming.Mark(ready ? "core ready (proxy port answers)" : "core not confirmed ready");
        }
        else
        {
            await WaitForProxyPort(preContext);
            if (preContext.IsTunEnabled)
            {
                SwitchTiming.Mark("proxy port ready");
            }
            await CoreStartPreService(preContext);
            SwitchTiming.Mark("pre core started");
        }

        AppManager.Instance.RunningCoreType = preContext?.RunCoreType ?? mainContext.RunCoreType;

        if (isTun && IsCoreRunning)
        {
            _tunDeviceMayBeStale = false;
        }

        if (_processService != null)
        {
            _runningConfigDigest = IsCoreRunning ? newDigest : null;
            if (newShape != null && IsCoreRunning && preContext is null)
            {
                _runningState = new RunningCoreState(mainContext.RunCoreType, newShape);
                if (mainContext.RunCoreType == ECoreType.sing_box && _config.CoreBasicItem.EnableCacheFile4Sbox)
                {
                    // The cache file remembers the selector's last choice and sing-box restores
                    // it over the config's default; the server the user picked must win.
                    await ClashApiManager.Instance.TrySelectProxyAsync(Global.ProxyTag, newShape.Selection);
                }
            }
            await UpdateFunc(true, $"{node.GetSummary()}");
        }
        return false;
    }

    /// <summary>
    /// Moves the running core to the new server: sing-box by pointing its server selector at it,
    /// Xray by replacing its proxy outbound. False when that did not work; the caller then
    /// restarts the core as usual, which also repairs a half-done Xray swap.
    /// </summary>
    private async Task<bool> TryHotSwitchAsync(CoreConfigShape newShape)
    {
        var running = _runningState!;
        bool ok;
        if (running.CoreType == ECoreType.sing_box)
        {
            SwitchTiming.MarkGapStart("hot switch: selecting the server in the running core");
            ok = await ClashApiManager.Instance.TrySelectProxyAsync(Global.ProxyTag, newShape.Selection);
        }
        else
        {
            var xrayExe = CoreInfoManager.Instance.GetCoreExecFile(
                CoreInfoManager.Instance.GetCoreInfo(ECoreType.Xray), out _);
            var swap = xrayExe.IsNullOrEmpty()
                ? EXraySwapResult.Unchanged
                : await XrayApiService.SwapOutboundAsync(xrayExe, AppManager.Instance.StatePort3,
                    running.Shape.Selection, newShape.Selection,
                    phase =>
                    {
                        if (phase.StartsWith("removing"))
                        {
                            SwitchTiming.MarkGapStart($"hot switch: {phase}");
                        }
                        else
                        {
                            SwitchTiming.Mark($"hot switch: {phase}");
                        }
                    });
            ok = swap == EXraySwapResult.Swapped;
            if (!ok)
            {
                SwitchTiming.Mark($"hot switch failed ({swap})");
            }
        }

        if (!ok)
        {
            SwitchTiming.Mark("hot switch refused by the core; restarting it");
            _runningState = null;
            return false;
        }
        _runningState = running with { Shape = newShape, HotSwitches = running.HotSwitches + 1 };
        SwitchTiming.Mark("hot switch done");
        return true;
    }

    public async Task<ProcessService?> LoadCoreConfigSpeedtest(List<ServerTestItem> selecteds)
    {
        var coreType = selecteds.FirstOrDefault()?.CoreType == ECoreType.sing_box ? ECoreType.sing_box : ECoreType.Xray;
        var fileName = string.Format(Global.CoreSpeedtestConfigFileName, Utils.GetGuid(false));
        var configPath = Utils.GetBinConfigPath(fileName);
        var result = await CoreConfigHandler.GenerateClientSpeedtestConfig(_config, configPath, selecteds, coreType);
        await UpdateFunc(false, result.Msg);
        if (result.Success != true)
        {
            return null;
        }

        await UpdateFunc(false, string.Format(ResUI.StartService, DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")));
        await UpdateFunc(false, configPath);

        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);
        return await RunProcess(coreInfo, fileName, true, false);
    }

    public async Task<ProcessService?> LoadCoreConfigSpeedtest(ServerTestItem testItem)
    {
        var node = await AppManager.Instance.GetProfileItem(testItem.IndexId);
        if (node is null)
        {
            return null;
        }

        var fileName = string.Format(Global.CoreSpeedtestConfigFileName, Utils.GetGuid(false));
        var configPath = Utils.GetBinConfigPath(fileName);
        var (context, _) = await CoreConfigContextBuilder.Build(_config, node);
        var result = await CoreConfigHandler.GenerateClientSpeedtestConfig(_config, context, testItem, configPath);
        if (result.Success != true)
        {
            return null;
        }

        var coreType = context.RunCoreType;
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);
        return await RunProcess(coreInfo, fileName, true, false);
    }

    public async Task CoreStop()
    {
        await StopProcesses();
        AwgRelayManager.Instance.StopAll();
    }

    private async Task StopProcesses()
    {
        _runningState = null;
        _runningConfigDigest = null;
        try
        {
            if (_linuxSudo)
            {
                await CoreAdminManager.Instance.KillProcessAsLinuxSudo();
                _linuxSudo = false;
            }

            if (_processService != null)
            {
                await _processService.StopAsync();
                _processService.Dispose();
                _processService = null;
            }

            if (_processPreService != null)
            {
                await _processPreService.StopAsync();
                _processPreService.Dispose();
                _processPreService = null;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    #region Private

    private async Task CoreStart(CoreConfigContext context)
    {
        var node = context.Node;
        var coreType = AppManager.Instance.GetCoreType(node, node.ConfigType);
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(coreType);

        var displayLog = node.ConfigType != EConfigType.Custom || node.DisplayLog;
        var proc = await RunProcess(coreInfo, Global.CoreConfigFileName, displayLog, true, context.IsTunEnabled);
        if (proc is null)
        {
            return;
        }
        _processService = proc;
    }

    private async Task CoreStartPreService(CoreConfigContext? preContext)
    {
        if (_processService is { HasExited: false } && preContext != null)
        {
            var preCoreType = preContext?.Node?.CoreType ?? ECoreType.sing_box;
            var fileName = Utils.GetBinConfigPath(Global.CorePreConfigFileName);
            var result = await CoreConfigHandler.GenerateClientConfig(preContext, fileName);
            if (result.Success)
            {
                var coreInfo = CoreInfoManager.Instance.GetCoreInfo(preCoreType);
                var proc = await RunProcess(coreInfo, Global.CorePreConfigFileName, true, true, preContext.IsTunEnabled);
                if (proc is null)
                {
                    return;
                }
                _processPreService = proc;
            }
        }
    }

    private async Task UpdateFunc(bool notify, string msg)
    {
        await _updateFunc?.Invoke(notify, msg);
    }

    private static async Task WaitForProxyPort(CoreConfigContext? preContext)
    {
        if (preContext is null)
        {
            return;
        }
        if (!preContext.IsTunEnabled)
        {
            return;
        }

        var port = preContext.Node.Port;
        if (!await WaitForSocksPort(port, (int)Global.LocalFetch.TotalMilliseconds, () => false))
        {
            Logging.SaveLog($"WaitForProxyPort Timeout waiting for proxy port {port} to be ready.");
        }
    }

    /// <summary>
    /// Waits until the freshly started core answers on its local SOCKS port - the point from
    /// which traffic can flow - instead of a fixed delay. Returns early, with false, when the
    /// core exits. Custom configs may not have the port at all and are not waited for.
    /// </summary>
    private async Task<bool> WaitForCoreReady(CoreConfigContext context, int timeoutMs = 5000)
    {
        if (!IsCoreRunning)
        {
            return false;
        }
        if (context.Node.ConfigType == EConfigType.Custom)
        {
            return true;
        }
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        return await WaitForSocksPort(port, timeoutMs, () => !IsCoreRunning);
    }

    private static async Task<bool> WaitForSocksPort(int port, int timeoutMs, Func<bool> giveUp)
    {
        using var rootCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        var rootToken = rootCts.Token;

        // SOCKS5 client greeting: VER=5, NMETHODS=1, METHOD=0x00 (no auth)
        ReadOnlyMemory<byte> greeting = new byte[] { 0x05, 0x01, 0x00 };
        var buf = new byte[2];

        while (!rootToken.IsCancellationRequested && !giveUp())
        {
            using var tcp = new TcpClient();
            using var attemptCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(rootToken, attemptCts.Token);
            var linkedToken = linkedCts.Token;
            try
            {
                await tcp.ConnectAsync(Global.Loopback, port, linkedToken);
                var stream = tcp.GetStream();

                await stream.WriteAsync(greeting, linkedToken);

                var read = await stream.ReadAsync(buf.AsMemory(0, 2), linkedToken);

                // Server selection: VER=5 - the proxy is fully ready
                if (read == 2 && buf[0] == 0x05)
                {
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                if (!rootToken.IsCancellationRequested)
                {
                    continue;
                }
                return false;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                // Connection refused, proxy not ready yet, wait 50ms before retrying
                try
                {
                    await Task.Delay(50, rootToken);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
            catch
            {
                // Ignore other exceptions and continue
            }
        }
        return false;
    }

    #endregion Private

    #region Process

    /// <summary>
    ///     Decides whether a core launch must be elevated on non-Windows platforms.
    ///     The TUN state comes from the immutable <see cref="CoreConfigContext" /> snapshot that
    ///     generated the config, never from the live mutable config: the generated config and the
    ///     launch mode must always agree, even if TUN is toggled while a reload is in flight.
    /// </summary>
    public static bool ShouldRunAsSudo(bool isTunLaunch, ECoreType? coreType, bool isNonWindows)
    {
        return isTunLaunch
            && coreType is ECoreType.sing_box or ECoreType.mihomo or ECoreType.Xray
            && isNonWindows;
    }

    private async Task<ProcessService?> RunProcess(CoreInfo? coreInfo, string configPath, bool displayLog, bool mayNeedSudo, bool isTunLaunch = false)
    {
        var fileName = CoreInfoManager.Instance.GetCoreExecFile(coreInfo, out var msg);
        if (fileName.IsNullOrEmpty())
        {
            await UpdateFunc(false, msg);
            return null;
        }

        try
        {
            if (mayNeedSudo
                && ShouldRunAsSudo(isTunLaunch, coreInfo.CoreType, Utils.IsNonWindows()))
            {
                _linuxSudo = true;
                await CoreAdminManager.Instance.Init(_config, _updateFunc);
                return await CoreAdminManager.Instance.RunProcessAsLinuxSudo(fileName, coreInfo, configPath);
            }

            return await RunProcessNormal(fileName, coreInfo, configPath, displayLog);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            await UpdateFunc(mayNeedSudo, ex.Message);
            return null;
        }
    }

    private async Task<ProcessService?> RunProcessNormal(string fileName, CoreInfo? coreInfo, string configPath, bool displayLog)
    {
        var environmentVars = new Dictionary<string, string>();
        foreach (var kv in coreInfo.Environment)
        {
            environmentVars[kv.Key] = string.Format(kv.Value, coreInfo.AbsolutePath ? Utils.GetBinConfigPath(configPath).AppendQuotes() : configPath);
        }

        var procService = new ProcessService(
            fileName: fileName,
            arguments: string.Format(coreInfo.Arguments, coreInfo.AbsolutePath ? Utils.GetBinConfigPath(configPath).AppendQuotes() : configPath),
            workingDirectory: Utils.GetBinConfigPath(),
            displayLog: displayLog,
            redirectInput: false,
            environmentVars: environmentVars,
            updateFunc: _updateFunc
        );

        await procService.StartAsync();

        await Task.Delay(100);

        if (procService is null or { HasExited: true })
        {
            throw new Exception(ResUI.FailedToRunCore);
        }
        AddProcessJob(procService.Handle);

        return procService;
    }

    private void AddProcessJob(nint processHandle)
    {
        if (Utils.IsWindows())
        {
            _processJob ??= new();
            try
            {
                _processJob?.AddProcess(processHandle);
            }
            catch { }
        }
    }

    #endregion Process
}
