using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoulExe.ViewModels;

namespace SoulExe.Controls;

public partial class GroupConversationThreadView : UserControl
{
    public GroupConversationThreadView() => InitializeComponent();

    public void ScrollToEnd() => SceneMessagesScroll.ScrollToEnd();

    public void ScrollToMessage(Guid messageId)
    {
        var target = FindVisualDescendant<FrameworkElement>(SceneMessagesScroll,
            element => element.DataContext is SceneMessageViewModel message && message.Id == messageId);
        if (target is null) return;
        var position = target.TransformToAncestor(SceneMessagesScroll).Transform(new Point(0, 0));
        SceneMessagesScroll.ScrollToVerticalOffset(Math.Max(0, SceneMessagesScroll.VerticalOffset + position.Y - 34));
    }

    private void MessageMenuButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private static T? FindVisualDescendant<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed && predicate(typed)) return typed;
            var match = FindVisualDescendant(child, predicate);
            if (match is not null) return match;
        }
        return null;
    }
}
