using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SoulExe.Controls;
using SoulExe.ViewModels;

namespace SoulExe.Views;

public partial class ChatWorkspaceView : UserControl
{
    public static readonly RoutedUICommand ShowConversationListCommand = new("Показать диалоги", nameof(ShowConversationListCommand), typeof(ChatWorkspaceView));
    public static readonly RoutedUICommand ShowConversationDetailsCommand = new("Показать информацию", nameof(ShowConversationDetailsCommand), typeof(ChatWorkspaceView));

    private MainViewModel? _viewModel;
    private readonly HashSet<SceneMessageViewModel> _subscribedSceneMessages = [];
    private bool _chatScrollQueued;
    private bool _sceneScrollQueued;
    private bool _isSubscribed;
    private int _lifecycleVersion;
    private WorkspaceLayout _layout;
    private Drawer _openDrawer;
    private IInputElement? _drawerOpener;
    private IInputElement? _dialogOpener;

    public ChatWorkspaceView()
    {
        InitializeComponent();
        CommandBindings.Add(new CommandBinding(ShowConversationListCommand, (_, _) => OpenDrawer(Drawer.List), (_, e) => e.CanExecute = _layout == WorkspaceLayout.Narrow));
        CommandBindings.Add(new CommandBinding(ShowConversationDetailsCommand, (_, _) => OpenDrawer(Drawer.Details), (_, e) => e.CanExecute = _layout != WorkspaceLayout.Wide));
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Unsubscribe(_viewModel);
        _viewModel = e.NewValue as MainViewModel;
        if (!IsLoaded) return;
        Subscribe(_viewModel);
        ScheduleChatScroll();
        ScheduleSceneScroll();
    }

    private void ChatWorkspaceView_OnLoaded(object sender, RoutedEventArgs e)
    {
        _lifecycleVersion++;
        Subscribe(_viewModel ?? DataContext as MainViewModel);
        UpdateWorkspaceLayout();
        ScheduleChatScroll();
        ScheduleSceneScroll();
    }

    private void ChatWorkspaceView_OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateWorkspaceLayout();

    private void UpdateWorkspaceLayout()
    {
        var layout = ActualWidth >= 1060 ? WorkspaceLayout.Wide : ActualWidth >= 700 ? WorkspaceLayout.Medium : WorkspaceLayout.Narrow;
        if (_layout == layout && IsLoaded) return;

        CloseDrawer(false);
        _layout = layout;
        var isWide = layout == WorkspaceLayout.Wide;
        var isNarrow = layout == WorkspaceLayout.Narrow;

        WideListHost.Visibility = isNarrow ? Visibility.Collapsed : Visibility.Visible;
        ListSplitter.Visibility = isNarrow ? Visibility.Collapsed : Visibility.Visible;
        WideDetailsHost.Visibility = isWide ? Visibility.Visible : Visibility.Collapsed;
        DetailsSplitter.Visibility = isWide ? Visibility.Visible : Visibility.Collapsed;
        ListColumn.Width = isNarrow ? new GridLength(0) : new GridLength(300);
        ListSplitterColumn.Width = isNarrow ? new GridLength(0) : new GridLength(14);
        DetailsSplitterColumn.Width = isWide ? new GridLength(14) : new GridLength(0);
        DetailsColumn.Width = isWide ? new GridLength(300) : new GridLength(0);
        ThreadColumn.MinWidth = isWide ? 430 : 0;
        MoveToHost(ConversationList, isNarrow ? ListDrawerHost : WideListHost);
        MoveToHost(ConversationDetails, isWide ? WideDetailsHost : DetailsDrawerHost);

        ListDrawer.Width = Math.Min(340, Math.Max(280, ActualWidth - 56));
        DetailsDrawer.Width = Math.Min(390, Math.Max(300, ActualWidth - 40));
        CommandManager.InvalidateRequerySuggested();
    }

    private static void MoveToHost(FrameworkElement content, ContentControl host)
    {
        if (ReferenceEquals(content.Parent, host)) return;
        if (content.Parent is ContentControl parent) parent.Content = null;
        host.Content = content;
    }

