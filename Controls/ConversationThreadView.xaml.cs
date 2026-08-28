using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoulExe.Controls;

public partial class ConversationThreadView : UserControl
{
    public ConversationThreadView() => InitializeComponent();

    public void ScrollToEnd()
    {
        if (FindActiveThread<PersonalConversationThreadView>() is { } personalThread)
            personalThread.ScrollToEnd();
        else if (FindActiveThread<GroupConversationThreadView>() is { } groupThread)
            groupThread.ScrollToEnd();
    }

    public void ScrollPersonalToEnd()
    {
        if (FindActiveThread<PersonalConversationThreadView>() is { } personalThread)
            personalThread.ScrollToEnd();
    }

    public void ScrollSceneToEnd()
    {
        if (FindActiveThread<GroupConversationThreadView>() is { } groupThread)
            groupThread.ScrollToEnd();
    }

    public void ResetPersonalAutoFollow()
    {
        if (FindActiveThread<PersonalConversationThreadView>() is { } personalThread)
            personalThread.ResetAutoFollow();
    }

    public void ResetSceneAutoFollow()
    {
        if (FindActiveThread<GroupConversationThreadView>() is { } groupThread)
            groupThread.ResetAutoFollow();
    }

    public void ScrollToPersonalMessage(Guid messageId)
    {
        if (FindActiveThread<PersonalConversationThreadView>() is { } personalThread)
            personalThread.ScrollToMessage(messageId);
    }

    public void ScrollToSceneMessage(Guid messageId)
    {
        if (FindActiveThread<GroupConversationThreadView>() is { } groupThread)
            groupThread.ScrollToMessage(messageId);
    }

    private T? FindActiveThread<T>() where T : DependencyObject
        => FindVisualDescendant<T>(ActiveConversationThread);

    private static T? FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed) return typed;
            if (FindVisualDescendant<T>(child) is { } match) return match;
        }

        return null;
    }
}
