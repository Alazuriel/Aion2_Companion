using Avalonia.Controls;

namespace AION_2_Companion.Views;

public partial class CharacterSearchView : UserControl
{
    public CharacterSearchView()
    {
        InitializeComponent();
        this.FindControl<Control>("UnderForgeContent")!.IsVisible = !Program.DeveloperMode;
        this.FindControl<Control>("DeveloperContent")!.IsVisible = Program.DeveloperMode;
    }
}
