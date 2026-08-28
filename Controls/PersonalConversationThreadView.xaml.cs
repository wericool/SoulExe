using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SoulExe.ViewModels;

namespace SoulExe.Controls;

public partial class PersonalConversationThreadView : UserControl
{
    private const double BottomTolerance = 2;
    private bool _autoFollow = true;
    private ScrollViewer? _scrollViewer;
    private DispatcherTimer? _autoFollowTimer;
    private bool _manualPointerScroll;
    private bool _manualScrollCheckQueued;

    public PersonalConversationThreadView()
    {
        InitializeComponent();
        Loaded += PersonalConversationThreadView_OnLoaded;
        Unloaded += PersonalConversationThreadView_OnUnloaded;
    }

    public void ScrollToEnd()
    {
        if (!_autoFollow) return;
        EnsureScrollViewer();
        _autoFollowTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _autoFollowTimer.Tick -= AutoFollowTimer_OnTick;
        _autoFollowTimer.Tick += AutoFollowTimer_OnTick;
        if (!_autoFollowTimer.IsEnabled) _autoFollowTimer.Start();
    }

    public void ResetAutoFollow()
    {
        _autoFollow = true;
        UpdateNewMessagesButton();
        ScrollToEnd();
        // Loading an edited/deleted history replaces the whole visual tree. The
        // first pass can run before the new bubbles change ScrollableHeight, so
        // follow once again after layout has completed.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (_autoFollow) ScrollToEnd();
        }));
    }

    private void PersonalConversationThreadView_OnLoaded(object sender, RoutedEventArgs e) => ScrollToEnd();

    private void PersonalConversationThreadView_OnUnloaded(object sender, RoutedEventArgs e) => _autoFollowTimer?.Stop();

    private void AutoFollowTimer_OnTick(object? sender, EventArgs e)
    {
        _autoFollowTimer?.Stop();
        if (!_autoFollow) return;
        EnsureScrollViewer();
        if (_scrollViewer is null)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ScrollToEnd));
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            if (_autoFollow) ScrollToBottom();
        }));
    }

    public void ScrollToMessage(Guid messageId)
    {
        _autoFollow = false;
        UpdateNewMessagesButton();
        var message = ChatMessagesList.Items.OfType<ChatMessageViewModel>().FirstOrDefault(item => item.MessageId == messageId);
        if (message is null) return;
        ChatMessagesList.ScrollIntoView(message);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ScrollRealizedMessage(messageId)));
    }

    private void ChatMessagesScroll_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer scrollViewer) return;
        _scrollViewer = scrollViewer;
    }

    // Extent changes from streamed tokens are reported through ScrollChanged too.
    // Only real input is allowed to turn auto-follow off.
    private void ChatMessages_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) => QueueManualScrollStateCheck();

    private void ChatMessages_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            QueueManualScrollStateCheck();
    }

    private void ChatMessages_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualAncestor<ScrollBar>(e.OriginalSource as DependencyObject) is null) return;
        _manualPointerScroll = true;
        _autoFollow = false;
        UpdateNewMessagesButton();
    }

    private void ChatMessages_OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_manualPointerScroll) return;
        _manualPointerScroll = false;
        QueueManualScrollStateCheck();
    }

    private void QueueManualScrollStateCheck()
    {
        if (_manualScrollCheckQueued) return;
        _manualScrollCheckQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _manualScrollCheckQueued = false;
            EnsureScrollViewer();
            _autoFollow = IsAtBottom();
            UpdateNewMessagesButton();
        }));
    }

    private void NewMessagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        _autoFollow = true;
        UpdateNewMessagesButton();
        ScrollToEnd();
    }

    private void LoadOlderMessagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        _autoFollow = false;
        UpdateNewMessagesButton();
    }

    private bool IsAtBottom() => _scrollViewer is null || _scrollViewer.ScrollableHeight - _scrollViewer.VerticalOffset <= BottomTolerance;

    private void ScrollToBottom()
    {
        EnsureScrollViewer();
        ScrollToOffset(_scrollViewer?.ScrollableHeight ?? 0);
    }

    private void ScrollToOffset(double offset)
    {
        _scrollViewer?.ScrollToVerticalOffset(offset);
    }

    private void UpdateNewMessagesButton() => NewMessagesButton.Visibility = _autoFollow ? Visibility.Collapsed : Visibility.Visible;

    private void ScrollRealizedMessage(Guid messageId)
    {
        EnsureScrollViewer();
        if (_scrollViewer is null) return;
        var target = FindVisualDescendant<FrameworkElement>(ChatMessagesList,
            element => element.DataContext is ChatMessageViewModel message && message.MessageId == messageId);
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

    private void ChatIdentity_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || !viewModel.NavigateCommand.CanExecute("Characters")) return;
        viewModel.NavigateCommand.Execute("Characters");
        e.Handled = true;
    }

    private void EnsureScrollViewer()
    {
        if (_scrollViewer is not null) return;
        _scrollViewer = FindVisualDescendant<ScrollViewer>(ChatMessagesList, _ => true);
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

    private static T? FindVisualAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match) return match;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }
}
