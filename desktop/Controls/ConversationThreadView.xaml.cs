using System.Windows;
using System.Windows.Controls;
using SoulTextWpf.ViewModels;

namespace SoulTextWpf.Controls;

public partial class ConversationThreadView : UserControl
{
    public static readonly DependencyProperty ThreadProperty = DependencyProperty.Register(
        nameof(Thread),
        typeof(ConversationThreadPresentationViewModel),
        typeof(ConversationThreadView));

    public ConversationThreadPresentationViewModel? Thread
    {
        get => (ConversationThreadPresentationViewModel?)GetValue(ThreadProperty);
        set => SetValue(ThreadProperty, value);
    }

    public ConversationThreadView() => InitializeComponent();
}
