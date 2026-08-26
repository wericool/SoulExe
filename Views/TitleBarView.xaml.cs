using System.Windows;
using System.Windows.Controls;

namespace SoulExe.Views;

public partial class TitleBarView : UserControl
{
    public TitleBarView() => InitializeComponent();

    private void MinimizeWindowButton_OnClick(object sender, RoutedEventArgs e) => Window.GetWindow(this)!.WindowState = WindowState.Minimized;
    private void ToggleMaximizeWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this)!;
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
    private void CloseWindowButton_OnClick(object sender, RoutedEventArgs e) => Window.GetWindow(this)!.Close();
}
