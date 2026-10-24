using System;
using System.Runtime.InteropServices;

namespace AION_2_Companion.Views.Overlays;

public partial class MapOverlayWindow
{
    private const uint WDA_NONE = 0x00000000;

    [DllImport("user32.dll",
        EntryPoint = "SetWindowDisplayAffinity",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDisplayAffinityNative(
        IntPtr hwnd,
        uint affinity);

    public void DisableCaptureExclusion()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var handle = TryGetPlatformHandle();

        if (handle is null || handle.Handle == IntPtr.Zero)
        {
            Console.WriteLine(
                "[MAP CAPTURE] Window handle unavailable.");
            return;
        }

        bool success = SetDisplayAffinityNative(
            handle.Handle,
            WDA_NONE);

        if (success)
        {
            Console.WriteLine(
                "[MAP CAPTURE] Overlay visible for Discord/screenshot capture.");
        }
        else
        {
            int error = Marshal.GetLastWin32Error();

            Console.WriteLine(
                $"[MAP CAPTURE ERROR] Win32={error}");
        }
    }
}