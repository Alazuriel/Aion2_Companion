using System;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace AION_2_Companion.Core;

public static class AdministratorAccess
{
    public static bool IsRunningAsAdministrator =>
        OperatingSystem.IsWindows() && IsWindowsAdministrator();

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
