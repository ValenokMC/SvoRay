using System.Net;
using System.Reactive.Disposables;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using ServiceLib.Services;
using v2rayN.Base;
using v2rayN.Manager;

namespace v2rayN.Views;

public partial class MainWindow
{
    private const double SimpleWidth = 420;
    private const double SimpleHeight = 780;
    private const double SimpleMinWidth = 360;
    private const double SimpleMinHeight = 560;

    /// <summary>Past this the phone layout stops looking like one; the card stretches, nothing else.</summary>
    private const double SimpleMaxWidth = 560;

    private const double AdvancedWidth = 1180;
    private const double AdvancedHeight = 760;
    private const double AdvancedMinWidth = 860;
    private const double AdvancedMinHeight = 620;

    private static Config _config;
    private readonly SerialDisposable _layoutBindingsDisposable = new();
    private CheckUpdateView? _checkUpdateView;
    private BackupAndRestoreView? _backupAndRestoreView;

    /// <summary>
    /// Set when the user explicitly asked for "add or replace" while profiles already exist,
    /// so the import form stays open until it succeeds or is cancelled.
    /// </summary>
    private bool _importFormPinned;

    private DispatcherTimer? _simpleStatusTimer;

    /// <summary>Profile the running check belongs to; null when no check is in flight.</summary>
    private string? _pingIndexId;

    private DispatcherTimer? _pingTimeoutTimer;

    private ESvoRayConnectionState _lastConnectionState = ESvoRayConnectionState.Off;

    /// <summary>Set while the mode radio buttons are being set from the config rather than by the user.</summary>
    private bool _suppressModeEvents;

    private Size? _simpleSize;
    private Size? _advancedSize;

    private ESvoRayConnectionState _trayState = ESvoRayConnectionState.Off;

    public MainWindow()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        ThreadPool.RegisterWaitForSingleObject(App.ProgramStarted, OnProgramStarted, null, -1, false);

        App.Current.SessionEnding += Current_SessionEnding;
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        menuSettingsSetUWP.Click += MenuSettingsSetUWP_Click;
        menuPromotion.Click += MenuPromotion_Click;
        menuClose.Click += MenuClose_Click;
        menuCheckUpdate.Click += MenuCheckUpdate_Click;
        btnNewUpdate.Click += MenuCheckUpdate_Click;
        menuBackupAndRestore.Click += MenuBackupAndRestore_Click;
        btnAdvancedMode.Click += (_, _) => ShowAdvancedMode();
        btnSimpleMode.Click += (_, _) => ShowSimpleMode();
        btnImportSubscriptionSimple.Click += BtnImportSubscriptionSimple_Click;
        btnUpdateSimple.Click += BtnUpdateSimple_Click;
        btnCheckPing.Click += BtnCheckPing_Click;
        cmbSimpleServers.SelectionChanged += (_, _) => ResetPingResult();
        btnShowImport.Click += BtnShowImport_Click;
        btnCancelImport.Click += BtnCancelImport_Click;
        btnSimpleRouting.Click += BtnSimpleRouting_Click;
        togSvoRayConnect.Click += TogSvoRayConnect_Click;
        rdoModeProxy.Checked += async (_, _) => await ApplyModeAsync(ESvoRayMode.Proxy);
        rdoModeTun.Checked += async (_, _) => await ApplyModeAsync(ESvoRayMode.Tun);
        menuTrayToggle.Click += MenuTrayToggle_Click;
        menuShowWindow.Click += (_, _) => ShowHideWindow(true);
        menuTrayExit.Click += MenuTrayExit_Click;
        SourceInitialized += (_, _) => SvoRayService.ApplyGlassBackdrop(this);

        pbTheme.Content ??= new ThemeSettingView();

