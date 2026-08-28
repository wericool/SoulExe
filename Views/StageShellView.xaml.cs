using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SoulExe.ViewModels;

namespace SoulExe.Views;

/// <summary>Immersive "theater" shell. The conversation is the stage; every other
/// route renders as a full-page overlay above it, and Backstage is the only hub.
/// Page caching semantics mirror the previous AppShellView.</summary>
public partial class StageShellView : UserControl
{
    private MainViewModel? _viewModel;
    private IInputElement? _setupOpener;
    private IInputElement? _backstageOpener;
    private readonly Dictionary<string, UserControl> _pageCache = [];
    private static readonly CubicEase SheetEase = new() { EasingMode = EasingMode.EaseOut };
    private static readonly Duration SheetInDuration = new(TimeSpan.FromMilliseconds(200));
    private static readonly Duration SheetOutDuration = new(TimeSpan.FromMilliseconds(150));
    private int _pageSheetVersion;

    public StageShellView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void StageShellView_OnLoaded(object sender, RoutedEventArgs e)
    {
        Subscribe(DataContext as MainViewModel);
        UpdateStage();
        FocusInitialSetupIfVisible();
    }

    private void StageShellView_OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Hard reset: fade-out callbacks never run while detached, so the next
        // attach must start from a clean, fully collapsed sheet.
        _pageSheetVersion++;
        CollapsePageSheet();
        StageHost.Content = null;
        Subscribe(null);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        Subscribe(e.NewValue as MainViewModel);
        UpdateStage();
    }

    private void Subscribe(MainViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel)) return;
        if (_viewModel is not null) _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        _viewModel = viewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage))
        {
            UpdateStage();
            return;
        }

        if (e.PropertyName != nameof(MainViewModel.IsInitialSetupVisible)) return;
        var viewModel = _viewModel;
        if (viewModel?.IsInitialSetupVisible == true)
        {
            _setupOpener = Keyboard.FocusedElement;
            FocusInitialSetupIfVisible();
        }
        else if (_setupOpener is not null)
        {
            var opener = _setupOpener;
            _setupOpener = null;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (IsLoaded && ReferenceEquals(_viewModel, viewModel)) opener.Focus();
            }));
        }
    }

    private void UpdateStage()
    {
        if (!IsLoaded || _viewModel is null) return;

        var page = _viewModel.CurrentPage;
        EnsureStageContent();
        if (page == "Chat")
        {
            // The conversation owns the screen; the sheet slides away and reveals it.
            if (BackstageOverlay.Visibility == Visibility.Visible) CloseBackstage();
            ClosePageSheet();
            TopBar.Visibility = Visibility.Visible;
            return;
        }

        TopBar.Visibility = Visibility.Collapsed;
        OpenPageSheet(page);
    }

    private void OpenPageSheet(string page)
    {
        if (page == "Characters")
        {
            // The character editor reads SelectedCharacter at load time, so it
            // must be rebuilt per visit instead of served from the page cache.
            PageHost.Content = new CharactersView { DataContext = _viewModel };
        }
        else
        {
            if (!_pageCache.TryGetValue(page, out var view))
            {
                view = page switch
                {
                    "Home" => new LibraryView(),
                    "Gateway" => new GatewayView(),
                    "Setup" => new SetupView(),
                    "Models" => new ModelsView(),
                    "Options" => new SettingsView(),
                    _ => null
                };
                if (view is null)
                {
                    PageHost.Content = null;
                    return;
                }
                view.DataContext = _viewModel;
                _pageCache.Add(page, view);
            }
            if (!ReferenceEquals(view.DataContext, _viewModel)) view.DataContext = _viewModel;
            if (!ReferenceEquals(PageHost.Content, view)) PageHost.Content = view;
        }

        _pageSheetVersion++;
        var reopening = PageSheet.Visibility == Visibility.Visible;
        PageScrim.Visibility = Visibility.Visible;
        PageSheet.Visibility = Visibility.Visible;
        AnimateFade(PageScrim, reopening ? PageScrim.Opacity : 0, 1, SheetInDuration);
        PlaySheetIn(PageSheetFrame);
    }

    private void ClosePageSheet()
    {
        if (PageSheet.Visibility != Visibility.Visible && PageScrim.Visibility != Visibility.Visible) return;
        _pageSheetVersion++;
        var version = _pageSheetVersion;
        var translate = PageSheetFrame.RenderTransform as TranslateTransform ?? new TranslateTransform();
        PageSheetFrame.RenderTransform = translate;
        var slide = new DoubleAnimation(translate.Y, 26, SheetOutDuration) { EasingFunction = SheetEase };
        var fade = new DoubleAnimation(PageSheetFrame.Opacity, 0, SheetOutDuration) { EasingFunction = SheetEase };
        fade.Completed += (_, _) =>
        {
            // A sheet opened meanwhile replaced these animations; its open path owns collapse now.
            if (_pageSheetVersion != version) return;
            PageSheetFrame.BeginAnimation(OpacityProperty, null);
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            CollapsePageSheet();
        };
        PageSheetFrame.BeginAnimation(OpacityProperty, fade);
        translate.BeginAnimation(TranslateTransform.YProperty, slide);
        AnimateFade(PageScrim, PageScrim.Opacity, 0, SheetOutDuration);
    }

    private void CollapsePageSheet()
    {
        PageScrim.Visibility = Visibility.Collapsed;
        PageSheet.Visibility = Visibility.Collapsed;
        PageSheetFrame.Opacity = 1;
        // Detach the page only once fully hidden; clearing earlier would flash
        // an empty frame during the fade-out.
        PageHost.Content = null;
    }

    private static void PlaySheetIn(Border frame)
    {
        var translate = new TranslateTransform(0, 26);
        frame.RenderTransform = translate;
        frame.BeginAnimation(OpacityProperty, new DoubleAnimation(frame.Opacity, 1, SheetInDuration) { EasingFunction = SheetEase });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(26, 0, SheetInDuration) { EasingFunction = SheetEase });
    }

    private static void AnimateFade(UIElement element, double from, double to, Duration duration)
    {
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(from, to, duration) { EasingFunction = SheetEase });
    }

    private void EnsureStageContent()
    {
        if (!_pageCache.TryGetValue("Chat", out var chat))
        {
            chat = new ChatWorkspaceView { DataContext = _viewModel };
            _pageCache.Add("Chat", chat);
        }
        else if (!ReferenceEquals(chat.DataContext, _viewModel))
        {
            chat.DataContext = _viewModel;
        }
        if (!ReferenceEquals(StageHost.Content, chat)) StageHost.Content = chat;
    }

    private void OpenBackstage()
    {
        if (_viewModel?.IsInitialSetupVisible == true) return;
        _backstageOpener = Keyboard.FocusedElement;
        BackstageOverlay.Visibility = Visibility.Visible;
        FadeIn(BackstageOverlay);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (IsLoaded && BackstageOverlay.Visibility == Visibility.Visible)
                BackstageCloseButton.Focus();
        }));
    }

    private void CloseBackstage()
    {
        if (BackstageOverlay.Visibility != Visibility.Visible) return;
        BackstageOverlay.Visibility = Visibility.Collapsed;
        if (_backstageOpener is not null)
        {
            var opener = _backstageOpener;
            _backstageOpener = null;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (IsLoaded) opener.Focus();
            }));
        }
    }

    private void ModelChip_OnClick(object sender, RoutedEventArgs e)
    {
        ModelChipPopup.IsOpen = !ModelChipPopup.IsOpen;
    }

    private void ModelChipNav_OnClick(object sender, RoutedEventArgs e)
    {
        ModelChipPopup.IsOpen = false;
    }

    private void BackstageButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (BackstageOverlay.Visibility == Visibility.Visible) CloseBackstage();
        else OpenBackstage();
    }

    // WindowChrome.CaptionHeight is 1 so page toolbars stay clickable; these handlers
    // restore window dragging from the shell's own empty surfaces instead.
    private void TopBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only the empty band of the top bar drags; its buttons and pill keep their clicks.
        if (!ReferenceEquals(e.OriginalSource, TopBar)) return;
        var window = Window.GetWindow(this);
        if (window is null) return;
        if (e.ClickCount == 2)
        {
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else if (window.WindowState != WindowState.Maximized)
        {
            window.DragMove();
        }
        e.Handled = true;
    }

    private void ChromeArea_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Page overlay frame and backstage scrim drag the window; page content does not.
        if (sender is not Grid grid || !ReferenceEquals(e.OriginalSource, grid)) return;
        Window.GetWindow(this)?.DragMove();
        e.Handled = true;
    }

    private void MinimizeWindowButton_OnClick(object sender, RoutedEventArgs e) => Window.GetWindow(this)!.WindowState = WindowState.Minimized;

    private void ToggleMaximizeWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this)!;
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseWindowButton_OnClick(object sender, RoutedEventArgs e) => Window.GetWindow(this)!.Close();

    private static void FadeIn(UIElement element)
    {
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        element.BeginAnimation(OpacityProperty, animation);
    }

    private void BackstageNav_OnClick(object sender, RoutedEventArgs e) => CloseBackstage();

    private void BackstageWorld_OnClick(object sender, RoutedEventArgs e)
    {
        // Button raises Click before executing its Command, so the library tab
        // is already selected when the navigation renders the sheet.
        if (sender is FrameworkElement { Tag: string tab } && !string.IsNullOrEmpty(tab)
            && _viewModel?.SelectLibraryTabCommand.CanExecute(tab) == true)
        {
            _viewModel.SelectLibraryTabCommand.Execute(tab);
        }
        CloseBackstage();
    }

    private void BackstageOptions_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tab } && !string.IsNullOrEmpty(tab)
            && _viewModel?.SelectOptionsTabCommand.CanExecute(tab) == true)
        {
            _viewModel.SelectOptionsTabCommand.Execute(tab);
        }
        CloseBackstage();
    }

    private void CreateCharacterFromBackstage_OnClick(object sender, RoutedEventArgs e) => OpenCharacterCreationFromBackstage();

    private void OpenCharacterCreationFromBackstage()
    {
        if (_viewModel is null) return;
        if (_viewModel.SelectLibraryTabCommand.CanExecute("characters")) _viewModel.SelectLibraryTabCommand.Execute("characters");
        if (_viewModel.NavigateCommand.CanExecute("Home")) _viewModel.NavigateCommand.Execute("Home");
        CloseBackstage();

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (_viewModel?.OpenCharacterCreationDialogCommand.CanExecute(null) == true)
                _viewModel.OpenCharacterCreationDialogCommand.Execute(null);
        }));
    }

    private void BackstageConversation_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || _viewModel is null) return;
        if (element.DataContext is not ConversationListItemViewModel item) return;
        CloseBackstage();
        // The setter opens the conversation and routes to the stage itself.
        _viewModel.SelectedConversationItem = item;
    }

    private void BackstageCharacter_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || _viewModel is null) return;
        if (button.DataContext is not HomeCharacterCardViewModel card) return;
        if (card.IsAddCharacter)
        {
            OpenCharacterCreationFromBackstage();
            return;
        }
        CloseBackstage();
        if (_viewModel.OpenCharacterChatCommand.CanExecute(card.Character)) _viewModel.OpenCharacterChatCommand.Execute(card.Character);
    }

    private void Presence_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Phase D will open the character sheet; for now jump to the character route.
        if (_viewModel?.NavigateCommand.CanExecute("Characters") == true) _viewModel.NavigateCommand.Execute("Characters");
        e.Handled = true;
    }

    private void StageShellView_OnKeyDown(object sender, KeyEventArgs e)
    {
        // Bubbling, on purpose: nested views close their own dialogs in
        // PreviewKeyDown first; reaching here means Escape is free to leave the sheet.
        if (e.Key != Key.Escape || PageSheet.Visibility != Visibility.Visible) return;
        if (_viewModel?.NavigateCommand.CanExecute("Chat") != true) return;
        _viewModel.NavigateCommand.Execute("Chat");
        e.Handled = true;
    }

    private void StageShellView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (ModelChipPopup.IsOpen)
        {
            ModelChipPopup.IsOpen = false;
            e.Handled = true;
            return;
        }
        if (_viewModel?.IsInitialSetupVisible == true)
        {
            if (!_viewModel.SkipInitialSetupCommand.CanExecute(null)) return;
            _viewModel.SkipInitialSetupCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (BackstageOverlay.Visibility == Visibility.Visible)
        {
            CloseBackstage();
            e.Handled = true;
        }
    }

    private void FocusInitialSetupIfVisible()
    {
        var viewModel = _viewModel;
        if (viewModel?.IsInitialSetupVisible != true) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (IsLoaded && ReferenceEquals(_viewModel, viewModel) && viewModel.IsInitialSetupVisible)
                InitialSetupView.FocusInitialControl();
        }));
    }
}
