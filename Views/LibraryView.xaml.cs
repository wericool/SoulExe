using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SoulExe.ViewModels;

namespace SoulExe.Views;

public partial class LibraryView : UserControl
{
    private MainViewModel? _viewModel;
    private IInputElement? _dialogOpener;
    private int _lifecycleVersion;

    public LibraryView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void LibraryView_OnLoaded(object sender, RoutedEventArgs e)
    {
        _lifecycleVersion++;
        Subscribe(DataContext as MainViewModel);
    }

    private void LibraryView_OnUnloaded(object sender, RoutedEventArgs e)
    {
        _lifecycleVersion++;
        Subscribe(null);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded) Subscribe(e.NewValue as MainViewModel);
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
        if (e.PropertyName is not (nameof(MainViewModel.IsCharacterCreationDialogOpen) or nameof(MainViewModel.IsCharacterDeleteDialogOpen) or nameof(MainViewModel.IsLibraryLoreEditorOpen) or nameof(MainViewModel.IsPersonaEditorOpen) or nameof(MainViewModel.IsPersonaDeleteDialogOpen) or nameof(MainViewModel.IsPendingDeletionDialogOpen))) return;

        if (OpenDialog() is { } dialog)
        {
            _dialogOpener ??= Keyboard.FocusedElement;
            var viewModel = _viewModel;
            var lifecycleVersion = _lifecycleVersion;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (IsLoaded && _lifecycleVersion == lifecycleVersion && ReferenceEquals(_viewModel, viewModel) && OpenDialog() == dialog) dialog.Focus();
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

    private Control? OpenDialog()
    {
        if (_viewModel?.IsPersonaDeleteDialogOpen == true) return PersonaDeleteInitialFocus;
        if (_viewModel?.IsPendingDeletionDialogOpen == true) return PendingDeletionInitialFocus;
        if (_viewModel?.IsPersonaEditorOpen == true) return PersonaEditorInitialFocus;
        if (_viewModel?.IsCharacterDeleteDialogOpen == true) return CharacterDeleteInitialFocus;
        if (_viewModel?.IsCharacterCreationDialogOpen == true) return CharacterCreationInitialFocus;
        if (_viewModel?.IsLibraryLoreEditorOpen == true) return LoreEditorInitialFocus;
        return null;
    }

    private void LibraryView_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _viewModel is null) return;
        ICommand? command = _viewModel.IsPersonaDeleteDialogOpen ? _viewModel.CancelPersonaDeleteCommand :
            _viewModel.IsPendingDeletionDialogOpen ? _viewModel.CancelPendingDeletionCommand :
            _viewModel.IsPersonaEditorOpen ? _viewModel.ClosePersonaEditorCommand :
            _viewModel.IsCharacterDeleteDialogOpen ? _viewModel.CancelCharacterDeleteCommand :
            _viewModel.IsCharacterCreationDialogOpen ? _viewModel.CloseCharacterCreationDialogCommand :
            _viewModel.IsLibraryLoreEditorOpen ? _viewModel.CloseLibraryLoreEditorCommand : null;
        if (command is null || !command.CanExecute(null)) return;
        command.Execute(null);
        e.Handled = true;
    }
}
