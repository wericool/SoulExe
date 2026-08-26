using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SoulExe;

public sealed class FormattedChatTextBlock : TextBlock
{
    public static readonly DependencyProperty ContentTextProperty = DependencyProperty.Register(
        nameof(ContentText),
        typeof(string),
        typeof(FormattedChatTextBlock),
        new PropertyMetadata("", OnContentTextChanged));

    public string ContentText
    {
        get => (string)GetValue(ContentTextProperty);
        set => SetValue(ContentTextProperty, value);
    }

    private static readonly Brush ActionBrush = new SolidColorBrush(Color.FromRgb(244, 184, 96));
    private static readonly Brush QuoteBrush = new SolidColorBrush(Color.FromRgb(142, 204, 255));

    public FormattedChatTextBlock()
    {
        TextWrapping = TextWrapping.Wrap;
        LineHeight = 23;
    }

    private static void OnContentTextChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        ((FormattedChatTextBlock)source).Render(args.NewValue as string ?? "");
    }

    private void Render(string text)
    {
        Inlines.Clear();
        if (string.IsNullOrEmpty(text)) return;

        var index = 0;
        while (index < text.Length)
        {
            var marker = text[index];
            if (marker == '*')
            {
                var closing = text.IndexOf('*', index + 1);
                if (closing > index + 1)
                {
                    Inlines.Add(new Run(text[(index + 1)..closing])
                    {
                        FontStyle = FontStyles.Italic,
                        Foreground = ActionBrush
                    });
                    index = closing + 1;
                    continue;
                }
            }

            if (marker is '“' or '«' or '"')
            {
                var matching = marker == '«' ? '»' : marker == '“' ? '”' : '"';
                var closing = text.IndexOf(matching, index + 1);
                if (closing > index)
                {
                    Inlines.Add(new Run(text[index..(closing + 1)])
                    {
                        Foreground = QuoteBrush
                    });
                    index = closing + 1;
                    continue;
                }
            }

            var start = index;
            index++;
            while (index < text.Length && text[index] != '*' && text[index] != '“' && text[index] != '«' && text[index] != '"') index++;
            Inlines.Add(new Run(text[start..index]));
        }
    }
}
