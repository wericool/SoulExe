using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        PageHost.Content = null;
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
        if (page == "Chat")
        {
            // The conversation owns the screen; overlays step aside.
            if (BackstageOverlay.Visibility == Visibility.Visible) CloseBackstage();
            if (!ReferenceEquals(PageHost.Content, null)) PageHost.Content = null;
            if (PageOverlay.Visibility != Visibility.Collapsed) PageOverlay.Visibility = Visibility.Collapsed;
            // The floating top bar belongs to the stage only; over pages it
            // overlapped their toolbars and stole clicks near the window top.
            TopBar.Visibility = Visibility.Visible;
            EnsureStageContent();
            return;
        }

        TopBar.Visibility = Visibility.Collapsed;
        EnsureStageContent();
        PageOverlay.Visibility = Visibility.Visible;
        if (page == "Characters")
        {
            // The character editor reads SelectedCharacter at load time, so it
            // must be rebuilt per visit instead of served from the page cache.
            PageHost.Content = new CharactersView { DataContext = _viewModel };
            return;
        }
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
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (IsLoaded && BackstageOverlay.Visibility == Visibility.Visible)
                BackstageOverlay.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
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

    private void BackstageNav_OnClick(object sender, RoutedEventArgs e) => CloseBackstage();

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
        CloseBackstage();
        if (card.IsAddCharacter)
        {
            // Creation dialog lives in the Library; route there.
            if (_viewModel.NavigateCommand.CanExecute("Home")) _viewModel.NavigateCommand.Execute("Home");
            return;
        }
        if (_viewModel.OpenCharacterEditorCommand.CanExecute(card.Character)) _viewModel.OpenCharacterEditorCommand.Execute(card.Character);
    }

    private void Presence_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Phase D will open the character sheet; for now jump to the character route.
        if (_viewModel?.NavigateCommand.CanExecute("Characters") == true) _viewModel.NavigateCommand.Execute("Characters");
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
