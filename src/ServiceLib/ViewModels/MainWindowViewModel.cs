namespace ServiceLib.ViewModels;

public partial class MainWindowViewModel : MyReactiveObject
{
    public Interaction<RxVoid, string?> ReadTextFromClipboardInteraction { get; } = new();
    public Interaction<RxVoid, byte[]?> ScanScreenInteraction { get; } = new();
    public Interaction<RxVoid, string?> BrowseImageFileInteraction { get; } = new();
    public Interaction<bool?, RxVoid> ShowHideWindowInteraction { get; } = new();

    public bool DesignMode { get; set; }

    public ProfilesViewModel ProfilesViewModel { get; } = new();
    public MsgViewModel MsgViewModel { get; } = new();
    public ClashProxiesViewModel ClashProxiesViewModel { get; } = new();
    public ClashConnectionsViewModel ClashConnectionsViewModel { get; } = new();
    public CheckUpdateViewModel CheckUpdateViewModel { get; } = new();
    public BackupAndRestoreViewModel BackupAndRestoreViewModel { get; } = new();
    public StatusBarViewModel StatusBarViewModel { get; } = StatusBarViewModel.Instance;

    #region Menu

    //servers
    public ReactiveCommand<RxVoid, RxVoid> AddVmessServerCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> AddVlessServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddShadowsocksServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddSocksServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddHttpServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddTrojanServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddHysteria2ServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddTuicServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddWireguardServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddAnytlsServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddNaiveServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddMasqueServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddCustomServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddCustomOutboundServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddPolicyGroupServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddProxyChainServerCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddServerViaClipboardCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddServerViaScanCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddServerViaImageCmd { get; }

    //Subscription
    public ReactiveCommand<RxVoid, RxVoid> SubSettingCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> SubUpdateCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SubUpdateViaProxyCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SubGroupUpdateCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SubGroupUpdateViaProxyCmd { get; }

    //Setting
    public ReactiveCommand<RxVoid, RxVoid> OptionSettingCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> RoutingSettingCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> DNSSettingCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> FullConfigTemplateCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> GlobalHotkeySettingCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> RebootAsAdminCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> ClearServerStatisticsCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> OpenTheFileLocationCmd { get; }

    //Presets
    public ReactiveCommand<RxVoid, RxVoid> RegionalPresetDefaultCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> RegionalPresetRussiaCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> RegionalPresetIranCmd { get; }

    public ReactiveCommand<RxVoid, RxVoid> ReloadCmd { get; }

    [Reactive]
    public partial bool BlReloadEnabled { get; set; }

    [Reactive]
    public partial bool ShowClashUI { get; set; }

    [Reactive]
    public partial int TabMainSelectedIndex { get; set; }

    [Reactive] public partial bool BlIsWindows { get; set; }

    [Reactive] public partial bool BlNewUpdate { get; set; }

    [Reactive] public partial EGirdOrientation MainGirdOrientation { get; set; }

    #endregion Menu

    #region Init

