using System.Windows.Controls;
using System.Windows.Input;
using SoulExe.ViewModels;

namespace SoulExe.Controls;

public partial class ConversationComposerView : UserControl
{
    public ConversationComposerView() => InitializeComponent();

    private void PersonalDraft_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        if (DataContext is MainViewModel viewModel && viewModel.SendCommand.CanExecute(null))
            viewModel.SendCommand.Execute(null);
    }

    private void GroupDraft_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Key.Enter is not raised for IME-processed composition keys, so composition remains with TextBox.
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.Control) return;
        if (DataContext is not MainViewModel viewModel || !viewModel.SendGroupMessageCommand.CanExecute(null)) return;

        e.Handled = true;
        viewModel.SendGroupMessageCommand.Execute(null);
    }
}
