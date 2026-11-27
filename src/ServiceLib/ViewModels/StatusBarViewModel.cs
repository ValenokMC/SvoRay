namespace ServiceLib.ViewModels;

public partial class StatusBarViewModel : MyReactiveObject
{
    public Interaction<string, RxVoid> SetClipboardDataInteraction { get; } = new();
    public Interaction<RxVoid, string?> PasswordInputInteraction { get; } = new();
    public Interaction<RxVoid, RxVoid> DispatcherRefreshIconInteraction { get; } = new();
    public EventChannel<bool> SubscriptionsUpdateRequested { get; } = new();
    public EventChannel<bool?> ShowHideWindowRequested { get; } = new();

    /// <summary>
    /// Raised by the tray menu so the main window runs the same connect/disconnect
    /// path as the power button, including the TUN preparation step.
    /// </summary>
    public EventChannel<bool> ConnectToggleRequested { get; } = new();

    private static readonly Lazy<StatusBarViewModel> _instance = new(() => new());
    public static StatusBarViewModel Instance => _instance.Value;

    public EventChannel<string> SetDefaultServerRequested { get; } = new();
    public EventChannel<RxVoid> ReloadRequested { get; } = new();
    public EventChannel<RxVoid> AddServerViaScanRequested { get; } = new();
    public EventChannel<RxVoid> AddServerViaClipboardRequested { get; } = new();

    #region ObservableCollection

    public BulkObservableCollection<RoutingItem> RoutingItems { get; } = [];

    public BulkObservableCollection<ComboItem> Servers { get; } = [];

    /// <summary>
    /// Every profile, never capped by <see cref="ConfigItems.TrayMenuServersLimit"/>,
    /// so the simple-mode selector keeps working for large subscriptions.
    /// </summary>
    public BulkObservableCollection<ComboItem> SimpleServers { get; } = [];

    /// <summary>
    /// The same servers grouped under a heading per source, with a last row that adds one, for
    /// the simple screen's selector. Its server rows are the very objects in <see cref="SimpleServers"/>,
    /// so both selectors show the one <see cref="SelectedServer"/>.
    /// </summary>
    public BulkObservableCollection<ComboItem> SimpleServerList { get; } = [];

    [Reactive]
    public partial RoutingItem SelectedRouting { get; set; }

    [Reactive]
    public partial ComboItem SelectedServer { get; set; }

    [Reactive]
    public partial bool BlServers { get; set; }

    #endregion ObservableCollection

    public ReactiveCommand<RxVoid, RxVoid> AddServerViaClipboardCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddServerViaScanCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SubUpdateCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SubUpdateViaProxyCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> CopyProxyCmdToClipboardCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> NotifyLeftClickCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> ShowWindowCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> HideWindowCmd { get; }

    #region System Proxy

    [Reactive]
    public partial bool BlSystemProxyClear { get; set; }

    [Reactive]
    public partial bool BlSystemProxySet { get; set; }

    [Reactive]
    public partial bool BlSystemProxyNothing { get; set; }

