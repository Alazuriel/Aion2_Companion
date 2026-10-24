using System;
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
using AION_2_Companion.Core.Map;
using AION_2_Companion.ViewModels;

namespace AION_2_Companion.Views;

public partial class MainWindow : Window
{
    private MapOverlayManager? _mapManager;
    private TrayIcon? _trayIcon;
    private bool _forceExit;
    private bool _closeDialogOpen;
    private bool _petFarmLoaded;

    public MainWindow()
    {
        InitializeComponent();

        Opened += MainWindowOpened;
        Closing += MainWindowClosing;
        Closed += MainWindowClosed;
        CompanionSettings.MapHelperEnabledChanged += MapHelperEnabledChanged;

        ShowPage("Dashboard");
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void NavClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string page)
            ShowPage(page);
    }

    private void MainWindowOpened(object? sender, EventArgs e)
    {
        EnsureTrayIcon();
        ApplyMapHelperState();
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

        var openItem = new NativeMenuItem("Companion megnyitása");
        openItem.Click += (_, _) => RestoreFromTray();

        var exitItem = new NativeMenuItem("Kilépés");
        exitItem.Click += (_, _) => ExitApplication();

        var menu = new NativeMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(exitItem);

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
        _mapManager?.Dispose();
        _mapManager = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private void ShowPage(string page)
    {
        var host = this.FindControl<ContentControl>("PageHost");
        if (host is null)
            return;

        host.Content = page switch
        {
            "Analytics" => new CombatAnalyticsView(),
            "CombatLog" => new CombatLogWindow(),
            "Boss" => new BossAssistantView(),
            "Guides" => new GuidesView(),
            "Settings" => new SettingsView(),
            "Debug" => new CaptureMonitorView(),
            _ => new DashboardView(),
        };

        // A Combat Log sajat, belso gyujtemenyeket hasznal. Ne irjuk felul a
        // konstruktoraban beallitott DataContextet a foablakeval.
        if (host.Content is Control pageView && host.Content is not CombatLogWindow)
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
    }
}
