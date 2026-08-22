using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoulTextWpf;

/// <summary>Compact hover-only help glyph used beside local-LLM settings.</summary>
public sealed class HelpHint : Border
{
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(HelpHint), new PropertyMetadata("", OnHintChanged));

    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public HelpHint()
    {
        Width = 17;
        Height = 17;
        CornerRadius = new CornerRadius(9);
        Background = new SolidColorBrush(Color.FromRgb(48, 58, 90));
        BorderBrush = new SolidColorBrush(Color.FromRgb(124, 92, 252));
        BorderThickness = new Thickness(1);
        Margin = new Thickness(7, 0, 0, 0);
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Left;
        Cursor = System.Windows.Input.Cursors.Help;
        Child = new TextBlock
        {
            Text = "?",
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 10.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static void OnHintChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not HelpHint control) return;
        control.ToolTip = new TextBlock
        {
            Text = args.NewValue as string ?? "",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360
        };
    }
}
