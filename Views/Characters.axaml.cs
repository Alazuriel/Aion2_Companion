using AION_2_Companion.ViewModels;
using Avalonia.Controls;

namespace AION_2_Companion.Views;

public partial class Characters : UserControl
{
    private readonly CharactersViewModel _viewModel = new();

    public Characters()
    {
        InitializeComponent();
        DataContext = _viewModel;
        DetachedFromVisualTree += (_, _) => _viewModel.Dispose();
    }
}
