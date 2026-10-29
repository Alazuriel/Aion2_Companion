using Avalonia;
using System;
using System.Threading;
using AION_2_Companion.Core;

namespace AION_2_Companion;

sealed class Program
{
    private const string SingleInstanceMutexName = @"Local\Nexforge.AION2Companion";
    private static Mutex? _singleInstanceMutex;

    internal static bool AutoCaptureRequested { get; private set; }
    internal static bool AutoCaptureDisabled { get; private set; }
    internal static bool DeveloperMode { get; private set; }
    internal static bool TestLoginRequested { get; private set; }
    internal static string? RequestedLanguage { get; private set; }
    internal static bool AvaloniaTraceRequested { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        bool allowMultiple = Array.Exists(args, arg =>
            arg.Equals("--allow-multiple", StringComparison.OrdinalIgnoreCase));
        if (!allowMultiple)
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                SingleInstanceMutexName,
                out bool createdNew);
            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                return;
            }
        }

        AutoCaptureRequested = Array.Exists(args, arg =>
            arg.Equals("--auto", StringComparison.OrdinalIgnoreCase));
        AutoCaptureDisabled = Array.Exists(args, arg =>
            arg.Equals("--no-auto", StringComparison.OrdinalIgnoreCase));
        DeveloperMode = Array.Exists(args, arg =>
            arg.Equals("--developer", StringComparison.OrdinalIgnoreCase));
        // Optional startup gate for testing the real account sign-in flow.
        TestLoginRequested = Array.Exists(args, arg =>
            arg.Equals("--test-login", StringComparison.OrdinalIgnoreCase));
        string? languageArgument = Array.Find(args, arg =>
            arg.StartsWith("--language=", StringComparison.OrdinalIgnoreCase));
        RequestedLanguage = languageArgument is null
            ? null
            : LocalizationService.NormalizeLanguageCode(languageArgument["--language=".Length..]);
        AvaloniaTraceRequested = Array.Exists(args, arg =>
            arg.Equals("--avalonia-trace", StringComparison.OrdinalIgnoreCase));

        DebugConsole.Initialize(args);

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            if (_singleInstanceMutex is not null)
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }
        }
    }

    // Avalonia configuration; also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        AppBuilder builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();

#if DEBUG
        // Keep the normal --debug console independent from Avalonia's very noisy
        // trace stream. Long packet captures can otherwise grow Rider's monitoring
        // data on the system drive by several gigabytes.
        if (AvaloniaTraceRequested)
            builder = builder.LogToTrace();
#endif

        return builder;
    }
}
