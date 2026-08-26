using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SoulExe.ViewModels;

namespace SoulExe.Controls;

public partial class GroupConversationThreadView : UserControl
{
    private const double BottomTolerance = 2;
    private bool _autoFollow = true;
    private bool _isProgrammaticScroll;
    private ScrollViewer? _scrollViewer;

    public GroupConversationThreadView() => InitializeComponent();

    public void ScrollToEnd()
    {
        if (!_autoFollow) return;
        ScrollToBottom();
    }

    public void ScrollToMessage(Guid messageId)
    {
        _autoFollow = false;
        UpdateNewMessagesButton();
        var message = SceneMessagesList.Items.OfType<SceneMessageViewModel>().FirstOrDefault(item => item.Id == messageId);
        if (message is null) return;
        SceneMessagesList.ScrollIntoView(message);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ScrollRealizedMessage(messageId)));
    }

    private void SceneMessagesScroll_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        _scrollViewer ??= e.OriginalSource as ScrollViewer;
        if (_isProgrammaticScroll) return;
        _autoFollow = IsAtBottom();
        UpdateNewMessagesButton();
    }

    private void NewMessagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        _autoFollow = true;
        UpdateNewMessagesButton();
        SmoothScrollToBottom();
    }

    private void LoadOlderMessagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        _autoFollow = false;
        UpdateNewMessagesButton();
    }

    private bool IsAtBottom() => _scrollViewer is null || _scrollViewer.ScrollableHeight - _scrollViewer.VerticalOffset <= BottomTolerance;

    private void ScrollToBottom() => ScrollToOffset(_scrollViewer?.ScrollableHeight ?? 0);

    private void ScrollToOffset(double offset)
    {
        _isProgrammaticScroll = true;
        try { _scrollViewer?.ScrollToVerticalOffset(offset); }
        finally { _isProgrammaticScroll = false; }
    }

    private void SmoothScrollToBottom()
    {
        var start = _scrollViewer?.VerticalOffset ?? 0;
        var target = _scrollViewer?.ScrollableHeight ?? 0;
        const int steps = 8;
        var step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            step++;
            ScrollToOffset(start + (target - start) * step / steps);
            if (step < steps) return;
            timer.Stop();
            ScrollToBottom();
        };
        timer.Start();
    }

    private void UpdateNewMessagesButton() => NewMessagesButton.Visibility = _autoFollow ? Visibility.Collapsed : Visibility.Visible;

    private void ScrollRealizedMessage(Guid messageId)
    {
        if (_scrollViewer is null) return;
        var target = FindVisualDescendant<FrameworkElement>(SceneMessagesList,
            element => element.DataContext is SceneMessageViewModel message && message.Id == messageId);
        if (target is null) return;
        var position = target.TransformToAncestor(_scrollViewer).Transform(new Point(0, 0));
        ScrollToOffset(Math.Max(0, _scrollViewer.VerticalOffset + position.Y - 34));
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
