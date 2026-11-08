using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AION_2_Companion.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Threading.Tasks;
using AION_2_Companion.Core.Map;
using AION_2_Companion.Core.EventTimers;
using AION_2_Companion.Core.Accounts;
using AION_2_Companion.ViewModels;
using AION_2_Companion.Views.Overlays;

namespace AION_2_Companion.Views;

public partial class MainWindow : Window
{
    private MapOverlayManager? _mapManager;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _trayOpenItem;
    private NativeMenuItem? _trayExitItem;
    private bool _forceExit;
    private bool _closeDialogOpen;
    private bool _petFarmLoaded;
    private bool _automaticUpdateRunning;
    private UpdateInfo? _availableUpdate;
    private string? _lastOfferedUpdateVersion;
    private string? _lastBackgroundUpdateVersion;
    private readonly CancellationTokenSource _updateCancellation = new();
    private bool _startupCompleted;
    private bool _testLoginConnecting;
    private bool _testLoginCanCancel;
    private CancellationTokenSource? _testLoginCancelDelay;
    private readonly DispatcherTimer _automaticUpdateTimer;
    private readonly DispatcherTimer _onboardingPulseTimer;
    private List<OnboardingStep> _onboardingSteps = new();
    private int _onboardingIndex = -1;
    private string? _onboardingPageKey;
    private bool _onboardingPulseHigh;
    private string _characterDetailsReturnPage = "MyCharacters";

    private sealed record OnboardingStep(
        string PageKey,
        string TargetName,
        string TitleKey,
        string TextKey,
        Version IntroducedIn);

    public MainWindow()
    {
        InitializeComponent();

        // README exports are fully automated. Keep the render host away from the
        // interactive desktop so the pipeline never steals focus or captures a cursor.
        if (Program.ReadmeScreenshotsRequested)
        {
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(-20000, -20000);
        }

        this.FindControl<Button>("DeveloperNavButton")!.IsVisible = Program.DeveloperMode;
        this.FindControl<TextBlock>("DeveloperSectionLabel")!.IsVisible = Program.DeveloperMode;

        Opened += MainWindowOpened;
        Closing += MainWindowClosing;
        Closed += MainWindowClosed;
        CompanionSettings.MapHelperEnabledChanged += MapHelperEnabledChanged;
        LocalizationService.LanguageChanged += LanguageChanged;
        SizeChanged += (_, _) => UpdateOnboardingLayout();

        _onboardingPulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _onboardingPulseTimer.Tick += (_, _) =>
        {
            if (!this.FindControl<Grid>("OnboardingLayer")!.IsVisible)
                return;

            _onboardingPulseHigh = !_onboardingPulseHigh;
            this.FindControl<Border>("OnboardingHighlight")!.Opacity =
                _onboardingPulseHigh ? 1.0 : 0.68;
        };

        _automaticUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _automaticUpdateTimer.Tick += async (_, _) => await CheckForUpdatesAsync();

        ShowPage("Dashboard");
        if (Program.TestLoginRequested)
            StartTestLogin();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void NavClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string page)
            return;

        if (page == "DpsOverlay")
        {
            MeterSnapshot snapshot = (DataContext as MainWindowViewModel)?.BuildSnapshot()
                                     ?? new MeterSnapshot();
            OverlayWindowManager.ToggleDps(snapshot);
            return;
        }