        this.WhenActivated(disposables =>
        {
            //servers
            this.BindCommand(ViewModel, vm => vm.AddVmessServerCmd, v => v.menuAddVmessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddVlessServerCmd, v => v.menuAddVlessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddShadowsocksServerCmd, v => v.menuAddShadowsocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddSocksServerCmd, v => v.menuAddSocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHttpServerCmd, v => v.menuAddHttpServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTrojanServerCmd, v => v.menuAddTrojanServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHysteria2ServerCmd, v => v.menuAddHysteria2Server).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTuicServerCmd, v => v.menuAddTuicServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddWireguardServerCmd, v => v.menuAddWireguardServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddAnytlsServerCmd, v => v.menuAddAnytlsServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddNaiveServerCmd, v => v.menuAddNaiveServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomServerCmd, v => v.menuAddCustomServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddPolicyGroupServerCmd, v => v.menuAddPolicyGroupServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddProxyChainServerCmd, v => v.menuAddProxyChainServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaClipboardCmd, v => v.menuAddServerViaClipboard).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaScanCmd, v => v.menuAddServerViaScan).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaImageCmd, v => v.menuAddServerViaImage).DisposeWith(disposables);

            //sub
            this.BindCommand(ViewModel, vm => vm.SubSettingCmd, v => v.menuSubSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateCmd, v => v.menuSubUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateViaProxyCmd, v => v.menuSubUpdateViaProxy).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateCmd, v => v.menuSubGroupUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateViaProxyCmd, v => v.menuSubGroupUpdateViaProxy).DisposeWith(disposables);

            //setting
            this.BindCommand(ViewModel, vm => vm.OptionSettingCmd, v => v.menuOptionSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingSettingCmd, v => v.menuRoutingSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.DNSSettingCmd, v => v.menuDNSSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.FullConfigTemplateCmd, v => v.menuFullConfigTemplate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GlobalHotkeySettingCmd, v => v.menuGlobalHotkeySetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RebootAsAdminCmd, v => v.menuRebootAsAdmin).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ClearServerStatisticsCmd, v => v.menuClearServerStatistics).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OpenTheFileLocationCmd, v => v.menuOpenTheFileLocation).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetDefaultCmd, v => v.menuRegionalPresetsDefault).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetRussiaCmd, v => v.menuRegionalPresetsRussia).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetIranCmd, v => v.menuRegionalPresetsIran).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.ReloadCmd, v => v.menuReload).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlReloadEnabled, v => v.menuReload.IsEnabled).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.BlNewUpdate, v => v.btnNewUpdate.Visibility).DisposeWith(disposables);

            _layoutBindingsDisposable.DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.MainGirdOrientation)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(UpdateLayout)
                .DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.StatusBarViewModel)
                .Subscribe(vm => ViewHost.Show(contentStatusBarView, vm))
                .DisposeWith(disposables);

            // The simple selector uses the uncapped list so large subscriptions stay usable.
            this.OneWayBind(ViewModel, vm => vm.StatusBarViewModel.SimpleServers, v => v.cmbSimpleServers.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.StatusBarViewModel.SelectedServer, v => v.cmbSimpleServers.SelectedItem).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.StatusBarViewModel.HasSubscriptions, v => v.btnUpdateSimple.Visibility).DisposeWith(disposables);
            this.WhenAnyValue(v => v.ViewModel.StatusBarViewModel.ConnectionState)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(UpdateSimpleConnectionState)
                .DisposeWith(disposables);
            this.WhenAnyValue(v => v.ViewModel.StatusBarViewModel.HasProfiles)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(UpdateSimpleImportState)
                .DisposeWith(disposables);

            ViewModel.StatusBarViewModel.ConnectToggleRequested
                .AsObservable()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(async enable => await ToggleConnectionAsync(enable))
                .DisposeWith(disposables);

            //tray
            this.OneWayBind(ViewModel, vm => vm.StatusBarViewModel.SimpleServers, v => v.cmbTrayServers.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.StatusBarViewModel.SelectedServer, v => v.cmbTrayServers.SelectedItem).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.StatusBarViewModel.HasProfiles, v => v.menuTrayProfiles.Visibility).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.StatusBarViewModel.HasSubscriptions, v => v.menuTraySubUpdate.Visibility).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.StatusBarViewModel.SubUpdateCmd, v => v.menuTraySubUpdate).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.StatusBarViewModel.NotifyLeftClickCmd, v => v.tbNotify.LeftClickCommand).DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.StatusBarViewModel.ConnectionState)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(ApplyTrayState)
                .DisposeWith(disposables);

            // Registered here, not in StatusBarView: that view only exists in advanced mode,
            // so in simple mode this interaction previously had no handler at all.
            ViewModel.StatusBarViewModel.DispatcherRefreshIconInteraction.RegisterHandler(interaction =>
            {
                Application.Current?.Dispatcher.Invoke(() => ApplyTrayState(_trayState), DispatcherPriority.Normal);
                interaction.SetOutput(Unit.Default);
            }).DisposeWith(disposables);

            // Fallback for the notice stream, same reason as the tray interaction above.
            // MsgView registers the real handler, but it is the content of a tab inside the
            // collapsed AdvancedPanel: WPF does not create the content of an unselected tab,
            // so in simple mode nothing was registered and every batch of messages threw
            // UnhandledInteractionException. ReactiveUI invokes handlers in reverse
            // registration order, so once MsgView appears its handler runs first and this one
            // never sees the message. Simple mode drops the text on purpose - the stream
            // carries v2rayN diagnostics including the running server address.
            ViewModel.MsgViewModel.DispatcherShowMsgInteraction.RegisterHandler(interaction =>
            {
                interaction.SetOutput(Unit.Default);
            }).DisposeWith(disposables);

            ViewModel.ReadTextFromClipboardInteraction.RegisterHandler(interaction =>
            {
                var clipboardData = WindowsUtils.GetClipboardData();
                interaction.SetOutput(clipboardData);
            }).DisposeWith(disposables);

            ViewModel.ScanScreenInteraction.RegisterHandler(interaction =>
            {
                ShowHideWindow(false);
                if (Application.Current?.MainWindow is { } window)
                {
                    var bytes = QRCodeWindowsUtils.CaptureScreen(window);
                    interaction.SetOutput(bytes);
                }
                ShowHideWindow(true);
            }).DisposeWith(disposables);

            ViewModel.BrowseImageFileInteraction.RegisterHandler(interaction =>
            {
                if (UI.OpenFileDialog(out var fileName, "PNG|*.png|All|*.*") != true)
                {
                    interaction.SetOutput(null);
                    return;
                }
                interaction.SetOutput(fileName);
            }).DisposeWith(disposables);

            ViewModel.ShowHideWindowInteraction.RegisterHandler(interaction =>
            {
                ShowHideWindow(interaction.Input);
                interaction.SetOutput(Unit.Default);
            }).DisposeWith(disposables);

            AppEvents.SendSnackMsgRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(async content => await DelegateSnackMsg(content))
              .DisposeWith(disposables);

            AppEvents.AppExitRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(_ => StorageUI())
              .DisposeWith(disposables);

            AppEvents.ShutdownRequested
             .AsObservable()
             .ObserveOn(RxSchedulers.MainThreadScheduler)
             .Subscribe(Shutdown)
             .DisposeWith(disposables);
        });

        var appVersion = typeof(MainWindow).Assembly.GetName().Version;
        var displayVersion = appVersion is null ? "dev" : $"{appVersion.Major}.{appVersion.Minor}.{appVersion.Build}";
        Title = $"SvoRay · {displayVersion} · {(Utils.IsAdministrator() ? "Администратор" : "Обычный режим")}";
        if (_config.UiItem.AutoHideStartup)
        {
            WindowState = WindowState.Minimized;
        }

        if (!_config.GuiItem.EnableHWA)
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        }

        AddHelpMenuItem();
        WindowsManager.Instance.RegisterGlobalHotkey(_config, OnHotkeyHandler, null);

        InitSimpleMode();
    }

    /// <summary>
    /// Restores the stored simple-mode choices into the screen. The radio buttons raise Checked
    /// while this runs, so the handler is suppressed - otherwise restoring the saved mode would
    /// be read as the user changing it and would reconnect on startup.
    /// </summary>
    private void InitSimpleMode()
    {
        _suppressModeEvents = true;
        rdoModeProxy.IsChecked = _config.SvoRayItem.Mode == ESvoRayMode.Proxy;
        rdoModeTun.IsChecked = _config.SvoRayItem.Mode == ESvoRayMode.Tun;
        _suppressModeEvents = false;

        UpdateRoutingSummary();
    }

    #region Event

    private void ShowAdvancedMode()
    {
        if (SimplePanel.Visibility == Visibility.Visible && WindowState == WindowState.Normal)
        {
            _simpleSize = new Size(Width, Height);
        }

        SimplePanel.Visibility = Visibility.Collapsed;
        AdvancedPanel.Visibility = Visibility.Visible;
        ApplyWindowSize(false);
    }

    private void ShowSimpleMode()
    {
        if (AdvancedPanel.Visibility == Visibility.Visible && WindowState == WindowState.Normal)
        {
            _advancedSize = new Size(Width, Height);
        }

        AdvancedPanel.Visibility = Visibility.Collapsed;
        SimplePanel.Visibility = Visibility.Visible;
        ApplyWindowSize(true);
    }

    /// <summary>
    /// Simple mode is a phone-shaped window, advanced mode a desktop one, and they cannot share
    /// a size or a minimum. Each keeps what the user last resized it to for the session, so
    /// switching back and forth does not undo their adjustment.
    /// </summary>
    private void ApplyWindowSize(bool simple)
    {
        var work = SystemParameters.WorkArea;

        // MaxWidth is assigned before Width in both branches: while the phone limit is still in
        // force, a wider assignment would be clamped down to it and silently ignored.
        if (simple)
        {
            MinWidth = SimpleMinWidth;
            MinHeight = SimpleMinHeight;
            MaxWidth = SimpleMaxWidth;

            var size = _simpleSize ?? new Size(SimpleWidth, SimpleHeight);
            Width = Math.Min(size.Width, work.Width);
            Height = Math.Min(size.Height, work.Height);
        }
        else
        {
            MaxWidth = double.PositiveInfinity;
            MinWidth = AdvancedMinWidth;
            MinHeight = AdvancedMinHeight;

            var size = _advancedSize ?? new Size(AdvancedWidth, AdvancedHeight);
            Width = Math.Min(Math.Max(size.Width, AdvancedMinWidth), work.Width);
            Height = Math.Min(Math.Max(size.Height, AdvancedMinHeight), work.Height);
        }

        // The window keeps its top-left corner through a resize, so a switch between the two
        // shapes would push it towards the edge of the screen and eventually off it.
        if (WindowState == WindowState.Normal)
        {
            Left = work.Left + ((work.Width - Width) / 2);
            Top = work.Top + ((work.Height - Height) / 2);
        }
    }

    private void UpdateSimpleImportState(bool hasProfiles)
    {
        var showImport = !hasProfiles || _importFormPinned;

        ImportStatePanel.Visibility = showImport ? Visibility.Visible : Visibility.Collapsed;
        ProfilesStatePanel.Visibility = showImport ? Visibility.Collapsed : Visibility.Visible;
        btnCancelImport.Visibility = hasProfiles ? Visibility.Visible : Visibility.Collapsed;

        if (!showImport)
        {
            ClearSubscriptionInput();
        }
    }

    /// <summary>
    /// Drops the pasted link from the UI as far as WPF practically allows, including the
    /// undo buffer, so a private subscription URL cannot be recovered from the form.
    /// </summary>
    private void ClearSubscriptionInput()
    {
        txtSubscriptionUrl.Clear();
        txtSubscriptionUrl.IsUndoEnabled = false;
        txtSubscriptionUrl.IsUndoEnabled = true;
        txtImportStatus.Text = string.Empty;
        txtImportStatus.Visibility = Visibility.Collapsed;
    }

    private void ShowImportError(string message)
    {
        txtImportStatus.Text = message;
        txtImportStatus.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Shows a short-lived status under the profile block. Simple mode deliberately does not
    /// use the shared snackbar, whose notices include the running server address.
    /// </summary>
    private void ShowSimpleStatus(string message, bool isError)
    {
        txtSimpleStatus.Text = message;
        txtSimpleStatus.Foreground = new SolidColorBrush(isError
            ? Color.FromRgb(0xF0, 0xA9, 0xA9)
            : Color.FromRgb(0x9F, 0xD9, 0xCE));
        txtSimpleStatus.Visibility = Visibility.Visible;

        _simpleStatusTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _simpleStatusTimer.Tick -= HideSimpleStatus;
        _simpleStatusTimer.Tick += HideSimpleStatus;
        _simpleStatusTimer.Stop();
        _simpleStatusTimer.Start();
    }

    private void HideSimpleStatus(object? sender, EventArgs e)
    {
        _simpleStatusTimer?.Stop();
        txtSimpleStatus.Visibility = Visibility.Collapsed;
    }

    private void BtnShowImport_Click(object sender, RoutedEventArgs e)
    {
        _importFormPinned = true;
        UpdateSimpleImportState(ViewModel?.StatusBarViewModel.HasProfiles == true);
        txtSubscriptionUrl.Focus();
    }

    private void BtnCancelImport_Click(object sender, RoutedEventArgs e)
    {
        _importFormPinned = false;
        UpdateSimpleImportState(ViewModel?.StatusBarViewModel.HasProfiles == true);
    }

    private async void BtnImportSubscriptionSimple_Click(object sender, RoutedEventArgs e)
    {
        var value = txtSubscriptionUrl.Text.TrimEx();
        if (value.IsNullOrEmpty())
        {
            ShowImportError("Сначала вставьте ссылку подписки или профиль.");
            return;
        }

        btnImportSubscriptionSimple.IsEnabled = false;
        txtImportButton.Text = "Импортируем…";
        txtImportStatus.Visibility = Visibility.Collapsed;
        try
        {
            var success = await ViewModel.ImportSubscriptionAndRefreshAsync(value);
            if (success)
            {
                _importFormPinned = false;
                UpdateSimpleImportState(true);
            }
            else
            {
                ShowImportError("Не удалось импортировать ссылку. Проверьте её и повторите.");
            }
        }
        catch (Exception ex)
        {
            // Log the failure kind only: the exception message can echo the private URL.
            Logging.SaveLog($"SvoRay import failed: {ex.GetType().Name}");
            ShowImportError("Ошибка импорта. Подробности доступны в расширенном режиме.");
        }
        finally
        {
            txtImportButton.Text = "Импортировать";
            btnImportSubscriptionSimple.IsEnabled = true;
        }
    }

    private async void BtnUpdateSimple_Click(object sender, RoutedEventArgs e)
    {
        btnUpdateSimple.IsEnabled = false;
        txtUpdateSubscription.Text = "Обновляем…";
        try
        {
            // A failed download does not throw; the handler reports it through the result.
            var updated = await ViewModel.UpdateSubscriptionProcess(string.Empty, false);
            await ViewModel.RefreshSimpleUiAsync();

            if (updated)
            {
                txtUpdateSubscription.Text = "Обновлено";
                ShowSimpleStatus("Список серверов обновлён.", false);
            }
            else
            {
                ShowSimpleStatus("Не удалось получить подписку. Проверьте соединение и ссылку, затем повторите.", true);
            }
            await Task.Delay(2000);
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"SvoRay subscription update failed: {ex.GetType().Name}");
            ShowSimpleStatus("Не удалось обновить подписку. Проверьте соединение и повторите.", true);
        }
        finally
        {
            txtUpdateSubscription.Text = "Обновить";
            btnUpdateSimple.IsEnabled = true;
        }
    }

    private async void TogSvoRayConnect_Click(object sender, RoutedEventArgs e)
    {
        await ToggleConnectionAsync(togSvoRayConnect.IsChecked == true);
    }

    /// <summary>
    /// Shared by the power button and the tray menu so both take the same path. What is actually
    /// switched depends on the mode: the TUN adapter, or the Windows system proxy.
    /// </summary>
    private async Task ToggleConnectionAsync(bool enable)
    {
        var statusBar = ViewModel.StatusBarViewModel;
        try
        {
            if (!enable)
            {
                // Both modes take the same path: the switches go down and the core is stopped.
                await statusBar.Disconnect();
                return;
            }

            var profile = await ConfigHandler.GetDefaultServer(_config);
            if (profile is null)
            {
                togSvoRayConnect.IsChecked = false;
                statusBar.SetConnectionState(
                    ESvoRayConnectionState.Error, "Сначала добавьте подключение и выберите профиль.");
                return;
            }

            statusBar.SetConnectionState(ESvoRayConnectionState.Connecting);
            await SvoRayService.PrepareAsync(_config);

            // Simple mode has just made its own profile the active one; advanced mode shows
            // which routing profile is in use and would otherwise still name the previous one.
            await statusBar.RefreshRoutingsMenu();

            if (_config.SvoRayItem.Mode == ESvoRayMode.Tun)
            {
                // The tunnel switch owns the rest of this path: it asks for administrator rights
                // when they are missing, and its own reload is what starts the core.
                await statusBar.ClearSystemProxy();
                statusBar.EnableTun = true;
                return;
            }

            // The core is stopped while disconnected, so proxy mode always starts it here. Both
            // switches are written first and awaited: the reload reads them to decide whether the
            // core may run, and would take it straight back down if it still saw a cleared proxy.
            await statusBar.ConnectViaSystemProxy();
            await ViewModel.Reload();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SvoRay connect", ex);
            togSvoRayConnect.IsChecked = MainWindowViewModel.IsSvoRayConnectionRequested(_config);
            statusBar.SetConnectionState(
                ESvoRayConnectionState.Error, "Не удалось переключить VPN. Откройте расширенные настройки.");
        }
    }

    /// <summary>
    /// Switches between proxy and TUN. An active connection is taken down through the mode it was
    /// established with before the new one is brought up: the two use different switches, and
    /// dropping the old one afterwards would leave the system proxy or the tunnel behind.
    /// </summary>
    private async Task ApplyModeAsync(ESvoRayMode mode)
    {
        if (_suppressModeEvents || _config.SvoRayItem.Mode == mode)
        {
            return;
        }

        var wasConnected = _lastConnectionState is ESvoRayConnectionState.On or ESvoRayConnectionState.Connecting;
        try
        {
            if (wasConnected)
            {
                await ToggleConnectionAsync(false);
            }

            _config.SvoRayItem.Mode = mode;
            await ConfigHandler.SaveConfig(_config);

            if (wasConnected)
            {
                await ToggleConnectionAsync(true);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SvoRay mode", ex);
            ShowSimpleStatus("Не удалось сменить режим. Откройте расширенные настройки.", true);
        }
    }

    private async void BtnSimpleRouting_Click(object sender, RoutedEventArgs e)
    {
        var window = new SimpleRoutingWindow(_config.SvoRayItem.RoutingMode, _config.SvoRayItem.RuleDomains)
        {
            Owner = this
        };
        if (window.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _config.SvoRayItem.RoutingMode = window.ResultMode;
            _config.SvoRayItem.RuleDomains = window.ResultDomains;
            await SvoRayRoutingHandler.ApplyAsync(_config);
            await ConfigHandler.SaveConfig(_config);
            await ViewModel.StatusBarViewModel.RefreshRoutingsMenu();
            UpdateRoutingSummary();

            // The core reads the routing profile at startup only, so a live connection has to be
            // rebuilt for the new list to mean anything.
            if (_lastConnectionState is not ESvoRayConnectionState.Off)
            {
                ShowSimpleStatus("Применяем маршрутизацию…", false);
                await ViewModel.Reload();
            }
            else
            {
                ShowSimpleStatus("Маршрутизация сохранена.", false);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SvoRay routing", ex);
            ShowSimpleStatus("Не удалось сохранить маршрутизацию. Откройте расширенные настройки.", true);
        }
    }

    private void UpdateRoutingSummary()
    {
        var count = _config.SvoRayItem.RuleDomains.Count;
        txtRoutingSummary.Text = _config.SvoRayItem.RoutingMode switch
        {
            ESvoRayRoutingMode.ProxyListed when count > 0 => $"Через VPN: {count} {DomainsWord(count)}",
            ESvoRayRoutingMode.ProxyListed => "VPN не используется",
            _ when count > 0 => $"Без VPN: {count} {DomainsWord(count)}",
            _ => "Весь трафик через VPN"
        };
    }

    private static string DomainsWord(int count)
    {
        var tens = count % 100;
        var ones = count % 10;
        if (tens is >= 11 and <= 14)
        {
            return "доменов";
        }
        return ones switch
        {
            1 => "домен",
            2 or 3 or 4 => "домена",
            _ => "доменов"
        };
    }

    /// <summary>
    /// Keeps the tray icon, its tooltip and its menu in step with the connection state.
    /// </summary>
    private async void ApplyTrayState(ESvoRayConnectionState state)
    {
        _trayState = state;

        menuTrayState.Header = WindowsManager.GetNotifyToolTip(state);
        menuTrayToggle.Header = state is ESvoRayConnectionState.On or ESvoRayConnectionState.Connecting
            ? "Выключить"
            : "Включить";
        menuTrayToggle.IsEnabled = state != ESvoRayConnectionState.Connecting;

        tbNotify.ToolTipText = WindowsManager.GetNotifyToolTip(state);
        tbNotify.Icon = await WindowsManager.Instance.GetNotifyIcon(state);
        Icon = WindowsManager.Instance.GetAppIcon(_config);
    }

    private void MenuTrayToggle_Click(object sender, RoutedEventArgs e)
    {
        var enable = _trayState is not (ESvoRayConnectionState.On or ESvoRayConnectionState.Connecting);
        ViewModel?.StatusBarViewModel.ConnectToggleRequested.Publish(enable);
    }

    private async void MenuTrayExit_Click(object sender, RoutedEventArgs e)
    {
        tbNotify.Dispose();
        await AppManager.Instance.AppExitAsync(true);
    }

    /// <summary>
    /// Measures the delay of the selected profile through the proxy itself: a temporary core
    /// is started and a request goes out through it. A plain TCP probe was rejected on
    /// purpose - a server can complete a TCP handshake while the proxy on it does not work,
    /// and reporting that as success would mislead.
    /// </summary>
    private async void BtnCheckPing_Click(object sender, RoutedEventArgs e)
    {
        if (_lastConnectionState == ESvoRayConnectionState.On)
        {
            await CheckLiveConnectionAsync();
            return;
        }

        var indexId = ViewModel?.StatusBarViewModel.SelectedServer?.ID;
        if (indexId.IsNullOrEmpty())
        {
            ShowSimpleStatus("Сначала выберите профиль.", true);
            return;
        }

        var profile = await AppManager.Instance.GetProfileItem(indexId);
        if (profile is null)
        {
            ShowSimpleStatus("Профиль не найден. Обновите подписку.", true);
            return;
        }

        BeginPingCheck(indexId);

        // RunLoop is fire-and-forget and reports through the callback on a worker thread.
        // Results arrive as {IndexId, Delay}; a final {"", <message>} marks the end of the run.
        var service = new SpeedtestService(_config, result =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() => OnPingResult(result));
            return Task.CompletedTask;
        });
        service.RunLoop(ESpeedActionType.Realping, [profile]);
    }

    /// <summary>
    /// Answers the question the user would otherwise open an IP-check site for: is traffic really
    /// going through the tunnel right now. The request is made through the local proxy port of the
    /// running core, so it tests the live connection rather than starting a second one - and it
    /// holds in proxy mode too, where a browser that ignores the system proxy would say nothing.
    /// </summary>
    private async Task CheckLiveConnectionAsync()
    {
        _pingTimeoutTimer?.Stop();
        _pingIndexId = null;
        btnCheckPing.IsEnabled = false;
        icoCheckPing.Kind = PackIconKind.TimerSandEmpty;
        icoCheckPing.Foreground = Brushes.White;
        txtCheckPing.Text = "Проверяем…";
        txtCheckPing.Foreground = Brushes.White;

        try
        {
            var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            var proxy = new WebProxy($"socks5://{Global.Loopback}:{port}");

            var delay = await ConnectionHandler.GetRealPingTime(proxy);
            if (delay <= 0)
            {
                ShowPingFailure("Нет ответа");
                ShowSimpleStatus("Трафик через VPN не проходит. Попробуйте другой профиль или переподключитесь.", true);
                return;
            }

            // Only the country is shown. The exit address is the server address, which simple
            // mode keeps off screen on purpose; advanced mode reports it in full.
            var info = await ConnectionHandler.GetIPInfo(proxy);
            var country = info?.Country;

            icoCheckPing.Kind = PackIconKind.ShieldCheck;
            txtCheckPing.Text = $"{delay} мс";
            var green = new SolidColorBrush(Color.FromRgb(0x5F, 0xE3, 0xB4));
            txtCheckPing.Foreground = green;
            icoCheckPing.Foreground = green;
            ShowSimpleStatus(country.IsNullOrEmpty() || country == "unknown"
                ? $"VPN работает, трафик идёт через сервер. Задержка {delay} мс."
                : $"VPN работает, выход в интернет: {country}. Задержка {delay} мс.", false);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SvoRay live check", ex);
            ShowPingFailure("Ошибка");
        }
        finally
        {
            btnCheckPing.IsEnabled = true;
        }
    }

    private void BeginPingCheck(string indexId)
    {
        _pingIndexId = indexId;
        btnCheckPing.IsEnabled = false;
        icoCheckPing.Kind = PackIconKind.TimerSandEmpty;
        txtCheckPing.Text = "Проверяем…";
        txtCheckPing.Foreground = Brushes.White;

        // The run can end without ever reporting a delay - the core may fail to start at all.
        // Without this the button would stay disabled for the rest of the session.
        _pingTimeoutTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _pingTimeoutTimer.Tick -= OnPingTimeout;
        _pingTimeoutTimer.Tick += OnPingTimeout;
        _pingTimeoutTimer.Stop();
        _pingTimeoutTimer.Start();
    }

    private void OnPingResult(SpeedTestResult result)
    {
        // Delay for our profile is the only interesting message. The service also reports IP
        // info and a completion notice, and both arrive with no delay value.
        if (_pingIndexId is null || result.IndexId != _pingIndexId || result.Delay.IsNullOrEmpty())
        {
            return;
        }

        // The service reuses Delay for progress as well as for the answer: it sends the
        // "Testing..." placeholder before the run starts and the number once the measurement
        // is done. Only a number is an answer. Treating the placeholder as one made the button
        // report a failure instantly, before anything had been measured, and then discard the
        // real result because the request was already considered finished.
        if (!int.TryParse(result.Delay, out var delay))
        {
            return;
        }

        _pingTimeoutTimer?.Stop();
        _pingIndexId = null;
        btnCheckPing.IsEnabled = true;

        if (delay <= 0)
        {
            // The underlying message can carry the server address, so it stays out of simple mode.
            ShowPingFailure("Нет ответа");
            return;
        }

        icoCheckPing.Kind = PackIconKind.SpeedometerMedium;
        txtCheckPing.Text = $"{delay} мс";
        var color = delay switch
        {
            < 150 => Color.FromRgb(0x5F, 0xE3, 0xB4),
            < 400 => Color.FromRgb(0xE8, 0xC8, 0x6A),
            _ => Color.FromRgb(0xF0, 0xA9, 0xA9)
        };
        txtCheckPing.Foreground = new SolidColorBrush(color);
        icoCheckPing.Foreground = new SolidColorBrush(color);
    }

    private void OnPingTimeout(object? sender, EventArgs e)
    {
        _pingTimeoutTimer?.Stop();
        _pingIndexId = null;
        btnCheckPing.IsEnabled = true;
        ShowPingFailure("Нет ответа");
    }

    private void ShowPingFailure(string text)
    {
        var red = new SolidColorBrush(Color.FromRgb(0xF0, 0xA9, 0xA9));
        icoCheckPing.Kind = PackIconKind.CloseCircleOutline;
        icoCheckPing.Foreground = red;
        txtCheckPing.Text = text;
        txtCheckPing.Foreground = red;
        ShowSimpleStatus("Профиль не ответил. Попробуйте другой или обновите подписку.", true);
    }

    /// <summary>
    /// A delay belongs to one profile only. Keeping it on screen after the user picks another
    /// one would show a number that was never measured for what is now selected.
    /// </summary>
    private void ResetPingResult()
    {
        _pingTimeoutTimer?.Stop();
        _pingIndexId = null;
        btnCheckPing.IsEnabled = _lastConnectionState
            is ESvoRayConnectionState.Off or ESvoRayConnectionState.On;
        icoCheckPing.Kind = PackIconKind.SpeedometerMedium;
        icoCheckPing.Foreground = Brushes.White;
        txtCheckPing.Text = "Проверить";
        txtCheckPing.Foreground = Brushes.White;
    }

    private void UpdateSimpleConnectionState(ESvoRayConnectionState state)
    {
        _lastConnectionState = state;
        togSvoRayConnect.IsChecked = state is ESvoRayConnectionState.Connecting or ESvoRayConnectionState.On;
        togSvoRayConnect.IsEnabled = state != ESvoRayConnectionState.Connecting;

        // Switching modes mid-handshake would take down a connection that is not up yet.
        rdoModeProxy.IsEnabled = rdoModeTun.IsEnabled = state != ESvoRayConnectionState.Connecting;

        // The button asks two different questions. Disconnected it measures the selected profile,
        // which starts a second core - not something to do while TUN holds the default route.
        // Connected it probes the tunnel that is already up, which is the more useful of the two:
        // it says whether traffic really leaves through the server right now.
        btnCheckPing.IsEnabled = state is ESvoRayConnectionState.Off or ESvoRayConnectionState.On
            && _pingIndexId is null;
        btnCheckPing.ToolTip = state switch
        {
            ESvoRayConnectionState.On => "Проверить, что трафик действительно идёт через VPN",
            ESvoRayConnectionState.Off => "Измерить задержку выбранного профиля через сам прокси",
            _ => "Доступно, когда VPN включён или выключен"
        };

        switch (state)
        {
            case ESvoRayConnectionState.Connecting:
                txtConnectionTitle.Text = "Подключение…";
                txtConnectionSubtitle.Text = string.Empty;
                break;

            case ESvoRayConnectionState.On:
                txtConnectionTitle.Text = "VPN включён";
                txtConnectionSubtitle.Text = _config.SvoRayItem.Mode == ESvoRayMode.Proxy
                    ? "Через прокси идут программы, которые учитывают системные настройки"
                    : "Через VPN идёт весь трафик системы";
                break;

            case ESvoRayConnectionState.Error:
                txtConnectionTitle.Text = "Ошибка подключения";
                txtConnectionSubtitle.Text = ViewModel?.StatusBarViewModel.ConnectionErrorText
                    ?? "Не удалось запустить подключение. Подробности — в расширенном режиме.";
                break;

            default:
                txtConnectionTitle.Text = "VPN выключен";
                txtConnectionSubtitle.Text = "Выберите профиль и подключитесь";
                break;
        }
    }

    private void OnProgramStarted(object state, bool timeout)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            ShowHideWindow(true);
        });
    }

    private async Task DelegateSnackMsg(string content)
    {
        MainSnackbar.MessageQueue?.Enqueue(content);
        await Task.CompletedTask;
    }

    private void OnHotkeyHandler(EGlobalHotkey e)
    {
        switch (e)
        {
            case EGlobalHotkey.ShowForm:
                ShowHideWindow(null);
                break;

            case EGlobalHotkey.SystemProxyClear:
            case EGlobalHotkey.SystemProxySet:
            case EGlobalHotkey.SystemProxyUnchanged:
            case EGlobalHotkey.SystemProxyPac:
                AppEvents.SysProxyChangeRequested.Publish((ESysProxyType)((int)e - 1));
                break;
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        ShowHideWindow(false);
    }

    private async void Current_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        Logging.SaveLog("Current_SessionEnding");
        StorageUI();
        await AppManager.Instance.AppExitAsync(false);
    }

    private void Shutdown(bool obj)
    {
        Application.Current.Shutdown();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            switch (e.Key)
            {
                case Key.V:
                    if (Keyboard.FocusedElement is TextBox)
                    {
                        return;
                    }
                    AddServerViaClipboardAsync().ContinueWith(_ => { });

                    break;

                case Key.S:
                    ScanScreenTaskAsync().ContinueWith(_ => { });
                    break;
            }
        }
        else
        {
            if (e.Key == Key.F5)
            {
                ViewModel?.Reload();
            }
        }
    }

    private void MenuClose_Click(object sender, RoutedEventArgs e)
    {
        StorageUI();
        ShowHideWindow(false);
    }

    private void MenuPromotion_Click(object sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart($"{Utils.Base64Decode(Global.PromotionUrl)}?t={DateTime.Now.Ticks}");
    }

    private void MenuSettingsSetUWP_Click(object sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart(Utils.GetBinPath("EnableLoopback.exe"));
    }

    public async Task AddServerViaClipboardAsync()
    {
        var clipboardData = WindowsUtils.GetClipboardData();
        if (clipboardData.IsNotEmpty() && ViewModel != null)
        {
            await ViewModel.AddServerViaClipboardAsync(clipboardData);
        }
    }

    private async Task ScanScreenTaskAsync()
    {
        ShowHideWindow(false);

        if (Application.Current?.MainWindow is Window window)
        {
            var bytes = QRCodeWindowsUtils.CaptureScreen(window);
            await ViewModel?.ScanScreenResult(bytes);
        }

        ShowHideWindow(true);
    }

    private void MenuCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        _checkUpdateView ??= new CheckUpdateView();
        _checkUpdateView.ViewModel = ViewModel?.CheckUpdateViewModel;
        DialogHost.Show(_checkUpdateView, "RootDialog");

        AppEvents.HasUpdateNotified.Publish(false);
    }

    private void MenuBackupAndRestore_Click(object sender, RoutedEventArgs e)
    {
        _backupAndRestoreView ??= new BackupAndRestoreView();
        _backupAndRestoreView.ViewModel = ViewModel?.BackupAndRestoreViewModel;
        DialogHost.Show(_backupAndRestoreView, "RootDialog");
    }

    #endregion Event

    #region UI

    public void ShowHideWindow(bool? blShow)
    {
        var bl = blShow ?? !AppManager.Instance.ShowInTaskbar;
        if (bl)
        {
            this?.Show();
            if (this?.WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            this?.Activate();
            this?.Focus();
        }
        else
        {
            this?.Hide();
        }
        AppManager.Instance.ShowInTaskbar = bl;
    }

    protected override void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // The base restores one remembered size for the window as a whole, which is the wrong
        // shape for whichever mode it did not come from. Keep it only when it still fits the
        // mode that is about to be shown, then let that mode decide.
        base.OnLoaded(sender, e);
        var restored = new Size(Width, Height);
        if (SimplePanel.Visibility == Visibility.Visible)
        {
            if (restored.Width <= SimpleMaxWidth)
            {
                _simpleSize = restored;
            }
        }
        else
        {
            _advancedSize = restored;
        }
        ApplyWindowSize(SimplePanel.Visibility == Visibility.Visible);

        if (_config.UiItem.AutoHideStartup)
        {
            ShowHideWindow(false);
        }
        RestoreUI();
    }

    private void RestoreUI()
    {
        if (_config.UiItem.MainGirdHeight1 > 0 && _config.UiItem.MainGirdHeight2 > 0)
        {
            if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal)
            {
                gridMain.ColumnDefinitions[0].Width = new GridLength(_config.UiItem.MainGirdHeight1, GridUnitType.Star);
                gridMain.ColumnDefinitions[2].Width = new GridLength(_config.UiItem.MainGirdHeight2, GridUnitType.Star);
            }
            else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical)
            {
                gridMain1.RowDefinitions[0].Height = new GridLength(_config.UiItem.MainGirdHeight1, GridUnitType.Star);
                gridMain1.RowDefinitions[2].Height = new GridLength(_config.UiItem.MainGirdHeight2, GridUnitType.Star);
            }
        }
    }

    private void StorageUI()
    {
        ConfigHandler.SaveWindowSizeItem(_config, GetType().Name, Width, Height);

        if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal)
        {
            ConfigHandler.SaveMainGirdHeight(_config, gridMain.ColumnDefinitions[0].ActualWidth, gridMain.ColumnDefinitions[2].ActualWidth);
        }
        else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical)
        {
            ConfigHandler.SaveMainGirdHeight(_config, gridMain1.RowDefinitions[0].ActualHeight, gridMain1.RowDefinitions[2].ActualHeight);
        }
    }

    private void UpdateLayout(EGirdOrientation orientation)
    {
        var currentLayoutDisposables = new CompositeDisposable();
        _layoutBindingsDisposable.Disposable = currentLayoutDisposables;

        gridMain.Visibility = orientation == EGirdOrientation.Horizontal ? Visibility.Visible : Visibility.Collapsed;
        gridMain1.Visibility = orientation == EGirdOrientation.Vertical ? Visibility.Visible : Visibility.Collapsed;
        gridMain2.Visibility = orientation == EGirdOrientation.Tab ? Visibility.Visible : Visibility.Collapsed;

        switch (orientation)
        {
            case EGirdOrientation.Horizontal:
                this.WhenAnyValue(v => v.ViewModel.ProfilesViewModel)
                    .Subscribe(vm => ViewHost.Show(tabProfiles, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.MsgViewModel)
                    .Subscribe(vm => ViewHost.Show(tabMsgView, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.ClashProxiesViewModel)
                    .Subscribe(vm => ViewHost.Show(tabClashProxies, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.ClashConnectionsViewModel)
                    .Subscribe(vm => ViewHost.Show(tabClashConnections, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabMsgView.Visibility).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies.Visibility).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections.Visibility).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;

            case EGirdOrientation.Vertical:
                this.WhenAnyValue(v => v.ViewModel.ProfilesViewModel)
                    .Subscribe(vm => ViewHost.Show(tabProfiles1, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.MsgViewModel)
                    .Subscribe(vm => ViewHost.Show(tabMsgView1, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.ClashProxiesViewModel)
                    .Subscribe(vm => ViewHost.Show(tabClashProxies1, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.ClashConnectionsViewModel)
                    .Subscribe(vm => ViewHost.Show(tabClashConnections1, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabMsgView1.Visibility).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies1.Visibility).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections1.Visibility).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain1.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;

            case EGirdOrientation.Tab:
            default:
                this.WhenAnyValue(v => v.ViewModel.ProfilesViewModel)
                    .Subscribe(vm => ViewHost.Show(tabProfiles2, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.MsgViewModel)
                    .Subscribe(vm => ViewHost.Show(tabMsgView2, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.ClashProxiesViewModel)
                    .Subscribe(vm => ViewHost.Show(tabClashProxies2, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.ClashConnectionsViewModel)
                    .Subscribe(vm => ViewHost.Show(tabClashConnections2, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies2.Visibility).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections2.Visibility).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain2.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;
        }

        RestoreUI();
    }

    private void AddHelpMenuItem()
    {
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo();
        foreach (var it in coreInfo
            .Where(t => t.CoreType is not ECoreType.v2fly
                        and not ECoreType.hysteria))
        {
            var item = new MenuItem()
            {
                Tag = it.Url.Replace(@"/releases", ""),
                Header = string.Format(ResUI.menuWebsiteItem, it.CoreType.ToString().Replace("_", " ")).UpperFirstChar()
            };
            item.Click += MenuItem_Click;
            menuHelp.Items.Add(item);
        }
    }

    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            ProcUtils.ProcessStart(item.Tag.ToString());
        }
    }

    #endregion UI
}
