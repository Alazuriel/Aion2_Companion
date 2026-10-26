using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AION_2_Companion.Core;

namespace AION_2_Companion.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private string statusText = LocalizationService.Get("CaptureStopped");

    [ObservableProperty]
    private string statusColor = "#F87171";

    [ObservableProperty]
    private bool isCapturing;

    [ObservableProperty]
    private long receivedFrames;

    [ObservableProperty]
    private long parsedOpcodes;

    [ObservableProperty]
    private long unknownOpcodes;
    
    public bool CanStartCapture => !IsCapturing;

    partial void OnIsCapturingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartCapture));
    }
}
