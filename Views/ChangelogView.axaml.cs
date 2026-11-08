using System;
using System.Collections.Generic;
using AION_2_Companion.Core;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AION_2_Companion.Views;

public partial class ChangelogView : UserControl
{
    public IReadOnlyList<ChangelogEntry> Entries => ChangelogService.Load();
    public string CurrentVersionText => $"v{ChangelogService.GetCurrentVersion()}";

    public ChangelogView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
        AttachedToVisualTree += (_, _) => LocalizationService.LanguageChanged += RefreshLanguage;
        DetachedFromVisualTree += (_, _) => LocalizationService.LanguageChanged -= RefreshLanguage;
    }

    private void RefreshLanguage(object? sender, EventArgs e)
    {
        // Section headings are NFL strings. Reload the release-note model when
        // the language changes, without touching the bundled CHANGELOG.md.
        DataContext = null;
        DataContext = this;
    }
}
