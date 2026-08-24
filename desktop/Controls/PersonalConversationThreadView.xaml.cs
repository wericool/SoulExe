using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoulExe.ViewModels;

namespace SoulExe.Controls;

public partial class PersonalConversationThreadView : UserControl
{
    public PersonalConversationThreadView() => InitializeComponent();

    public void ScrollToEnd() => ChatMessagesScroll.ScrollToEnd();

    public void ScrollToMessage(Guid messageId)
    {
        var target = FindVisualDescendant<FrameworkElement>(ChatMessagesScroll,
            element => element.DataContext is ChatMessageViewModel message && message.MessageId == messageId);
        if (target is null) return;
        var position = target.TransformToAncestor(ChatMessagesScroll).Transform(new Point(0, 0));
        ChatMessagesScroll.ScrollToVerticalOffset(Math.Max(0, ChatMessagesScroll.VerticalOffset + position.Y - 34));
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
