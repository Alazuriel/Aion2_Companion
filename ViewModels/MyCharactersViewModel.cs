using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;
using Avalonia.Threading;

namespace AION_2_Companion.ViewModels;

public sealed class MyCharactersViewModel : INotifyPropertyChanged, IDisposable
{
    private DetectedCharacterSnapshot? _character = DetectedCharacterService.Shared.Current;

    public MyCharactersViewModel()
    {
        DetectedCharacterService.Shared.Changed += CharacterChanged;
        LocalizationService.LanguageChanged += LanguageChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsDeveloperMode => Program.DeveloperMode;
    public bool ShowUnderForge => !IsDeveloperMode;
    public bool HasCharacter => _character is not null;
    public bool HasNoCharacter => _character is null;
    public string Name => _character?.Name ?? "—";
    public string ClassName => _character?.ClassName ?? "—";
    public string? ClassIconSource => _character?.ClassIconPath;
    public string LevelText => _character?.Level is byte level
        ? LocalizationService.Format("LevelValue", level)
        : LocalizationService.Get("UnknownLevel");
    public string GearScoreText => _character?.GearScore is uint score
        ? LocalizationService.Format("GearScoreValue", score)
        : LocalizationService.Get("UnknownGearScore");
    public string ServerText => _character?.ServerId is ushort serverId
        ? LocalizationService.Format("ServerIdUnconfirmed", serverId)
        : LocalizationService.Get("UnknownServer");
    public string DetectedText => _character is null
        ? string.Empty
        : LocalizationService.Format("DetectedAt", _character.DetectedAtUtc.ToLocalTime().ToString("g"));
    public string CharacterTooltip => HasCharacter
        ? $"{Name}\n{ClassName}\n{LevelText}\n{GearScoreText}"
        : LocalizationService.Get("NoCharacterText");

    private void CharacterChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        _character = DetectedCharacterService.Shared.Current;
        NotifyAll();
    });

    private void LanguageChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(NotifyAll);

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(HasCharacter));
        OnPropertyChanged(nameof(HasNoCharacter));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ClassName));
        OnPropertyChanged(nameof(ClassIconSource));
        OnPropertyChanged(nameof(LevelText));
        OnPropertyChanged(nameof(GearScoreText));
        OnPropertyChanged(nameof(ServerText));
        OnPropertyChanged(nameof(DetectedText));
        OnPropertyChanged(nameof(CharacterTooltip));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        DetectedCharacterService.Shared.Changed -= CharacterChanged;
        LocalizationService.LanguageChanged -= LanguageChanged;
    }
}
