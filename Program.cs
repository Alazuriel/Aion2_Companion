using Avalonia;
using System;
using AION_2_Companion.Core;

namespace AION_2_Companion;

sealed class Program
{
    internal static bool AutoCaptureRequested { get; private set; }
    internal static bool AutoCaptureDisabled { get; private set; }
    internal static bool DeveloperMode { get; private set; }
    internal static string? RequestedLanguage { get; private set; }
    internal static bool AvaloniaTraceRequested { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        AutoCaptureRequested = Array.Exists(args, arg =>
            arg.Equals("--auto", StringComparison.OrdinalIgnoreCase));
        AutoCaptureDisabled = Array.Exists(args, arg =>
            arg.Equals("--no-auto", StringComparison.OrdinalIgnoreCase));
        DeveloperMode = Array.Exists(args, arg =>
            arg.Equals("--developer", StringComparison.OrdinalIgnoreCase));
        string? languageArgument = Array.Find(args, arg =>
            arg.StartsWith("--language=", StringComparison.OrdinalIgnoreCase));
        RequestedLanguage = languageArgument is null
            ? null
            : LocalizationService.NormalizeLanguageCode(languageArgument["--language=".Length..]);
        AvaloniaTraceRequested = Array.Exists(args, arg =>
            arg.Equals("--avalonia-trace", StringComparison.OrdinalIgnoreCase));

        DebugConsole.Initialize(args);

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
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
