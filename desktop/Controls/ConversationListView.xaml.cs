using System.Windows;
using System.Windows.Controls;

namespace SoulExe.Controls;

public partial class ConversationListView : UserControl
{
    public ConversationListView() => InitializeComponent();

    private void MenuButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
        e.Handled = true;
    }
}