    private void OpenDrawer(Drawer drawer)
    {
        if ((drawer == Drawer.List && _layout != WorkspaceLayout.Narrow) ||
            (drawer == Drawer.Details && _layout == WorkspaceLayout.Wide)) return;

        CloseDrawer(false);
        _drawerOpener = Keyboard.FocusedElement;
        _openDrawer = drawer;
        DrawerScrim.Visibility = Visibility.Visible;
        (drawer == Drawer.List ? ListDrawer : DetailsDrawer).Visibility = Visibility.Visible;
        var lifecycleVersion = _lifecycleVersion;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!IsLoaded || _lifecycleVersion != lifecycleVersion || _openDrawer != drawer) return;
            FrameworkElement content = drawer == Drawer.List ? ConversationList : ConversationDetails;
            content.Focus();
        }));
    }

    private void CloseDrawer(bool restoreFocus = true)
    {
        if (_openDrawer == Drawer.None) return;
        ListDrawer.Visibility = Visibility.Collapsed;
        DetailsDrawer.Visibility = Visibility.Collapsed;
        DrawerScrim.Visibility = Visibility.Collapsed;
        _openDrawer = Drawer.None;
        if (restoreFocus && _drawerOpener is not null) _drawerOpener.Focus();
        _drawerOpener = null;
    }

    private void DrawerScrim_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CloseDrawer();
        e.Handled = true;
    }

    private void ChatWorkspaceView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_openDrawer != Drawer.None)
        {
            CloseDrawer();
            e.Handled = true;
            return;
        }

        var command = OpenDialogCommand();
        if (command is null || !command.CanExecute(null)) return;
        command.Execute(null);
        e.Handled = true;
    }

    private void Subscribe(MainViewModel? viewModel)
    {
        if (viewModel is null || _isSubscribed) return;
        _viewModel = viewModel;
        _isSubscribed = true;
        viewModel.Messages.CollectionChanged += ChatMessages_OnCollectionChanged;
        viewModel.SceneMessages.CollectionChanged += SceneMessages_OnCollectionChanged;
        viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        foreach (var message in viewModel.SceneMessages)
            if (_subscribedSceneMessages.Add(message)) message.PropertyChanged += SceneMessage_OnPropertyChanged;
    }

    private void Unsubscribe(MainViewModel? viewModel)
    {
        if (viewModel is null || !_isSubscribed) return;
        viewModel.Messages.CollectionChanged -= ChatMessages_OnCollectionChanged;
        viewModel.SceneMessages.CollectionChanged -= SceneMessages_OnCollectionChanged;
        viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        foreach (var message in _subscribedSceneMessages) message.PropertyChanged -= SceneMessage_OnPropertyChanged;
        _subscribedSceneMessages.Clear();
        _isSubscribed = false;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CloseDrawer(false);
        _dialogOpener = null;
        _lifecycleVersion++;
        Unsubscribe(_viewModel);
    }

    private void ChatMessages_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleChatScroll();

    private void SceneMessages_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var item in _subscribedSceneMessages) item.PropertyChanged -= SceneMessage_OnPropertyChanged;
            _subscribedSceneMessages.Clear();
            ScheduleSceneScroll();
            return;
        }
        if (e.NewItems is not null)
            foreach (var item in e.NewItems.OfType<SceneMessageViewModel>())
                if (_subscribedSceneMessages.Add(item)) item.PropertyChanged += SceneMessage_OnPropertyChanged;
        if (e.OldItems is not null)
            foreach (var item in e.OldItems.OfType<SceneMessageViewModel>())
                if (_subscribedSceneMessages.Remove(item)) item.PropertyChanged -= SceneMessage_OnPropertyChanged;
        ScheduleSceneScroll();
    }

    private void SceneMessage_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SceneMessageViewModel.Content)) ScheduleSceneScroll();
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsAssistantTyping)) ScheduleChatScroll();
        if (e.PropertyName == nameof(MainViewModel.IsSceneTyping)) ScheduleSceneScroll();
        if (e.PropertyName == nameof(MainViewModel.SelectedChatMessageSearchResult)) ScheduleSearchResultScroll();
        if (e.PropertyName == nameof(MainViewModel.SelectedSceneMessageSearchResult)) ScheduleSceneSearchResultScroll();
        if (e.PropertyName is nameof(MainViewModel.IsNewChatCharacterPickerOpen) or nameof(MainViewModel.IsRenameChatDialogOpen) or nameof(MainViewModel.IsRenameSceneDialogOpen) or nameof(MainViewModel.IsPendingDeletionDialogOpen))
        {
            if ((e.PropertyName == nameof(MainViewModel.IsNewChatCharacterPickerOpen) && _viewModel?.IsNewChatCharacterPickerOpen == true) ||
                (e.PropertyName == nameof(MainViewModel.IsRenameChatDialogOpen) && _viewModel?.IsRenameChatDialogOpen == true) ||
                (e.PropertyName == nameof(MainViewModel.IsRenameSceneDialogOpen) && _viewModel?.IsRenameSceneDialogOpen == true) ||
                (e.PropertyName == nameof(MainViewModel.IsPendingDeletionDialogOpen) && _viewModel?.IsPendingDeletionDialogOpen == true)) CloseDrawer();

            if (OpenDialogFocus() is { } focus)
            {
                _dialogOpener ??= Keyboard.FocusedElement;
                var viewModel = _viewModel;
                var lifecycleVersion = _lifecycleVersion;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (IsLoaded && _lifecycleVersion == lifecycleVersion && ReferenceEquals(_viewModel, viewModel) && OpenDialogFocus() == focus) focus.Focus();
                }));
            }
            else if (_dialogOpener is not null)
            {
                var opener = _dialogOpener;
                _dialogOpener = null;
                var viewModel = _viewModel;
                var lifecycleVersion = _lifecycleVersion;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (IsLoaded && _lifecycleVersion == lifecycleVersion && ReferenceEquals(_viewModel, viewModel)) opener.Focus();
                }));
            }
        }
    }

    private Control? OpenDialogFocus()
    {
        if (_viewModel?.IsRenameSceneDialogOpen == true) return RenameSceneInitialFocus;
        if (_viewModel?.IsPendingDeletionDialogOpen == true) return PendingDeletionInitialFocus;
        if (_viewModel?.IsRenameChatDialogOpen == true) return RenameChatInitialFocus;
        if (_viewModel?.IsNewChatCharacterPickerOpen == true) return NewChatInitialFocus;
        return null;
    }

    private ICommand? OpenDialogCommand() => _viewModel?.IsRenameSceneDialogOpen == true ? _viewModel.CancelRenameSceneCommand :
        _viewModel?.IsPendingDeletionDialogOpen == true ? _viewModel.CancelPendingDeletionCommand :
        _viewModel?.IsRenameChatDialogOpen == true ? _viewModel.CancelRenameChatDialogCommand :
        _viewModel?.IsNewChatCharacterPickerOpen == true ? _viewModel.CancelNewChatCharacterPickerCommand : null;

    private void ScheduleChatScroll()
    {
        if (!IsLoaded || !_isSubscribed || _chatScrollQueued) return;
        _chatScrollQueued = true;
        var viewModel = _viewModel;
        var lifecycleVersion = _lifecycleVersion;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _chatScrollQueued = false;
            if (!IsLoaded || _lifecycleVersion != lifecycleVersion || !_isSubscribed || !ReferenceEquals(_viewModel, viewModel)) return;
            ConversationThread?.ScrollPersonalToEnd();
        }));
    }

    private void ScheduleSceneScroll()
    {
        if (!IsLoaded || !_isSubscribed || _sceneScrollQueued) return;
        _sceneScrollQueued = true;
        var viewModel = _viewModel;
        var lifecycleVersion = _lifecycleVersion;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _sceneScrollQueued = false;
            if (!IsLoaded || _lifecycleVersion != lifecycleVersion || !_isSubscribed || !ReferenceEquals(_viewModel, viewModel)) return;
            ConversationThread?.ScrollSceneToEnd();
        }));
    }

    private void ScheduleSearchResultScroll()
    {
        var viewModel = _viewModel;
        var lifecycleVersion = _lifecycleVersion;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!IsLoaded || _lifecycleVersion != lifecycleVersion || !_isSubscribed || !ReferenceEquals(_viewModel, viewModel)) return;
            var result = viewModel?.SelectedChatMessageSearchResult;
            if (result is not null) ConversationThread?.ScrollToPersonalMessage(result.MessageId);
        }));
    }

    private void ScheduleSceneSearchResultScroll()
    {
        var viewModel = _viewModel;
        var lifecycleVersion = _lifecycleVersion;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!IsLoaded || _lifecycleVersion != lifecycleVersion || !_isSubscribed || !ReferenceEquals(_viewModel, viewModel)) return;
            var result = viewModel?.SelectedSceneMessageSearchResult;
            if (result is not null) ConversationThread?.ScrollToSceneMessage(result.MessageId);
        }));
    }

    private enum WorkspaceLayout { Wide, Medium, Narrow }
    private enum Drawer { None, List, Details }
}
