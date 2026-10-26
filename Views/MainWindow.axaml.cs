using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    private bool _startupCompleted;
    private bool _testLoginBrowserOpened;
    private readonly DispatcherTimer _automaticUpdateTimer;
    private readonly DispatcherTimer _onboardingPulseTimer;
    private List<OnboardingStep> _onboardingSteps = new();
    private int _onboardingIndex = -1;
    private bool _onboardingPulseHigh;

    private sealed record OnboardingStep(
        string PageKey,
        string TargetName,
        string TitleKey,
        string TextKey,
        Version IntroducedIn);

    public MainWindow()
    {
        InitializeComponent();

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
        EnsureTrayIcon();

        if (Program.TestLoginRequested)
            return;

        await CompleteStartupAsync();
    }

    private async Task<bool> CompleteStartupAsync()
    {
        if (_startupCompleted)
            return true;

        _startupCompleted = true;
        ApplyMapHelperState();
        EventTimerService.Shared.NotificationRaised += EventTimerNotificationRaised;
        EventTimerService.Shared.Start();
        bool updateStarted = await CheckForUpdatesAsync();
        if (updateStarted)
            return false;

        _automaticUpdateTimer.Start();
        StartOnboarding(manual: false);
        return true;
    }

    public void StartTestLogin()
    {
        _testLoginBrowserOpened = false;
        this.FindControl<TextBlock>("TestLoginStatusText")!.Text =
            LocalizationService.Get("TestLoginWaiting");
        this.FindControl<Button>("TestLoginContinueButton")!.IsEnabled = false;
        this.FindControl<Grid>("TestLoginLayer")!.IsVisible = true;
    }

    private void OpenTestLoginBrowserClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Program.TestLoginUrl) { UseShellExecute = true });
            _testLoginBrowserOpened = true;
            this.FindControl<TextBlock>("TestLoginStatusText")!.Text =
                LocalizationService.Get("TestLoginBrowserOpened");
            this.FindControl<Button>("TestLoginContinueButton")!.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _testLoginBrowserOpened = false;
            this.FindControl<TextBlock>("TestLoginStatusText")!.Text =
                LocalizationService.Get("TestLoginBrowserFailed");
            DebugConsole.Log("auth-test", $"Test login page could not be opened: {ex.Message}");
        }
    }

    private async void CompleteTestLoginClick(object? sender, RoutedEventArgs e)
    {
        if (!_testLoginBrowserOpened)
            return;

        this.FindControl<Grid>("TestLoginLayer")!.IsVisible = false;
        if (!await CompleteStartupAsync())
            return;

        bool shouldStartCapture = !Program.AutoCaptureDisabled &&
            (Program.AutoCaptureRequested || CompanionSettings.AutoCaptureEnabled);
        if (shouldStartCapture && DataContext is MainWindowViewModel viewModel)
            viewModel.StartCaptureAfterLogin();
    }

    private async Task<bool> CheckForUpdatesAsync()
    {
        if (_automaticUpdateRunning || !UpdateService.ShouldCheckAutomatically())
            return false;

        _automaticUpdateRunning = true;
        try
        {
            UpdateCheckResult result = await UpdateService.CheckAsync();
            if (result.State != UpdateCheckState.UpdateAvailable || result.Update is null)
                return false;

            DebugConsole.Log("update", $"Downloading verified update {result.Update.Version} in the background.");
            string installer = await UpdateService.DownloadAsync(result.Update);
            UpdateService.LaunchSilentInstaller(installer);
            DebugConsole.Log("update", "Background updater started; restarting the Companion.");
            ExitApplication();
            return true;
        }
        catch (Exception ex)
        {
            DebugConsole.Log("update", $"Automatic update failed: {ex.Message}");
            return false;
        }
        finally
        {
            _automaticUpdateRunning = false;
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
                LocalizationService.Get(_testLoginBrowserOpened
                    ? "TestLoginBrowserOpened"
                    : "TestLoginWaiting");
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
        _onboardingPulseTimer.Stop();
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

        bool isMorePage = page is "Boss" or "Guides" or "Changelog" or "Support";
        SetMoreMenuExpanded(isMorePage);

        host.Content = page switch
        {
            "CombatLog" => new CombatLogWindow(),
            "Characters" => new CharactersView(),
            "EventTimers" => new EventTimersView(),
            "Boss" => new UnderForgeView(),
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
            host.Content is not ChangelogView &&
            host.Content is not EventTimersView)
            pageView.DataContext = DataContext;

        foreach (var button in this.GetVisualDescendants().OfType<Button>())
        {
            if (!button.Classes.Contains("nav"))
                continue;

            var selected = (button.Tag as string) == page;
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
            new("Dashboard", "DpsNavButton", "OnboardingDpsTitle", "OnboardingDpsText", new Version(0, 1, 0)),
            new("CombatLog", "CombatLogNavButton", "OnboardingCombatLogTitle", "OnboardingCombatLogText", new Version(0, 1, 0)),
            new("EventTimers", "EventTimersNavButton", "OnboardingTimersTitle", "OnboardingTimersText", new Version(0, 1, 5)),
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
        this.FindControl<Grid>("OnboardingLayer")!.IsVisible = true;
        _onboardingPulseTimer.Start();
        ShowOnboardingStep();
    }

    private void ShowOnboardingStep()
    {
        if (_onboardingIndex < 0 || _onboardingIndex >= _onboardingSteps.Count)
            return;

        OnboardingStep step = _onboardingSteps[_onboardingIndex];
        ShowPage(step.PageKey);
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

        const double gap = 7;
        double x = Math.Max(0, origin.Value.X - gap);
        double y = Math.Max(0, origin.Value.Y - gap);
        double width = Math.Min(layer.Bounds.Width - x, target.Bounds.Width + gap * 2);
        double height = Math.Min(layer.Bounds.Height - y, target.Bounds.Height + gap * 2);
        double fullWidth = layer.Bounds.Width;
        double fullHeight = layer.Bounds.Height;

        canvas.Width = fullWidth;
        canvas.Height = fullHeight;
        SetCanvasRect(this.FindControl<Border>("OnboardingShadeTop")!, 0, 0, fullWidth, y);
        SetCanvasRect(this.FindControl<Border>("OnboardingShadeLeft")!, 0, y, x, height);
        SetCanvasRect(this.FindControl<Border>("OnboardingShadeRight")!, x + width, y,
            Math.Max(0, fullWidth - x - width), height);
        SetCanvasRect(this.FindControl<Border>("OnboardingShadeBottom")!, 0, y + height,
            fullWidth, Math.Max(0, fullHeight - y - height));
        SetCanvasRect(this.FindControl<Border>("OnboardingHighlight")!, x, y, width, height);

        const double cardWidth = 380;
        const double cardHeight = 270;
        double cardX = x + width + 22;
        if (cardX + cardWidth > fullWidth - 20)
            cardX = Math.Max(20, x - cardWidth - 22);
        double cardY = Math.Clamp(y + height / 2 - cardHeight / 2, 20, Math.Max(20, fullHeight - cardHeight - 20));
        Border card = this.FindControl<Border>("OnboardingCard")!;
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
        _onboardingSteps.Clear();
        _onboardingPulseTimer.Stop();
        this.FindControl<Grid>("OnboardingLayer")!.IsVisible = false;
    }

    private static Version ParseVersion(string value) =>
        Version.TryParse(value, out Version? version) ? version : new Version(0, 0, 0);
}