    [Reactive]
    public partial bool BlSystemProxyPac { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> SystemProxyClearCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SystemProxySetCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SystemProxyNothingCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SystemProxyPacCmd { get; }

    [Reactive]
    public partial bool BlRouting { get; set; }

    [Reactive]
    public partial int SystemProxySelected { get; set; }

    [Reactive]
    public partial bool BlSystemProxyPacVisible { get; set; }

    #endregion System Proxy

    #region UI

    [Reactive]
    public partial string InboundDisplay { get; set; }

    [Reactive]
    public partial string InboundLanDisplay { get; set; }

    [Reactive]
    public partial string RunningServerDisplay { get; set; }

    [Reactive]
    public partial string RunningServerToolTipText { get; set; }

    [Reactive]
    public partial string RunningInfoDisplay { get; set; }

    [Reactive]
    public partial string SpeedProxyDisplay { get; set; }

    [Reactive]
    public partial string SpeedDirectDisplay { get; set; }

    [Reactive]
    public partial bool EnableTun { get; set; }

    [Reactive]
    public partial bool BlIsNonWindows { get; set; }

    #endregion UI

    #region SvoRay simple mode

    /// <summary>
    /// Single source of truth for the simple main card and the tray icon.
    /// </summary>
    [Reactive]
    public partial ESvoRayConnectionState ConnectionState { get; set; }

    /// <summary>
    /// Reason shown to the user when <see cref="ConnectionState"/> is <see cref="ESvoRayConnectionState.Error"/>.
    /// Never contains a subscription URL or any profile credential.
    /// </summary>
    [Reactive]
    public partial string? ConnectionErrorText { get; set; }

    /// <summary>
    /// True while at least one profile exists; drives ImportState/ProfilesState.
    /// </summary>
    [Reactive]
    public partial bool HasProfiles { get; set; }

    /// <summary>
    /// True while at least one subscription exists; hides the update button for single imported profiles.
    /// </summary>
    [Reactive]
    public partial bool HasSubscriptions { get; set; }

    public void SetConnectionState(ESvoRayConnectionState state, string? errorText = null)
    {
        RxSchedulers.MainThreadScheduler.Schedule(() =>
        {
            ConnectionState = state;
            ConnectionErrorText = state == ESvoRayConnectionState.Error ? errorText : null;
        });
    }

    /// <summary>
    /// Puts both switches into the shape proxy mode connects with, and waits until the config
    /// holds them. Assigning the reactive properties only schedules that work, and the caller
    /// reloads right after: the reload decides whether the core may run and reads the switches,
    /// so it has to see the final pair rather than whichever half arrived first.
    /// </summary>
    public async Task ConnectViaSystemProxy()
    {
        // The subscription bails out when the config already agrees, which is what keeps the
        // tunnel switch from asking for a reload of its own here.
        _config.TunModeItem.EnableTun = false;
        EnableTun = false;
        await SetListenerType(ESysProxyType.ForcedChange);
        await ConfigHandler.SaveConfig(_config);
    }

    /// <summary>
    /// Clears the system proxy and waits for it, leaving the tunnel alone. TUN mode routes at the
    /// adapter and never wants the Windows proxy setting behind it, so it has to be down before
    /// the tunnel comes up rather than whenever the reactive property happens to be serviced.
    /// </summary>
    public async Task ClearSystemProxy()
    {
        await SetListenerType(ESysProxyType.ForcedClear);
    }

    /// <summary>
    /// Takes both switches down and stops the core, whichever mode was in use.
    /// Off has to mean off on the server as well: the core holds its own connections to the
    /// endpoint and the app keeps reaching for its proxy port on a timer, so a core left running
    /// goes on reporting the account as online long after the switch was turned off.
    /// </summary>
    public async Task Disconnect()
    {
        _config.TunModeItem.EnableTun = false;
        EnableTun = false;
        await SetListenerType(ESysProxyType.ForcedClear);
        await ConfigHandler.SaveConfig(_config);
        await CoreManager.Instance.CoreStop();
        SetConnectionState(ESvoRayConnectionState.Off);
    }

    #endregion SvoRay simple mode

    public StatusBarViewModel()
    {
        _config = AppManager.Instance.Config;
        SelectedRouting = new();
        SelectedServer = new();
        RunningServerToolTipText = GetRunningServerToolTipText("-");
        BlSystemProxyPacVisible = Utils.IsWindows();
        BlIsNonWindows = Utils.IsNonWindows();

        if (_config.TunModeItem.EnableTun && AllowEnableTun())
        {
            EnableTun = true;
        }
        else
        {
            _config.TunModeItem.EnableTun = EnableTun = false;
        }

        // The initial reload confirms whether the core actually came up.
        ConnectionState = MainWindowViewModel.IsSvoRayConnectionRequested(_config)
            ? ESvoRayConnectionState.Connecting
            : ESvoRayConnectionState.Off;

        #region WhenAnyValue && ReactiveCommand

        this.WhenAnyValue(x => x.SelectedRouting)
            .Where(y => y != null && !y.Remarks.IsNullOrEmpty())
            .SubscribeAsync(async _ => await RoutingSelectedChangedAsync());

        this.WhenAnyValue(x => x.SelectedServer)
            .Where(y => y != null && !y.Text.IsNullOrEmpty())
            .Subscribe(_ => ServerSelectedChanged());

        SystemProxySelected = (int)_config.SystemProxyItem.SysProxyType;
        this.WhenAnyValue(x => x.SystemProxySelected)
            .Where(y => y >= 0)
            .SubscribeAsync(async _ => await DoSystemProxySelected());

        this.WhenAnyValue(x => x.EnableTun)
            .SubscribeAsync(async _ => await DoEnableTun());

        CopyProxyCmdToClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await CopyProxyCmdToClipboard();
        });

        NotifyLeftClickCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            ShowHideWindowRequested.Publish(null);
            await Task.CompletedTask;
        });
        ShowWindowCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            ShowHideWindowRequested.Publish(true);
            await Task.CompletedTask;
        });
        HideWindowCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            ShowHideWindowRequested.Publish(false);
            await Task.CompletedTask;
        });

        AddServerViaClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
            {
                await AddServerViaClipboard();
            });
        AddServerViaScanCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerViaScan();
        });
        SubUpdateCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(false);
        });
        SubUpdateViaProxyCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(true);
        });

        //System proxy
        SystemProxyClearCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.ForcedClear);
        });
        SystemProxySetCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.ForcedChange);
        });
        SystemProxyNothingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.Unchanged);
        });
        SystemProxyPacCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.Pac);
        });

        #endregion WhenAnyValue && ReactiveCommand

        #region AppEvents

        AppEvents.DispatcherStatisticsRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async result => await UpdateStatistics(result));

        AppEvents.SysProxyChangeRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async result => await SetListenerType(result));

        #endregion AppEvents

        _ = Init();
    }

    private async Task Init()
    {
        await ConfigHandler.InitBuiltinRouting(_config);
        await RefreshRoutingsMenu();
        await InboundDisplayStatus();
        // The view refreshes its icon on initialization; interaction handlers are not registered yet.
        await ChangeSystemProxyAsync(_config.SystemProxyItem.SysProxyType, false);

        BlRouting = true;
    }

    private async Task CopyProxyCmdToClipboard()
    {
        var cmd = Utils.IsWindows() ? "set" : "export";
        var address = $"{Global.Loopback}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}";

        var sb = new StringBuilder();
        sb.AppendLine($"{cmd} http_proxy={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} https_proxy={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} all_proxy={Global.Socks5Protocol}{address}");
        sb.AppendLine("");
        sb.AppendLine($"{cmd} HTTP_PROXY={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} HTTPS_PROXY={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} ALL_PROXY={Global.Socks5Protocol}{address}");

        await SetClipboardDataInteraction.HandleSafe(sb.ToString());
    }

    private async Task AddServerViaClipboard()
    {
        AddServerViaClipboardRequested.Publish();
        await Task.Delay(1000);
    }

    private async Task AddServerViaScan()
    {
        AddServerViaScanRequested.Publish();
        await Task.Delay(1000);
    }

    private async Task UpdateSubscriptionProcess(bool blProxy)
    {
        SubscriptionsUpdateRequested.Publish(blProxy);
        await Task.Delay(1000);
    }

    public async Task RefreshServersBiz()
    {
        await RefreshServersMenu();
        await RefreshSimpleAvailabilityBiz();

        //display running server
        var running = await ConfigHandler.GetDefaultServer(_config);
        if (running != null)
        {
            RunningServerDisplay = running.GetSummary();
            RunningServerToolTipText = GetRunningServerToolTipText(RunningServerDisplay);
        }
        else
        {
            RunningServerDisplay = ResUI.CheckServerSettings;
            RunningServerToolTipText = GetRunningServerToolTipText(RunningServerDisplay);
        }
    }

    /// <summary>
    /// Recomputes the ImportState/ProfilesState flags used by the simple main card.
    /// </summary>
    private async Task RefreshSimpleAvailabilityBiz()
    {
        HasProfiles = (await AppManager.Instance.ProfileItems(""))?.Count > 0;
        HasSubscriptions = (await AppManager.Instance.SubItems())?.Any(t => t.Url.IsNotEmpty()) == true;
    }

    private string GetRunningServerToolTipText(string serverInfo)
    {
        return Utils.IsLinux() ? Global.AppName : serverInfo;
    }

    private async Task RefreshServersMenu()
    {
        var (models, grouped) = await BuildServerItemsAsync();

        // The simple-mode selector always lists every profile; only the tray submenu is capped.
        SimpleServers.ReplaceRange(models);
        SimpleServerList.ReplaceRange(grouped);

        BlServers = models.Count <= _config.GuiItem.TrayMenuServersLimit;
        Servers.ReplaceRange(BlServers ? models : []);

        // Assign the selection after both collections are populated so the
        // bound ComboBoxes do not momentarily fall back to a null selection.
        var selected = models.FirstOrDefault(t => t.ID == _config.IndexId);
        if (selected is not null)
        {
            SelectedServer = selected;
        }
    }

    private static async Task<(List<SimpleServerItem> Servers, List<SimpleServerItem> Grouped)> BuildServerItemsAsync()
    {
        var profiles = await AppManager.Instance.ProfileModels("", "") ?? [];
        var subs = await AppManager.Instance.SubItems() ?? [];
        return SvoRayServerList.Build(profiles, subs, ResUI.SvoRayStandaloneServers, ResUI.SvoRayAddSourceItem);
    }

    private void ServerSelectedChanged()
    {
        if (SelectedServer == null)
        {
            return;
        }
        if (SelectedServer.ID.IsNullOrEmpty())
        {
            return;
        }
        SetDefaultServerRequested.Publish(SelectedServer.ID);
    }

    public async Task<AvailabilityCheckResult?> TestServerAvailability()
    {
        var item = await ConfigHandler.GetDefaultServer(_config);
        if (item == null)
        {
            return null;
        }

        await TestServerAvailabilitySub(ResUI.Speedtesting);

        var result = await Task.Run(ConnectionHandler.RunAvailabilityCheck);
        var msg = string.Format(ResUI.TestMeOutput, result.Time, result.Ip);

        var ip = result.GetValidIp();
        if (ip.IsNotEmpty())
        {
            ProfileExManager.Instance.SetTestIpInfo(item.IndexId, ip);
        }
        if (result.Time > 0)
        {
            ProfileExManager.Instance.SetTestDelay(item.IndexId, result.Time);
        }

        NoticeManager.Instance.SendMessageEx(msg);
        await TestServerAvailabilitySub(msg);
        return result;
    }

    private async Task TestServerAvailabilitySub(string msg)
    {
        RxSchedulers.MainThreadScheduler.Schedule(() =>
        {
            _ = TestServerAvailabilityResult(msg);
        });
        await Task.CompletedTask;
    }

    public async Task TestServerAvailabilityResult(string msg)
    {
        RunningInfoDisplay = msg;
        await Task.CompletedTask;
    }

    #region System proxy and Routings

    private async Task SetListenerType(ESysProxyType type)
    {
        if (_config.SystemProxyItem.SysProxyType == type)
        {
            return;
        }
        _config.SystemProxyItem.SysProxyType = type;
        await ChangeSystemProxyAsync(type, true);
        NoticeManager.Instance.SendMessageEx($"{ResUI.TipChangeSystemProxy} - {_config.SystemProxyItem.SysProxyType}");

        SystemProxySelected = (int)_config.SystemProxyItem.SysProxyType;
        await ConfigHandler.SaveConfig(_config);
    }

    public async Task ChangeSystemProxyAsync(ESysProxyType type, bool blChange)
    {
        await SysProxyHandler.UpdateSysProxy(_config, false);

        BlSystemProxyClear = type == ESysProxyType.ForcedClear;
        BlSystemProxySet = type == ESysProxyType.ForcedChange;
        BlSystemProxyNothing = type == ESysProxyType.Unchanged;
        BlSystemProxyPac = type == ESysProxyType.Pac;

        if (blChange)
        {
            await DispatcherRefreshIconInteraction.HandleSafe(RxVoid.Default);
        }
    }

    public async Task RefreshRoutingsMenu()
    {
        var routings = await AppManager.Instance.RoutingItems();

        RoutingItems.ReplaceRange(routings);

        SelectedRouting = routings.FirstOrDefault(t => t.IsActive == true);
    }

    private async Task RoutingSelectedChangedAsync()
    {
        if (SelectedRouting == null)
        {
            return;
        }

        var item = await AppManager.Instance.GetRoutingItem(SelectedRouting?.Id);
        if (item is null)
        {
            return;
        }

        if (await ConfigHandler.SetDefaultRouting(_config, item) == 0)
        {
            NoticeManager.Instance.SendMessageEx(ResUI.TipChangeRouting);
            ReloadRequested.Publish();
            await DispatcherRefreshIconInteraction.HandleSafe(RxVoid.Default);
        }
    }

    private async Task DoSystemProxySelected()
    {
        if (_config.SystemProxyItem.SysProxyType == (ESysProxyType)SystemProxySelected)
        {
            return;
        }
        await SetListenerType((ESysProxyType)SystemProxySelected);
    }

    private async Task DoEnableTun()
    {
        if (_config.TunModeItem.EnableTun == EnableTun)
        {
            return;
        }

        _config.TunModeItem.EnableTun = EnableTun;
        SetConnectionState(EnableTun ? ESvoRayConnectionState.Connecting : ESvoRayConnectionState.Off);

        if (EnableTun && AllowEnableTun() == false)
        {
            // When running as a non-administrator, reboot to administrator mode
            if (Utils.IsWindows())
            {
                _config.TunModeItem.EnableTun = false;
                await AppManager.Instance.RebootAsAdmin();
                return;
            }
            else
            {
                var password = await PasswordInputInteraction.HandleSafe(RxVoid.Default);
                if (password.IsNullOrEmpty())
                {
                    _config.TunModeItem.EnableTun = false;
                    SetConnectionState(ESvoRayConnectionState.Off);
                    return;
                }
            }
        }

        await ConfigHandler.SaveConfig(_config);
        ReloadRequested.Publish();
    }

    private bool AllowEnableTun()
    {
        if (Utils.IsWindows())
        {
            return Utils.IsAdministrator();
        }
        else if (Utils.IsLinux())
        {
            return AppManager.Instance.LinuxSudoPwd.IsNotEmpty();
        }
        else if (Utils.IsMacOS())
        {
            return AppManager.Instance.LinuxSudoPwd.IsNotEmpty();
        }
        return false;
    }

    #endregion System proxy and Routings

    #region UI

    public async Task InboundDisplayStatus()
    {
        StringBuilder sb = new();
        sb.Append($"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}");
        if (_config.Inbound.First().SecondLocalPortEnabled)
        {
            sb.Append($",{AppManager.Instance.GetLocalPort(EInboundProtocol.socks2)}");
        }
        sb.Append(']');
        InboundDisplay = $"{ResUI.LabLocal}:{sb}";

        if (_config.Inbound.First().AllowLANConn)
        {
            var lan = _config.Inbound.First().NewPort4LAN
                ? $"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks3)}]"
                : $"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}]";
            InboundLanDisplay = $"{ResUI.LabLAN}:{lan}";
        }
        else
        {
            InboundLanDisplay = $"{ResUI.LabLAN}:{Global.None}";
        }
        await Task.CompletedTask;
    }

    public async Task UpdateStatistics(ServerSpeedItem update)
    {
        if (!_config.GuiItem.DisplayRealTimeSpeed)
        {
            return;
        }

        try
        {
            if (AppManager.Instance.IsRunningCore(ECoreType.sing_box))
            {
                SpeedProxyDisplay = string.Format(ResUI.SpeedDisplayText, EInboundProtocol.mixed, Utils.HumanFy(update.ProxyUp), Utils.HumanFy(update.ProxyDown));
                SpeedDirectDisplay = string.Empty;
            }
            else
            {
                SpeedProxyDisplay = string.Format(ResUI.SpeedDisplayText, Global.ProxyTag, Utils.HumanFy(update.ProxyUp), Utils.HumanFy(update.ProxyDown));
                SpeedDirectDisplay = string.Format(ResUI.SpeedDisplayText, Global.DirectTag, Utils.HumanFy(update.DirectUp), Utils.HumanFy(update.DirectDown));
            }
        }
        catch
        {
        }
        await Task.CompletedTask;
    }

    #endregion UI
}
