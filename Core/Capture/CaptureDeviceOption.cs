using SharpPcap;

namespace AION_2_Companion.Core.Capture;

public sealed class CaptureDeviceOption
{
    public ICaptureDevice Device { get; }
    public string DisplayName { get; }
    public bool IsRecommended { get; }

    public CaptureDeviceOption(
        ICaptureDevice device,
        string name,
        bool isRecommended)
    {
        Device = device;
        IsRecommended = isRecommended;

        DisplayName = isRecommended
            ? $"{name} — ajánlott ({device.Name})"
            : $"{name} ({device.Name})";
    }

    public override string ToString() => DisplayName;
}