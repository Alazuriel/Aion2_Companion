
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using AION_2_Companion.Core.Capture;

namespace AION_2_Companion.Core.Capture;

public static class NetworkAdapterDetector
{
    public static IReadOnlyList<CaptureDeviceOption> Detect()
    {
        var devices = CaptureService.GetDevices();
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();

        var candidates = new List<(CaptureDeviceOption Option, int Priority)>();

        foreach (var device in devices)
        {
            NetworkInterface? networkInterface = null;

            // Windows alatt a Npcap adapterneve gyakran tartalmazza
            // a hálózati adapter GUID-ját.
            var match = Regex.Match(
                device.Name,
                @"\{([0-9a-fA-F-]{36})\}");

            if (match.Success)
            {
                var guid = match.Groups[1].Value;

                networkInterface = interfaces.FirstOrDefault(n =>
                    string.Equals(
                        n.Id.Trim('{', '}'),
                        guid,
                        StringComparison.OrdinalIgnoreCase));
            }

            var isUsable =
                networkInterface is not null &&
                networkInterface.OperationalStatus == OperationalStatus.Up &&
                networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel;

            var name = networkInterface?.Name;
            if (string.IsNullOrWhiteSpace(name))
                name = device.Description;

            if (string.IsNullOrWhiteSpace(name))
                name = device.Name;

            var option = new CaptureDeviceOption(
                device,
                name,
                isUsable);

            // Az aktív, valódi hálózati adapterek kerülnek előre.
            candidates.Add((option, isUsable ? 1 : 0));
        }

        return candidates
            .OrderByDescending(c => c.Priority)
            .ThenBy(c => c.Option.DisplayName)
            .Select(c => c.Option)
            .ToArray();
    }
}
