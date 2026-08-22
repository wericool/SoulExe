using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SoulTextWpf.Services;
using SoulTextWpf.ViewModels;

namespace SoulTextWpf;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        ApplyExecutableIconSafely();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void ApplyExecutableIconSafely()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/SoulExeWindowIcon.png", UriKind.Absolute));
            if (resource?.Stream is null) return;

            using (resource.Stream)
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = resource.Stream;
                bitmap.EndInit();
                bitmap.Freeze();
                Icon = bitmap;
            }
        }
        catch (Exception exception)
        {
            AppLog.Write("Не удалось назначить иконку окна SoulExe", exception);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel = await MainViewModel.CreateAsync();
            DataContext = _viewModel;
            _viewModel.Messages.CollectionChanged += ChatMessages_OnCollectionChanged;
            _viewModel.SceneMessages.CollectionChanged += SceneMessages_OnCollectionChanged;
            _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
            ScheduleChatScroll();
            ScheduleSceneScroll();
        }
        catch (Exception exception)
        {
            AppLog.Write("Failed to initialise the main window.", exception);
            MessageBox.Show(
                $"Не удалось загрузить локальные данные SoulExe.\n\n{exception.Message}\n\nЖурнал: {AppLog.LogFilePath}",
                "SoulExe — ошибка запуска",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Messages.CollectionChanged -= ChatMessages_OnCollectionChanged;
            _viewModel.SceneMessages.CollectionChanged -= SceneMessages_OnCollectionChanged;
            _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            await _viewModel.DisposeAsync();
            _viewModel = null;
        }
    }

    private void ChatDraftBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        // PreviewKeyDown runs before TextBox processes AcceptsReturn, so plain Enter cannot insert a line break.
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        if (DataContext is MainViewModel viewModel && viewModel.SendCommand.CanExecute(null))
            viewModel.SendCommand.Execute(null);
    }

    // Сохранённый «нормальный» размер: ручная установка Width/Height в Maximized
    // затирает RestoreBounds, и после «свернуть из полноэкранного» окно остаётся огромным.
    private Rect _normalBounds = Rect.Empty;

    private void MinimizeWindowButton_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void ToggleMaximizeWindowButton_OnClick(object sender, RoutedEventArgs e) => ToggleWindowState();

    private void CloseWindowButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void ToggleWindowState()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            RestoreNormalBounds();
            return;
        }

        CaptureNormalBounds();
        WindowState = WindowState.Maximized;
    }

    private void CaptureNormalBounds()
    {
        // Пока окно в Normal — запоминаем актуальные границы (и для системного maximize).
        if (WindowState != WindowState.Normal) return;
        if (double.IsNaN(Left) || double.IsNaN(Top) || Width <= 0 || Height <= 0) return;
        _normalBounds = new Rect(Left, Top, Width, Height);
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (WindowState == WindowState.Normal) CaptureNormalBounds();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (WindowState == WindowState.Normal) CaptureNormalBounds();
    }

    private void RestoreNormalBounds()
    {
        ClearValue(MaxWidthProperty);
        ClearValue(MaxHeightProperty);
        if (_normalBounds.IsEmpty) return;

        // Принудительно возвращаем размер до развёртывания.
        Left = _normalBounds.X;
        Top = _normalBounds.Y;
        Width = Math.Max(MinWidth, _normalBounds.Width);
        Height = Math.Max(MinHeight, _normalBounds.Height);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Maximized)
        {
            // На всякий случай, если развернули системно (не только нашей кнопкой).
            if (_normalBounds.IsEmpty && RestoreBounds.Width > 0 && RestoreBounds.Height > 0)
                _normalBounds = RestoreBounds;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ApplyMaximizedWorkArea));
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            ClearValue(MaxWidthProperty);
            ClearValue(MaxHeightProperty);
            // После Maximized Width/Height часто остаются равными экрану — возвращаем сохранённые.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RestoreNormalBounds));
        }
    }


    private void ApplyMaximizedWorkArea()
    {
        if (WindowState != WindowState.Maximized) return;

        var workArea = SystemParameters.WorkArea;
        // Ограничиваем максимум рабочей областью (учёт панели задач),
        // но не меняем Left/Top/Width/Height: они нужны WPF для RestoreBounds.
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;
    }

    private void WrapDraftInAsterisksButton_OnClick(object sender, RoutedEventArgs e)
    {
        var start = ChatDraftBox.SelectionStart;
        var length = ChatDraftBox.SelectionLength;
        if (length > 0)
        {
            var selected = ChatDraftBox.SelectedText;
            ChatDraftBox.SelectedText = $"*{selected}*";
            ChatDraftBox.Select(start, selected.Length + 2);
        }
        else
        {
            ChatDraftBox.SelectedText = "**";
            ChatDraftBox.Select(start + 1, 0);
        }
        ChatDraftBox.Focus();
        e.Handled = true;
    }

    private void ChatMessages_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleChatScroll();

    private void SceneMessages_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (var item in e.NewItems.OfType<SceneMessageViewModel>()) item.PropertyChanged += SceneMessage_OnPropertyChanged;
        if (e.OldItems is not null)
            foreach (var item in e.OldItems.OfType<SceneMessageViewModel>()) item.PropertyChanged -= SceneMessage_OnPropertyChanged;
        ScheduleSceneScroll();
    }

    private void SceneMessage_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SceneMessageViewModel.Content)) ScheduleSceneScroll();
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsAssistantTyping))
            ScheduleChatScroll();
        if (e.PropertyName == nameof(MainViewModel.IsSceneTyping))
            ScheduleSceneScroll();
        if (e.PropertyName == nameof(MainViewModel.SelectedChatMessageSearchResult))
            ScheduleSearchResultScroll();
        if (e.PropertyName == nameof(MainViewModel.SelectedSceneMessageSearchResult))
            ScheduleSceneSearchResultScroll();
    }

    private void ScheduleChatScroll()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => ChatMessagesScroll?.ScrollToEnd()));
    }

    private void ScheduleSceneScroll()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => SceneMessagesScroll?.ScrollToEnd()));
    }

    private void ScheduleSearchResultScroll()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var result = _viewModel?.SelectedChatMessageSearchResult;
            if (result is null || ChatMessagesScroll is null) return;
            var target = FindVisualDescendant<FrameworkElement>(ChatMessagesScroll,
                element => element.DataContext is ChatMessageViewModel message && message.MessageId == result.MessageId);
            if (target is null) return;
            var position = target.TransformToAncestor(ChatMessagesScroll).Transform(new Point(0, 0));
            ChatMessagesScroll.ScrollToVerticalOffset(Math.Max(0, ChatMessagesScroll.VerticalOffset + position.Y - 34));
        }));
    }

    private void ScheduleSceneSearchResultScroll()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var result = _viewModel?.SelectedSceneMessageSearchResult;
            if (result is null || SceneMessagesScroll is null) return;
            var target = FindVisualDescendant<FrameworkElement>(SceneMessagesScroll,
                element => element.DataContext is SceneMessageViewModel message && message.Id == result.MessageId);
            if (target is null) return;
            var position = target.TransformToAncestor(SceneMessagesScroll).Transform(new Point(0, 0));
            SceneMessagesScroll.ScrollToVerticalOffset(Math.Max(0, SceneMessagesScroll.VerticalOffset + position.Y - 34));
        }));
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

    private void MessageMenuButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void ChatListItemMenuButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
        e.Handled = true;
    }
}