        ShowPage(page);
    }

    private void ToggleMoreMenuClick(object? sender, RoutedEventArgs e)
    {
        StackPanel panel = this.FindControl<StackPanel>("MoreMenuPanel")!;
        SetMoreMenuExpanded(!panel.IsVisible);
    }

    private void SetMoreMenuExpanded(bool expanded)
    {
        this.FindControl<StackPanel>("MoreMenuPanel")!.IsVisible = expanded;
        this.FindControl<TextBlock>("MoreChevronText")!.Text = expanded ? "−" : "+";
    }

    private async void MainWindowOpened(object? sender, EventArgs e)
    {
        if (Program.ReadmeScreenshotsRequested)
        {
            await CaptureReadmeScreenshotsAsync();
            return;
        }

        EnsureTrayIcon();
        OverlayWindowManager.StartAutomaticPhaseward();

        if (Program.TestLoginRequested)
            return;

        await CompleteStartupAsync();
    }

    private async Task CaptureReadmeScreenshotsAsync()
    {
        try
        {
            string directory = await ReadmeScreenshotService.CaptureAsync(this);
            DebugConsole.Log("readme", $"Screenshots saved: {directory}");
        }
        catch (Exception ex)
        {
            DebugConsole.Log("readme", $"Screenshot capture failed: {ex.Message}");
        }
        finally
        {
            _forceExit = true;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            else
                Close();
        }
    }

    private Task<bool> CompleteStartupAsync()
    {
        if (_startupCompleted)
            return Task.FromResult(true);

        _startupCompleted = true;
        ApplyMapHelperState();
        EventTimerService.Shared.NotificationRaised += EventTimerNotificationRaised;
        EventTimerService.Shared.Start();
        // Never block app startup on GitHub or an update download.
        _ = CheckForUpdatesAsync(forceAtStartup: true);
        _automaticUpdateTimer.Start();
        StartOnboarding(manual: false);
        return Task.FromResult(true);
    }

    public void StartTestLogin()
    {
        _testLoginConnecting = false;
        _testLoginCanCancel = false;
        this.FindControl<TextBlock>("TestLoginStatusText")!.Text =
            LocalizationService.Get("TestLoginWaiting");
        Button button = this.FindControl<Button>("TestLoginStartButton")!;
        button.Content = LocalizationService.Get("TestLoginOpenBrowser");
        button.IsEnabled = true;
        this.FindControl<Grid>("TestLoginLayer")!.IsVisible = true;
    }

    private async void OpenTestLoginBrowserClick(object? sender, RoutedEventArgs e)
    {
        if (_testLoginConnecting)
        {
            if (_testLoginCanCancel)
            {
                this.FindControl<Button>("TestLoginStartButton")!.IsEnabled = false;
                AccountService.Shared.CancelConnect();
            }
            return;
        }

        _testLoginConnecting = true;
        _testLoginCanCancel = false;
        Button button = this.FindControl<Button>("TestLoginStartButton")!;
        button.IsEnabled = false;
        this.FindControl<TextBlock>("TestLoginStatusText")!.Text =
            LocalizationService.Get("AccountConnecting");
        ArmTestLoginCancel();

        await AccountService.Shared.ConnectAsync();
        DisarmTestLoginCancel();
        AccountServiceSnapshot snapshot = AccountService.Shared.Snapshot;
        if (snapshot.State == AccountConnectionState.Connected)
        {
            this.FindControl<Grid>("TestLoginLayer")!.IsVisible = false;
            if (await CompleteStartupAsync())
            {
                bool shouldStartCapture = !Program.AutoCaptureDisabled &&
                    (Program.AutoCaptureRequested || CompanionSettings.AutoCaptureEnabled);
                if (shouldStartCapture && DataContext is MainWindowViewModel viewModel)
                    viewModel.StartCaptureAfterLogin();
            }
        }
        else
        {
            this.FindControl<TextBlock>("TestLoginStatusText")!.Text =
                LocalizationService.Get(snapshot.StatusKey);
            button.IsEnabled = snapshot.CanConnect;
            button.Content = LocalizationService.Get("TestLoginOpenBrowser");
        }
        _testLoginConnecting = false;
    }

    private void ArmTestLoginCancel()
    {
        DisarmTestLoginCancel();
        _testLoginCancelDelay = new CancellationTokenSource();
        _ = EnableTestLoginCancelAfterDelayAsync(_testLoginCancelDelay.Token);
    }

    private async Task EnableTestLoginCancelAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            if (!_testLoginConnecting)
                return;

            _testLoginCanCancel = true;
            Button button = this.FindControl<Button>("TestLoginStartButton")!;
            button.Content = LocalizationService.Get("CancelSignIn");
            button.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void DisarmTestLoginCancel()
    {
        _testLoginCancelDelay?.Cancel();
        _testLoginCancelDelay?.Dispose();
        _testLoginCancelDelay = null;
        _testLoginCanCancel = false;
    }

    // Update checks only notify the user; installation always requires confirmation.
    private async Task<bool> CheckForUpdatesAsync(bool forceAtStartup = false)
    {
        if (_automaticUpdateRunning || !CompanionSettings.CheckUpdatesAutomatically ||
            (!forceAtStartup && !UpdateService.ShouldCheckAutomatically()))
            return false;

        _automaticUpdateRunning = true;
        try
        {
            UpdateCheckResult result = await UpdateService.CheckAsync(_updateCancellation.Token);
            if (_updateCancellation.IsCancellationRequested ||
                result.State != UpdateCheckState.UpdateAvailable || result.Update is null)
                return false;

            UpdateInfo update = result.Update;
            _availableUpdate = update;
            if (!string.Equals(_lastOfferedUpdateVersion, update.Version, StringComparison.OrdinalIgnoreCase))
            {
                _lastOfferedUpdateVersion = update.Version;
                this.FindControl<TextBlock>("UpdateNoticeText")!.Text =
                    string.Format(LocalizationService.Get("UpdateFound"), update.Version);
                this.FindControl<Border>("UpdateNotice")!.IsVisible = true;
            }

            if (CompanionSettings.DownloadUpdatesInBackground &&
                !string.Equals(_lastBackgroundUpdateVersion, update.Version, StringComparison.OrdinalIgnoreCase))
            {
                _lastBackgroundUpdateVersion = update.Version;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Only download and verify. Never launch an installer here.
                        await UpdateService.DownloadAsync(update,
                            cancellationToken: _updateCancellation.Token);
                        DebugConsole.Log("update", $"Verified update v{update.Version} is ready to install.");
                    }
                    catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        DebugConsole.Log("update", $"Optional background download failed: {ex.Message}");
                    }
                });
            }
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            DebugConsole.Log("update", $"Automatic update check failed: {ex.Message}");
        }
        finally
        {
            _automaticUpdateRunning = false;
        }
        return false;
    }

    private void UpdateLaterClick(object? sender, RoutedEventArgs e) =>
        this.FindControl<Border>("UpdateNotice")!.IsVisible = false;

    private void UpdateChangelogClick(object? sender, RoutedEventArgs e)
    {
        UpdateInfo? update = _availableUpdate;
        if (update is null)
            return;
        if (!Uri.TryCreate(update.ReleaseUrl, UriKind.Absolute, out Uri? url) ||
            url.Scheme != Uri.UriSchemeHttps ||
            !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !url.AbsolutePath.StartsWith("/Alazuriel/Aion2_Companion/releases/", StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DebugConsole.Log("update", $"Unable to open release notes: {ex.Message}");
        }
    }

    private async void UpdateOpenClick(object? sender, RoutedEventArgs e)
    {
        UpdateInfo? update = _availableUpdate;
        if (update is null)
            return;
        this.FindControl<Border>("UpdateNotice")!.IsVisible = false;
        try
        {
            await new UpdateWindow(update).ShowDialog(this);
        }
        catch (Exception ex)
        {
            DebugConsole.Log("update", $"Unable to show update window: {ex.Message}");
        }
    }

    private void TitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        BeginMoveDrag(e);
    }

    private async void MainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceExit || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            return;

        e.Cancel = true;

        MainWindowCloseAction action = CompanionSettings.CloseAction;
        if (action == MainWindowCloseAction.Ask)
        {
            if (_closeDialogOpen)
                return;

            _closeDialogOpen = true;
            var dialog = new CloseBehaviorDialog();
            MainWindowCloseAction? selected = await dialog.ShowDialog<MainWindowCloseAction?>(this);
            _closeDialogOpen = false;

            if (selected is null)
                return;

            action = selected.Value;
            CompanionSettings.CloseAction = action;
        }

        if (action == MainWindowCloseAction.MinimizeToTray)
        {
            Hide();
            return;
        }

        ExitApplication();
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null)
            return;

        _trayOpenItem = new NativeMenuItem(LocalizationService.Get("TrayOpen"));
        _trayOpenItem.Click += (_, _) => RestoreFromTray();

        _trayExitItem = new NativeMenuItem(LocalizationService.Get("TrayExit"));
        _trayExitItem.Click += (_, _) => ExitApplication();

        var menu = new NativeMenu();
        menu.Items.Add(_trayOpenItem);
        menu.Items.Add(_trayExitItem);

        using var iconStream = AssetLoader.Open(
            new Uri("avares://AION 2 Companion/Assets/nexforge-companion.ico"));

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "AION 2 Companion",
            Menu = menu,
            IsVisible = true,
        };
        _trayIcon.Clicked += (_, _) => RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void LanguageChanged(object? sender, EventArgs e)
    {
        if (_trayOpenItem is not null)
            _trayOpenItem.Header = LocalizationService.Get("TrayOpen");
        if (_trayExitItem is not null)
            _trayExitItem.Header = LocalizationService.Get("TrayExit");

        if (_onboardingIndex >= 0)
            ShowOnboardingStep();

        if (this.FindControl<Grid>("TestLoginLayer")!.IsVisible)
        {
            this.FindControl<TextBlock>("TestLoginStatusText")!.Text =
                LocalizationService.Get(_testLoginConnecting
                    ? "AccountConnecting"
                    : AccountService.Shared.Snapshot.StatusKey);
            this.FindControl<Button>("TestLoginStartButton")!.Content = LocalizationService.Get(
                _testLoginCanCancel ? "CancelSignIn" : "TestLoginOpenBrowser");
        }
    }

    private void ExitApplication()
    {
        _forceExit = true;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Close();
    }

    private void MapHelperEnabledChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(ApplyMapHelperState);

    
    private void ApplyMapHelperState()
    {
        bool isAdministrator =
            AdministratorAccess.IsRunningAsAdministrator;

        if (CompanionSettings.MapHelperEnabled && !isAdministrator)
        {
            CompanionSettings.MapHelperEnabled = false;
            return;
        }

        bool shouldRun =
            CompanionSettings.MapHelperEnabled && isAdministrator;

        if (shouldRun)
        {
            // Map Helper elindítása
            if (_mapManager is null)
            {
                _mapManager = new MapOverlayManager("AION2");
                _petFarmLoaded = false;

                Console.WriteLine("[MAP] Map Helper initialized.");
            }

            // Pet Farm adatbázis csatlakoztatása
            if (!_petFarmLoaded)
            {
                if (MainWindowViewModel._ElyosPets is { } petsDb)
                {
                    Console.WriteLine(
                        $"[PET FARM] Loading {petsDb.Pets.Count} spawn records...");

                    _mapManager.LoadPetFarmSpawns(petsDb.Pets);

                    _petFarmLoaded = true;

                    Console.WriteLine("[PET FARM] Database attached!");
                }
                else
                {
                    Console.WriteLine(
                        "[PET FARM] Elyos database not loaded yet!");
                }
            }

            return;
        }

        // Map Helper kikapcsolása
        _mapManager?.Dispose();
        _mapManager = null;
        _petFarmLoaded = false;
    }


    private void MainWindowClosed(object? sender, EventArgs e)
    {
        CompanionSettings.MapHelperEnabledChanged -= MapHelperEnabledChanged;
        LocalizationService.LanguageChanged -= LanguageChanged;
        _mapManager?.Dispose();
        _mapManager = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _automaticUpdateTimer.Stop();
        _updateCancellation.Cancel();
        _onboardingPulseTimer.Stop();
        DisarmTestLoginCancel();
        EventTimerService.Shared.NotificationRaised -= EventTimerNotificationRaised;
        EventTimerService.Shared.Stop();
    }

    private static void EventTimerNotificationRaised(object? sender, EventTimerNotificationEventArgs e) =>
        EventTimerNotificationPresenter.Show(e.Notification);

    private void ShowPage(string page)
    {
        var host = this.FindControl<ContentControl>("PageHost");
        if (host is null)
            return;

        bool isMorePage = page is "Guides" or "CharacterSearch" or
            "Leaderboards" or "Changelog" or "Support";
        SetMoreMenuExpanded(isMorePage);

        host.Content = page switch
        {
            "CombatLog" => new CombatLogWindow(),
            "Account" => CreateAccountView(),
            "MyCharacters" => CreateMyCharactersView(),
            "CharacterDetails" => CreateCharacterDetailsView(),
            "CharacterSearch" => CreateCharacterSearchView(),
            "Leaderboards" => new LeaderboardsView(),
            "EventTimers" => new EventTimersView(),
            "Guides" => new UnderForgeView(),
            "Settings" => new SettingsView(),
            "Changelog" => new ChangelogView(),
            "Support" => new DiagnosticsView(),
            "Debug" when Program.DeveloperMode => new CaptureMonitorView(),
            _ => new CommandCenterView(),
        };

        // A Combat Log sajat, belso gyujtemenyeket hasznal. Ne irjuk felul a
        // konstruktoraban beallitott DataContextet a foablakeval.
        if (host.Content is Control pageView &&
            host.Content is not CombatLogWindow &&
            host.Content is not LeaderboardsView &&
            host.Content is not ChangelogView &&
            host.Content is not EventTimersView &&
            host.Content is not AccountView &&
            host.Content is not MyCharactersView &&
            host.Content is not CharacterSearchView &&
            host.Content is not CharacterDetailsView)
            pageView.DataContext = DataContext;

        foreach (var button in this.GetVisualDescendants().OfType<Button>())
        {
            if (!button.Classes.Contains("nav"))
                continue;

            string? tag = button.Tag as string;
            var selected = tag == page ||
                           (page is "MyCharacters" or "CharacterDetails" && tag == "Account");
            if (selected)
            {
                if (!button.Classes.Contains("active"))
                    button.Classes.Add("active");
            }
            else
            {
                button.Classes.Remove("active");
            }
        }

        Button moreButton = this.FindControl<Button>("MoreNavButton")!;
        if (isMorePage)
        {
            if (!moreButton.Classes.Contains("active"))
                moreButton.Classes.Add("active");
        }
        else
        {
            moreButton.Classes.Remove("active");
        }
    }

    public void NavigateTo(string page) => ShowPage(page);

    private AccountView CreateAccountView()
    {
        var view = new AccountView();
        view.OpenCharactersRequested += (_, _) => ShowPage("MyCharacters");
        return view;
    }

    private MyCharactersView CreateMyCharactersView()
    {
        var view = new MyCharactersView();
        view.OpenCharacterRequested += (_, _) =>
        {
            _characterDetailsReturnPage = "MyCharacters";
            ShowPage("CharacterDetails");
        };
        view.OpenSearchRequested += (_, _) => ShowPage("CharacterSearch");
        return view;
    }

    private CharacterSearchView CreateCharacterSearchView()
    {
        var view = new CharacterSearchView();
        view.OpenSelfRequested += (_, _) => ShowPage("MyCharacters");
        view.OpenProfileRequested += (_, _) =>
        {
            _characterDetailsReturnPage = "CharacterSearch";
            ShowPage("CharacterDetails");
        };
        return view;
    }

    private CharacterDetailsView CreateCharacterDetailsView()
    {
        var view = new CharacterDetailsView();
        view.BackRequested += (_, _) => ShowPage(_characterDetailsReturnPage);
        return view;
    }

    public void StartOnboarding(bool manual)
    {
        Version current = ParseVersion(ChangelogService.GetCurrentVersion());
        Version? lastSeen = string.IsNullOrWhiteSpace(CompanionSettings.LastSeenOnboardingVersion)
            ? null
            : ParseVersion(CompanionSettings.LastSeenOnboardingVersion!);

        if (!manual && lastSeen is not null && lastSeen >= current)
            return;

        var all = new List<OnboardingStep>
        {
            new("Dashboard", "HomeNavButton", "OnboardingHomeTitle", "OnboardingHomeText", new Version(0, 1, 0)),
            new("Account", "AccountNavButton", "OnboardingAccountTitle", "OnboardingAccountText", new Version(0, 2, 2)),
            new("Dashboard", "DpsNavButton", "OnboardingDpsTitle", "OnboardingDpsText", new Version(0, 1, 0)),
            new("CombatLog", "CombatLogNavButton", "OnboardingCombatLogTitle", "OnboardingCombatLogText", new Version(0, 1, 0)),
            new("EventTimers", "EventTimersNavButton", "OnboardingTimersTitle", "OnboardingTimersText", new Version(0, 1, 5)),
            new("Leaderboards", "LeaderboardsNavButton", "OnboardingLeaderboardsTitle", "OnboardingLeaderboardsText", new Version(0, 2, 2)),
            new("Settings", "SettingsNavButton", "OnboardingSettingsTitle", "OnboardingSettingsText", new Version(0, 1, 0)),
            new("Support", "SupportNavButton", "OnboardingSupportTitle", "OnboardingSupportText", new Version(0, 1, 0)),
            new("Changelog", "ChangelogNavButton", "OnboardingChangelogTitle", "OnboardingChangelogText", new Version(0, 1, 0)),
        };

        _onboardingSteps = manual || lastSeen is null
            ? all
            : all.Where(step => step.IntroducedIn > lastSeen && step.IntroducedIn <= current).ToList();

        if (_onboardingSteps.Count == 0)
        {
            CompanionSettings.LastSeenOnboardingVersion = current.ToString(3);
            return;
        }

        _onboardingIndex = 0;
        _onboardingPageKey = null;
        this.FindControl<Grid>("OnboardingLayer")!.IsVisible = true;
        _onboardingPulseTimer.Start();
        ShowOnboardingStep();
    }

    private void ShowOnboardingStep()
    {
        if (_onboardingIndex < 0 || _onboardingIndex >= _onboardingSteps.Count)
            return;

        OnboardingStep step = _onboardingSteps[_onboardingIndex];
        // A language switch must not destroy and recreate the active tour page.
        if (!string.Equals(_onboardingPageKey, step.PageKey, StringComparison.Ordinal))
        {
            ShowPage(step.PageKey);
            _onboardingPageKey = step.PageKey;
        }
        this.FindControl<TextBlock>("OnboardingCounter")!.Text =
            LocalizationService.Format("OnboardingStep", _onboardingIndex + 1, _onboardingSteps.Count);
        this.FindControl<TextBlock>("OnboardingTitle")!.Text = LocalizationService.Get(step.TitleKey);
        this.FindControl<TextBlock>("OnboardingText")!.Text = LocalizationService.Get(step.TextKey);
        this.FindControl<Button>("OnboardingPreviousButton")!.IsVisible = _onboardingIndex > 0;
        this.FindControl<Button>("OnboardingNextButton")!.Content =
            LocalizationService.Get(_onboardingIndex == _onboardingSteps.Count - 1 ? "Finish" : "Next");

        Dispatcher.UIThread.Post(UpdateOnboardingLayout, DispatcherPriority.Loaded);
    }

    private void UpdateOnboardingLayout()
    {
        if (_onboardingIndex < 0 || _onboardingIndex >= _onboardingSteps.Count)
            return;

        Grid layer = this.FindControl<Grid>("OnboardingLayer")!;
        Canvas canvas = this.FindControl<Canvas>("OnboardingCanvas")!;
        Control? target = this.FindControl<Control>(_onboardingSteps[_onboardingIndex].TargetName);
        if (!layer.IsVisible || target is null || layer.Bounds.Width <= 0 || layer.Bounds.Height <= 0)
            return;

        Point? origin = target.TranslatePoint(new Point(0, 0), layer);
        if (origin is null)
            return;

        double fullWidth = layer.Bounds.Width;
        double fullHeight = layer.Bounds.Height;
        const double gap = 7;
        double x = Math.Clamp(origin.Value.X - gap, 0, fullWidth);
        double y = Math.Clamp(origin.Value.Y - gap, 0, fullHeight);
        double width = Math.Max(0, Math.Min(fullWidth - x, target.Bounds.Width + gap * 2));
        double height = Math.Max(0, Math.Min(fullHeight - y, target.Bounds.Height + gap * 2));

        canvas.Width = fullWidth;
        canvas.Height = fullHeight;
        SetCanvasRect(this.FindControl<Border>("OnboardingShadeTop")!, 0, 0, fullWidth, y);
        SetCanvasRect(this.FindControl<Border>("OnboardingShadeLeft")!, 0, y, x, height);
        SetCanvasRect(this.FindControl<Border>("OnboardingShadeRight")!, x + width, y,
            Math.Max(0, fullWidth - x - width), height);
        SetCanvasRect(this.FindControl<Border>("OnboardingShadeBottom")!, 0, y + height,
            fullWidth, Math.Max(0, fullHeight - y - height));
        SetCanvasRect(this.FindControl<Border>("OnboardingHighlight")!, x, y, width, height);

        // Never position the help card outside the current window. Avoid a
        // fixed height because NFL translations wrap differently per language.
        const double margin = 16;
        const double separation = 16;
        Border card = this.FindControl<Border>("OnboardingCard")!;
        double cardWidth = Math.Min(380, Math.Max(0, fullWidth - margin * 2));
        card.Width = cardWidth;
        card.MaxHeight = Math.Max(0, fullHeight - margin * 2);
        card.Measure(new Size(cardWidth, Math.Max(0, fullHeight - margin * 2)));
        double cardHeight = Math.Min(card.DesiredSize.Height, card.MaxHeight);
        double rightX = x + width + separation;
        double leftX = x - cardWidth - separation;
        bool fitsRight = rightX + cardWidth <= fullWidth - margin;
        bool fitsLeft = leftX >= margin;

        double cardX = fitsRight ? rightX : fitsLeft ? leftX : Math.Max(margin, (fullWidth - cardWidth) / 2);
        double cardY = y + height / 2 - cardHeight / 2;
        if (!fitsRight && !fitsLeft)
        {
            // Narrow windows: prefer below the highlighted control or above
            // when more vertical space is available.
            double belowY = y + height + separation;
            double aboveY = y - cardHeight - separation;
            cardY = belowY + cardHeight <= fullHeight - margin ? belowY
                : aboveY >= margin ? aboveY
                : Math.Max(margin, fullHeight - cardHeight - margin);
        }
        cardY = Math.Clamp(cardY, margin, Math.Max(margin, fullHeight - cardHeight - margin));
        Canvas.SetLeft(card, cardX);
        Canvas.SetTop(card, cardY);
    }

    private static void SetCanvasRect(Control control, double x, double y, double width, double height)
    {
        Canvas.SetLeft(control, x);
        Canvas.SetTop(control, y);
        control.Width = Math.Max(0, width);
        control.Height = Math.Max(0, height);
    }

    private void OnboardingPreviousClick(object? sender, RoutedEventArgs e)
    {
        if (_onboardingIndex <= 0)
            return;
        _onboardingIndex--;
        ShowOnboardingStep();
    }

    private void OnboardingNextClick(object? sender, RoutedEventArgs e)
    {
        if (_onboardingIndex < _onboardingSteps.Count - 1)
        {
            _onboardingIndex++;
            ShowOnboardingStep();
            return;
        }

        CompleteOnboarding();
    }

    private void OnboardingSkipClick(object? sender, RoutedEventArgs e) => CompleteOnboarding();

    private void CompleteOnboarding()
    {
        CompanionSettings.LastSeenOnboardingVersion = ChangelogService.GetCurrentVersion();
        _onboardingIndex = -1;
        _onboardingPageKey = null;
        _onboardingSteps.Clear();
        _onboardingPulseTimer.Stop();
        this.FindControl<Grid>("OnboardingLayer")!.IsVisible = false;
    }

    private static Version ParseVersion(string value) =>
        Version.TryParse(value, out Version? version) ? version : new Version(0, 0, 0);
}