    public MainWindowViewModel()
    {
        _config = AppManager.Instance.Config;
        BlIsWindows = Utils.IsWindows();
        MainGirdOrientation = _config.UiItem.MainGirdOrientation;

        #region WhenAnyValue && ReactiveCommand

        //servers
        AddVmessServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.VMess);
        });
        AddVlessServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.VLESS);
        });
        AddShadowsocksServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Shadowsocks);
        });
        AddSocksServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.SOCKS);
        });
        AddHttpServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.HTTP);
        });
        AddTrojanServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Trojan);
        });
        AddHysteria2ServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Hysteria2);
        });
        AddTuicServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.TUIC);
        });
        AddWireguardServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.WireGuard);
        });
        AddAnytlsServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Anytls);
        });
        AddNaiveServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Naive);
        });
        AddMasqueServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.MASQUE);
        });
        AddCustomServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Custom);
        });
        AddCustomOutboundServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.Outbound);
        });
        AddPolicyGroupServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.PolicyGroup);
        });
        AddProxyChainServerCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerAsync(EConfigType.ProxyChain);
        });
        AddServerViaClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerViaClipboardAsync(null);
        });
        AddServerViaScanCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerViaScanAsync();
        });
        AddServerViaImageCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerViaImageAsync();
        });

        //Subscription
        SubSettingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SubSettingAsync();
        });

        SubUpdateCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess("", false);
        });
        SubUpdateViaProxyCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess("", true);
        });
        SubGroupUpdateCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(_config.SubIndexId, false);
        });
        SubGroupUpdateViaProxyCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(_config.SubIndexId, true);
        });

        //Setting
        OptionSettingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await OptionSettingAsync();
        });
        RoutingSettingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RoutingSettingAsync();
        });
        DNSSettingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await DNSSettingAsync();
        });
        FullConfigTemplateCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await FullConfigTemplateAsync();
        });
        GlobalHotkeySettingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            var globalHotkeySettingViewModel = new GlobalHotkeySettingViewModel();
            if (await AppManager.Instance.WindowDialog.ShowDialogAsync(globalHotkeySettingViewModel) == true)
            {
                NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
            }
        });
        RebootAsAdminCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AppManager.Instance.RebootAsAdmin();
        });
        ClearServerStatisticsCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ClearServerStatistics();
        });
        OpenTheFileLocationCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await OpenTheFileLocation();
        });

        ReloadCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await Reload();
        });

        RegionalPresetDefaultCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ApplyRegionalPreset(EPresetType.Default);
        });

        RegionalPresetRussiaCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ApplyRegionalPreset(EPresetType.Russia);
        });

        RegionalPresetIranCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await ApplyRegionalPreset(EPresetType.Iran);
        });

        #endregion WhenAnyValue && ReactiveCommand

        #region AppEvents

        AppEvents.AddServerViaClipboardRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async _ => await AddServerViaClipboardAsync(null));

        AppEvents.HasUpdateNotified
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(bl => BlNewUpdate = bl);

        #endregion AppEvents

        ProfilesViewModel.RefreshServersRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async _ => await RefreshServersDispatcherAsync());

        var vmReloadRequestedList = new List<IObservable<RxVoid>>
        {
            ProfilesViewModel.ReloadRequested.AsObservable(),
            StatusBarViewModel.ReloadRequested.AsObservable(),
            CheckUpdateViewModel.ReloadRequested.AsObservable(),
        };

        foreach (var reloadRequested in vmReloadRequestedList)
        {
            reloadRequested
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .SubscribeAsync(async _ => await Reload());
        }

        StatusBarViewModel.AddServerViaScanRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async _ => await AddServerViaScanAsync());

        StatusBarViewModel.AddServerViaClipboardRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async _ => await AddServerViaClipboardAsync(null));

        StatusBarViewModel.ShowHideWindowRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async blShow =>
            {
                await ShowHideWindowInteraction.HandleSafe(blShow);
            });

        StatusBarViewModel.SetDefaultServerRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async indexId => await ProfilesViewModel.SetDefaultServer(indexId));

        StatusBarViewModel.SubscriptionsUpdateRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async blProxy => await UpdateSubscriptionProcess("", blProxy));

        _ = InitLoggedAsync();
    }

    /// <summary>
    /// Init runs fire-and-forget, so a throw inside it used to vanish: the steps after the
    /// failing one never ran, which left the server list empty and the certificate policy
    /// uninitialised, with nothing in the log to say why.
    /// </summary>
    private async Task InitLoggedAsync()
    {
        try
        {
            await Init();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("MainWindowViewModel.Init", ex);
        }
    }

    private async Task Init()
    {
        AppManager.Instance.ShowInTaskbar = true;

        if (DesignMode)
        {
            return;
        }

        //await ConfigHandler.InitBuiltinRouting(_config);
        await ConfigHandler.InitBuiltinDNS(_config);
        await ConfigHandler.InitBuiltinFullConfigTemplate(_config);
        await ProfileExManager.Instance.Init();
        await CoreManager.Instance.Init(_config, UpdateHandler);
        await CertPemManager.Instance.Init(_config);
        await ConfigHandler.MigrateSvoRaySubscriptions(_config);
        TaskManager.Instance.RegUpdateTask(_config, UpdateTaskHandler);

        if (_config.GuiItem.EnableStatistics || _config.GuiItem.DisplayRealTimeSpeed)
        {
            await StatisticsManager.Instance.Init(_config, UpdateStatisticsHandler);
        }
        await RefreshServersDispatcherAsync();

        await Reload();
    }

    #endregion Init

    #region Actions

    private async Task UpdateHandler(bool notify, string msg)
    {
        NoticeManager.Instance.SendMessage(msg);
        if (notify)
        {
            NoticeManager.Instance.Enqueue(msg);
        }
        await Task.CompletedTask;
    }

    private async Task UpdateTaskHandler(bool success, string msg)
    {
        NoticeManager.Instance.SendMessageEx(msg);
        if (success)
        {
            await RefreshServersDispatcherAsync();

            // The subscription may have brought other rules with it (or a selected server from
            // another provider): the simple profile is rebuilt before the config is compared.
            await SvoRayRoutingHandler.RefreshIfActiveAsync(_config);
            // A refresh runs every few hours by itself. The same list and rules again must not
            // restart the core and drop every call and game; a changed server set, selected
            // server or routing still reloads.
            await ReloadIfChanged();

            if (_config.UiItem.EnableAutoAdjustMainLvColWidth)
            {
                await ProfilesViewModel.AdjustMainLvColWidth();
            }
        }
    }

    private async Task UpdateStatisticsHandler(ServerSpeedItem update)
    {
        if (!AppManager.Instance.ShowInTaskbar)
        {
            return;
        }
        AppEvents.DispatcherStatisticsRequested.Publish(update);
        await Task.CompletedTask;
    }

    #endregion Actions

    #region Servers && Groups

    private readonly SemaphoreSlim _refreshServersSemaphore = new(1, 1);

    private async Task RefreshServers()
    {
        await ProfilesViewModel.RefreshServersBiz();
        await StatusBarViewModel.RefreshServersBiz();

        // await Task.Delay(200);
    }

    private async Task RefreshServersDispatcherAsync()
    {
        //await Observable.Start(async () => await RefreshServers(), RxSchedulers.MainThreadScheduler);
        await _refreshServersSemaphore.WaitAsync();
        try
        {
            await Signal.FromAsync(async () =>
            {
                await RefreshServers();
                return RxVoid.Default;
            })
                .SubscribeOn(RxSchedulers.MainThreadScheduler)
                .ToTask();
        }
        finally
        {
            _refreshServersSemaphore.Release();
        }
    }

    private async Task RefreshSubscriptions()
    {
        //await Observable.Start(async () => await ProfilesViewModel.RefreshSubscriptions(), RxSchedulers.MainThreadScheduler);
        await Signal.FromAsync(async () =>
            {
                await ProfilesViewModel.RefreshSubscriptions();
                return RxVoid.Default;
            })
            .SubscribeOn(RxSchedulers.MainThreadScheduler)
            .ToTask();
    }

    #endregion Servers && Groups

    #region Add Servers

    public async Task AddServerAsync(EConfigType eConfigType)
    {
        ProfileItem item = new()
        {
            Subid = _config.SubIndexId,
            ConfigType = eConfigType,
            IsSub = false,
        };

        bool? ret = false;
        if (eConfigType is EConfigType.Custom or EConfigType.Outbound)
        {
            var addServer2ViewModel = new AddServer2ViewModel(item);
            ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(addServer2ViewModel);
        }
        else if (eConfigType.IsGroupType())
        {
            var addGroupServerViewModel = new AddGroupServerViewModel(item);
            ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(addGroupServerViewModel);
        }
        else
        {
            var addServerViewModel = new AddServerViewModel(item);
            ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(addServerViewModel);
        }
        if (ret == true)
        {
            await RefreshServersDispatcherAsync();
            if (item.IndexId == _config.IndexId)
            {
                await Reload();
            }
        }
    }

    public async Task AddServerViaClipboardAsync(string? clipboardData)
    {
        var stringData = clipboardData;
        if (clipboardData == null)
        {
            var result = await ReadTextFromClipboardInteraction.HandleSafe(RxVoid.Default);
            if (result.IsNullOrEmpty())
            {
                NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
                return;
            }
            stringData = result;
        }
        var ret = await ConfigHandler.AddBatchServers(_config, stringData, _config.SubIndexId, false);
        if (ret > 0)
        {
            await RefreshSubscriptions();
            await RefreshServersDispatcherAsync();
            NoticeManager.Instance.Enqueue(string.Format(ResUI.SuccessfullyImportedServerViaClipboard, ret));
        }
        else
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
        }
    }

    public async Task AddServerViaScanAsync()
    {
        var result = await ScanScreenInteraction.HandleSafe(RxVoid.Default);
        await ScanScreenResult(result);
    }

    public async Task ScanScreenResult(byte[]? bytes)
    {
        var result = QRCodeUtils.ParseBarcode(bytes);
        await AddScanResultAsync(result);
    }

    public async Task AddServerViaImageAsync()
    {
        var imageFileName = await BrowseImageFileInteraction.HandleSafe(RxVoid.Default);
        await ScanImageResult(imageFileName);
    }

    public async Task ScanImageResult(string fileName)
    {
        if (fileName.IsNullOrEmpty())
        {
            return;
        }

        var result = QRCodeUtils.ParseBarcode(fileName);
        await AddScanResultAsync(result);
    }

    private async Task AddScanResultAsync(string? result)
    {
        if (result.IsNullOrEmpty())
        {
            NoticeManager.Instance.Enqueue(ResUI.NoValidQRcodeFound);
        }
        else
        {
            var ret = await ConfigHandler.AddBatchServers(_config, result, _config.SubIndexId, false);
            if (ret > 0)
            {
                await RefreshSubscriptions();
                await RefreshServersDispatcherAsync();
                NoticeManager.Instance.Enqueue(ResUI.SuccessfullyImportedServerViaScan);
            }
            else
            {
                NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
            }
        }
    }

    #endregion Add Servers

    #region Subscription

    /// <summary>
    /// Imports a subscription link or server links from the simple screen.
    /// <paramref name="replaceCurrent"/> is "Replace": once the new source has servers, the
    /// source of the server selected before - its whole subscription, or that one server if it
    /// was added on its own - is removed. Without it ("Add") nothing is removed, and both stay
    /// in the selector. Importing a link that is already there never creates a copy.
    /// </summary>
    public async Task<bool> ImportSubscriptionAndRefreshAsync(string url, bool replaceCurrent = false)
    {
        var value = url.TrimEx();
        var uri = Utils.TryUri(value);
        var isSubscription = uri is not null
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        var previous = replaceCurrent ? await AppManager.Instance.GetProfileItem(_config.IndexId) : null;
        var previousSubid = previous?.Subid.IsNotEmpty() == true ? previous.Subid : null;

        if (!isSubscription)
        {
            var selected = await ConfigHandler.AddStandaloneServers(_config, value, previousSubid,
                previousSubid is null ? previous?.IndexId : null);
            if (selected is null)
            {
                NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
                return false;
            }

            await ConfigHandler.SetDefaultServerIndex(_config, selected);
            await RemovePreviousSourceAsync(previous, keepSubid: null, keepIndexId: selected);
            await SvoRayRoutingHandler.RefreshIfActiveAsync(_config);
            await RefreshSimpleUiAsync();
            NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
            return true;
        }

        if (uri!.Scheme == Uri.UriSchemeHttp && !Utils.IsPrivateNetwork(uri.IdnHost))
        {
            NoticeManager.Instance.Enqueue(ResUI.InsecureUrlProtocol);
            return false;
        }

        var subscriptions = await AppManager.Instance.SubItems() ?? [];
        var subscription = subscriptions.FirstOrDefault(item =>
            string.Equals(item.Url.TrimEx(), value, StringComparison.OrdinalIgnoreCase));

        var addedNow = subscription is null;
        if (subscription is null && await ConfigHandler.AddSubItem(_config, value) != 0)
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
            return false;
        }

        subscription ??= (await AppManager.Instance.SubItems())?.FirstOrDefault(item =>
            string.Equals(item.Url.TrimEx(), value, StringComparison.OrdinalIgnoreCase));
        if (subscription is null)
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
            return false;
        }

        await UpdateSubscriptionProcess(subscription.Id, false);

        var profiles = await AppManager.Instance.ProfileItems(subscription.Id);
        var success = profiles is { Count: > 0 };
        if (success)
        {
            _config.SubIndexId = subscription.Id;
            if (profiles!.All(item => item.IndexId != _config.IndexId))
            {
                var firstUsableProfile = profiles.FirstOrDefault(item => item.Port > 0);
                await ConfigHandler.SetDefaultServerIndex(_config, firstUsableProfile?.IndexId);
            }
            await RemovePreviousSourceAsync(previous, keepSubid: subscription.Id, keepIndexId: null);
        }
        else if (addedNow)
        {
            // Nothing came down for a link added just now: it is not kept as an empty entry,
            // which the scheduler would fill in later next to the one the user adds instead.
            await ConfigHandler.RemoveSubscriptionIfEmpty(_config, subscription.Id);
        }

        await RefreshSubscriptions();
        await RefreshServersDispatcherAsync();
        if (success)
        {
            // The selected server may now belong to another subscription with other rules.
            await SvoRayRoutingHandler.RefreshIfActiveAsync(_config);
            await Reload();
        }
        NoticeManager.Instance.Enqueue(success ? ResUI.OperationSuccess : ResUI.MsgFailedImportSubscription);
        return success;
    }

    /// <summary>
    /// The second half of "Replace": drops what the previously selected server came from, unless
    /// the import turned out to be that same source (the same link pasted again).
    /// </summary>
    private async Task RemovePreviousSourceAsync(ProfileItem? previous, string? keepSubid, string? keepIndexId)
    {
        if (previous is null)
        {
            return;
        }

        if (previous.Subid.IsNotEmpty() && await AppManager.Instance.GetSubItem(previous.Subid) is not null)
        {
            if (previous.Subid != keepSubid)
            {
                await ConfigHandler.DeleteSubItem(_config, previous.Subid);
            }
        }
        else if (previous.IndexId != keepIndexId)
        {
            await ConfigHandler.RemoveServers(_config, [previous]);
        }
    }

    /// <summary>
    /// Removes a subscription with all its servers, or a single server added on its own, from the
    /// simple screen. When the selected server goes with it, the first remaining one is selected,
    /// and a running connection is rebuilt on it or stopped when nothing is left.
    /// </summary>
    public async Task<bool> RemoveSourceAsync(string? subId, string? indexId)
    {
        if (subId.IsNotEmpty())
        {
            await ConfigHandler.DeleteSubItem(_config, subId!);
        }
        else if (indexId.IsNotEmpty() && await AppManager.Instance.GetProfileItem(indexId!) is { } profile)
        {
            await ConfigHandler.RemoveServers(_config, [profile]);
        }
        else
        {
            return false;
        }

        if (await AppManager.Instance.GetProfileItem(_config.IndexId) is null)
        {
            var next = (await AppManager.Instance.ProfileItems("") ?? []).FirstOrDefault(t => t.Port > 0);
            await ConfigHandler.SetDefaultServerIndex(_config, next?.IndexId);
            await SvoRayRoutingHandler.RefreshIfActiveAsync(_config);
        }

        await RefreshSimpleUiAsync();
        return true;
    }

    public Task RefreshSimpleUiAsync() => RefreshSimpleUiAsync(keepCoreIfUnchanged: false);

    /// <param name="keepCoreIfUnchanged">True after a subscription refresh: the core restarts only when its config changed.</param>
    public async Task RefreshSimpleUiAsync(bool keepCoreIfUnchanged)
    {
        await RefreshSubscriptions();
        await RefreshServersDispatcherAsync();
        await Reload(keepCoreIfUnchanged);
    }

    private async Task SubSettingAsync()
    {
        var subSettingViewModel = new SubSettingViewModel();
        if (await AppManager.Instance.WindowDialog.ShowDialogAsync(subSettingViewModel) == true)
        {
            await RefreshSubscriptions();
        }
    }

    public async Task<bool> UpdateSubscriptionProcess(string subId, bool blProxy)
    {
        return await Task.Run(async () => await SubscriptionHandler.UpdateProcess(_config, subId, blProxy, UpdateTaskHandler));
    }

    #endregion Subscription

    #region Setting

    private async Task OptionSettingAsync()
    {
        var settingViewModel = new OptionSettingViewModel();
        var ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(settingViewModel);
        if (ret == true)
        {
            MainGirdOrientation = _config.UiItem.MainGirdOrientation;
            RxSchedulers.MainThreadScheduler.Schedule(async () =>
            {
                await StatusBarViewModel.InboundDisplayStatus();
            });
            await Reload();
        }
    }

    private async Task RoutingSettingAsync()
    {
        var routingSettingViewModel = new RoutingSettingViewModel();
        var ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(routingSettingViewModel);
        if (ret == true)
        {
            await ConfigHandler.InitBuiltinRouting(_config);
            RxSchedulers.MainThreadScheduler.Schedule(async () =>
            {
                await StatusBarViewModel.RefreshRoutingsMenu();
            });
            await Reload();
        }
    }

    private async Task DNSSettingAsync()
    {
        var dnsSettingViewModel = new DNSSettingViewModel();
        var ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(dnsSettingViewModel);
        if (ret == true)
        {
            await Reload();
        }
    }

    private async Task FullConfigTemplateAsync()
    {
        var fullConfigTemplateViewModel = new FullConfigTemplateViewModel();
        var ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(fullConfigTemplateViewModel);
        if (ret == true)
        {
            await Reload();
        }
    }

    private async Task ClearServerStatistics()
    {
        await StatisticsManager.Instance.ClearAllServerStatistics();
        await RefreshServersDispatcherAsync();
    }

    private async Task OpenTheFileLocation()
    {
        var path = Utils.StartupPath();
        if (Utils.IsWindows())
        {
            ProcUtils.ProcessStart(path);
        }
        else if (Utils.IsLinux())
        {
            ProcUtils.ProcessStart("xdg-open", path);
        }
        else if (Utils.IsMacOS())
        {
            ProcUtils.ProcessStart("open", path);
        }
        await Task.CompletedTask;
    }

    #endregion Setting

    #region core job

    private bool _hasNextReloadJob = false;

    /// <summary>Whether a queued reload was asked for as a full one, not only "if changed".</summary>
    private bool _nextReloadForced = false;
    private readonly SemaphoreSlim _reloadSemaphore = new(1, 1);

    public Task Reload() => Reload(keepIfUnchanged: false);

    /// <summary>
    /// A reload nobody asked for by hand, after a subscription refresh: the running core is left
    /// alone when the config it would get is the one it runs, so calls and games are not dropped
    /// every few hours for nothing. A changed server list, server or routing still reloads.
    /// </summary>
    public Task ReloadIfChanged() => Reload(keepIfUnchanged: true);

    private async Task Reload(bool keepIfUnchanged)
    {
        //If there are unfinished reload job, marked with next job.
        if (!await _reloadSemaphore.WaitAsync(0))
        {
            _hasNextReloadJob = true;
            _nextReloadForced |= !keepIfUnchanged;
            return;
        }

        if (DesignMode)
        {
            _reloadSemaphore.Release();
            return;
        }

        try
        {
            SwitchTiming.Continue("reload started");
            SetReloadEnabled(false);

            RxSchedulers.MainThreadScheduler.Schedule(() =>
            {
                if (TabMainSelectedIndex < 0)
                {
                    TabMainSelectedIndex = 0;
                }
            });

            // A reload is what brings the core up, and plenty of things ask for one that have
            // nothing to do with connecting: startup, a different profile picked in the selector,
            // a routing or DNS change, an imported subscription. While the switch is off none of
            // them may start the core - it would open its own connections to the endpoint and the
            // server would list the account as online without the user having connected.
            if (!IsCoreWanted(_config))
            {
                await CoreManager.Instance.CoreStop();
                await SysProxyHandler.UpdateSysProxy(_config, false);
                return;
            }

            var profileItem = await ConfigHandler.GetDefaultServer(_config);
            if (profileItem == null)
            {
                NoticeManager.Instance.Enqueue(ResUI.CheckServerSettings);
                return;
            }
            var allResult = await CoreConfigContextBuilder.BuildAll(_config, profileItem);
            if (NoticeManager.Instance.NotifyValidatorResult(allResult.CombinedValidatorResult) && !allResult.Success)
            {
                return;
            }
            // A sing-box core is generated with the rest of the subscription behind a selector, so
            // the next switch within it changes the selector instead of restarting the core.
            var mainContext = await ServerSwitchCandidates.AttachAsync(allResult.MainResult.Context,
                allResult.PreSocksResult?.Context);
            SwitchTiming.Mark("config context built");

            var kept = false;
            await Task.Run(async () =>
            {
                // LoadCore returns once the new core answers on its proxy port (or the running
                // core has switched server), so no fixed settling delay is needed here.
                kept = await CoreManager.Instance.LoadCore(mainContext, allResult.PreSocksResult?.Context, keepIfUnchanged);
                if (kept)
                {
                    return;
                }
                _ = SwitchTiming.ProbeFirstTrafficAsync(AppManager.Instance.GetLocalPort(EInboundProtocol.socks),
                    _config.SpeedTestItem.SpeedPingTestUrl);
                await SysProxyHandler.UpdateSysProxy(_config, false);
                SwitchTiming.Mark("system proxy updated");
            });
            if (kept)
            {
                return;
            }
            RxSchedulers.MainThreadScheduler.Schedule(async () =>
            {
                var result = await StatusBarViewModel.TestServerAvailability();
                SwitchTiming.Mark("availability check finished");
                if (result == null || profileItem.IndexId.IsNullOrEmpty())
                {
                    return;
                }

                await ProfilesViewModel.SetSpeedTestResult(new()
                {
                    IndexId = profileItem.IndexId,
                    IpInfo = result.Ip,
                    Delay = result.Time > 0 ? result.Time.ToString() : null
                });
            });

            var showClashUI = AppManager.Instance.IsRunningCore(ECoreType.sing_box);
            if (showClashUI)
            {
                //await Observable.Start(async () =>
                //{
                //    await ClashProxiesViewModel.ProxiesReload();
                //}, RxSchedulers.MainThreadScheduler);
                await Signal.FromAsync(async () =>
                    {
                        await ClashProxiesViewModel.ProxiesReload();
                        return RxVoid.Default;
                    }).SubscribeOn(RxSchedulers.MainThreadScheduler)
                    .ToTask();
            }

            ReloadResult(showClashUI);
        }
        finally
        {
            SetReloadEnabled(true);
            UpdateSvoRayConnectionState();
            SwitchTiming.Mark("ui shows state");
            _reloadSemaphore.Release();
            //If there is a next reload job, execute it.
            if (_hasNextReloadJob)
            {
                var forced = _nextReloadForced;
                _hasNextReloadJob = false;
                _nextReloadForced = false;
                await Reload(keepIfUnchanged: !forced);
            }
        }
    }

    /// <summary>
    /// Reports the connection state the simple main card and the tray icon show.
    /// It reflects whether the core process actually came up, not just the requested TUN flag.
    /// Specific failure reasons keep arriving through the existing notice/snackbar channel.
    /// </summary>
    private void UpdateSvoRayConnectionState()
    {
        if (!IsSvoRayConnectionRequested(_config))
        {
            StatusBarViewModel.SetConnectionState(ESvoRayConnectionState.Off);
            return;
        }

        StatusBarViewModel.SetConnectionState(CoreManager.Instance.IsCoreRunning
            ? ESvoRayConnectionState.On
            : ESvoRayConnectionState.Error);
    }

    /// <summary>
    /// Catches a core that exited on its own while the screen says "connected": nothing else
    /// notices, and the user is left with no internet (or, with the kill switch, a blocked one)
    /// under a green "VPN on". Called on a timer by the window.
    /// </summary>
    public void CheckCoreAlive()
    {
        var reloading = _reloadSemaphore.CurrentCount == 0;
        if (!ShouldReportCoreStopped(reloading, IsSvoRayConnectionRequested(_config),
                CoreManager.Instance.IsCoreRunning, StatusBarViewModel.ConnectionState))
        {
            return;
        }
        Logging.SaveLog("SvoRay: the core exited on its own");
        StatusBarViewModel.SetConnectionState(ESvoRayConnectionState.Error, ResUI.SvoRayCoreStopped);
    }

    /// <summary>
    /// Only a core that is gone while the user wants the connection and the screen shows it on;
    /// a reload stops and starts the core on purpose.
    /// </summary>
    public static bool ShouldReportCoreStopped(bool reloading, bool requested, bool coreRunning, ESvoRayConnectionState shown)
    {
        return !reloading && requested && !coreRunning && shown == ESvoRayConnectionState.On;
    }

    /// <summary>
    /// Whether the user asked for a connection, expressed in whatever the current simple mode
    /// switches: the TUN adapter, or the Windows system proxy. In proxy mode the core keeps
    /// running while disconnected - nothing is routed into it, which is what "off" means there.
    /// </summary>
    public static bool IsSvoRayConnectionRequested(Config config)
    {
        return config.SvoRayItem.Mode == ESvoRayMode.Proxy
            ? config.SystemProxyItem.SysProxyType == ESysProxyType.ForcedChange
            : config.TunModeItem.EnableTun;
    }

    /// <summary>
    /// Whether the core is allowed to run at all. Simple mode disconnects by clearing the system
    /// proxy and the tunnel together, and that pair is the disconnected state: the core is stopped
    /// so nothing of ours reaches the server. Advanced mode keeps the v2rayN behaviour, where
    /// "do not change" and PAC are ordinary ways to use the local proxy port by hand.
    /// </summary>
    public static bool IsCoreWanted(Config config)
    {
        return config.TunModeItem.EnableTun
            || config.SystemProxyItem.SysProxyType != ESysProxyType.ForcedClear;
    }

    private void ReloadResult(bool showClashUI)
    {
        RxSchedulers.MainThreadScheduler.Schedule(() =>
        {
            ShowClashUI = showClashUI;
            if (!showClashUI || TabMainSelectedIndex < 0)
            {
                TabMainSelectedIndex = 0;
            }
        });
    }

    private void SetReloadEnabled(bool enabled)
    {
        RxSchedulers.MainThreadScheduler.Schedule(() => BlReloadEnabled = enabled);
    }

    #endregion core job

    #region Presets

    public async Task ApplyRegionalPreset(EPresetType type)
    {
        await ConfigHandler.ApplyRegionalPreset(_config, type);
        await ConfigHandler.InitRouting(_config);
        RxSchedulers.MainThreadScheduler.Schedule(async () =>
        {
            await StatusBarViewModel.RefreshRoutingsMenu();
        });

        await ConfigHandler.SaveConfig(_config);
        await new UpdateService(_config, UpdateTaskHandler).UpdateGeoFileAll();
        await Reload();
    }

    #endregion Presets
}
