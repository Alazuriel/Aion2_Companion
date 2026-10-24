using Avalonia.Controls; using Avalonia.Interactivity; using Avalonia.Markup.Xaml;
namespace AION_2_Companion.Views; public partial class BossAssistantView:UserControl{public BossAssistantView(){InitializeComponent();}private void InitializeComponent()=>AvaloniaXamlLoader.Load(this);private void OpenOverlayClick(object? s,RoutedEventArgs e)=>Overlays.OverlayWindowManager.ToggleDbm();}
