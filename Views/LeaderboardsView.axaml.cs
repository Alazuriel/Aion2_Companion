using Avalonia.Controls;

namespace AION_2_Companion.Views;

public partial class LeaderboardsView : UserControl
{
    public LeaderboardsView()
    {
        InitializeComponent();
        this.FindControl<Control>("UnderForgeContent")!.IsVisible = !Program.DeveloperMode;
        this.FindControl<Control>("DeveloperContent")!.IsVisible = Program.DeveloperMode;
    }
}
