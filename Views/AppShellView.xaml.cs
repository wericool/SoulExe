using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SoulExe.ViewModels;

namespace SoulExe.Views;

public partial class AppShellView : UserControl
{
    // The application keeps a desktop-sized minimum window. Hiding its primary
    // navigation at ordinary desktop widths makes the shell look empty.
    private MainViewModel? _viewModel;
    private IInputElement? _setupOpener;
    private readonly Dictionary<string, UserControl> _pageCache = [];

    public AppShellView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void AppShellView_OnLoaded(object sender, RoutedEventArgs e)
    {
        Subscribe(DataContext as MainViewModel);
        UpdatePageHost();
        FocusInitialSetupIfVisible();
    }

    private void AppShellView_OnUnloaded(object sender, RoutedEventArgs e)
    {
        PageHost.Content = null;
        Subscribe(null);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        Subscribe(e.NewValue as MainViewModel);
        UpdatePageHost();
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
            UpdatePageHost();
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

    private void UpdatePageHost()
    {
        if (!IsLoaded || _viewModel is null) return;

        var page = _viewModel.CurrentPage;
        // Character editing depends on a freshly reloaded selected character.
        // Do not reuse an old detached editor instance for this route.
        if (page == "Characters")
        {
            var editor = new CharactersView { DataContext = _viewModel };
            PageHost.Content = editor;
            return;
        }
        if (!_pageCache.TryGetValue(page, out var view))
        {
            view = page switch
            {
                "Home" => new LibraryView(),
                "Chat" => new ChatWorkspaceView(),
                "Gateway" => new GatewayView(),
                "Setup" => new SetupView(),
                "Models" => new ModelsView(),
                "Options" => new SettingsView(),
                "Characters" => new CharactersView(),
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

        // Cached pages may be detached while another page is active. Set the
        // current window VM explicitly before attaching a view again.
        if (!ReferenceEquals(view.DataContext, _viewModel)) view.DataContext = _viewModel;
        if (!ReferenceEquals(PageHost.Content, view)) PageHost.Content = view;
    }

    private void AppShellView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_viewModel?.IsInitialSetupVisible == true)
        {
            if (!_viewModel.SkipInitialSetupCommand.CanExecute(null)) return;
            _viewModel.SkipInitialSetupCommand.Execute(null);
            e.Handled = true;
            return;
        }

    }
}
