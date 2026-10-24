using System.Collections.Generic;
using AION_2_Companion.Core;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AION_2_Companion.Views;

public partial class ChangelogView : UserControl
{
    public IReadOnlyList<ChangelogEntry> Entries { get; } = ChangelogService.Load();
    public string CurrentVersionText => $"v{ChangelogService.GetCurrentVersion()}";

    public ChangelogView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
    }
}
