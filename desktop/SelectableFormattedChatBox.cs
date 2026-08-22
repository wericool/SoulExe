using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SoulTextWpf.Models;

namespace SoulTextWpf;

/// <summary>Read-only selectable message text with lightweight Markdown-like rendering.</summary>
public sealed class SelectableFormattedChatBox : RichTextBox
{
    public static readonly DependencyProperty ContentTextProperty = DependencyProperty.Register(
        nameof(ContentText), typeof(string), typeof(SelectableFormattedChatBox), new PropertyMetadata("", OnContentChanged));
    public static readonly DependencyProperty AppearanceProperty = DependencyProperty.Register(
        nameof(Appearance), typeof(ChatAppearanceSettings), typeof(SelectableFormattedChatBox), new PropertyMetadata(null, OnAppearanceChanged));

    public string ContentText { get => (string)GetValue(ContentTextProperty); set => SetValue(ContentTextProperty, value); }
    public ChatAppearanceSettings? Appearance { get => (ChatAppearanceSettings?)GetValue(AppearanceProperty); set => SetValue(AppearanceProperty, value); }

    public SelectableFormattedChatBox()
    {
        IsReadOnly = true;
        IsUndoEnabled = false;
        IsDocumentEnabled = false;
        Focusable = true;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Render(ContentText);
    }

    private static void OnContentChanged(DependencyObject source, DependencyPropertyChangedEventArgs args) =>
        ((SelectableFormattedChatBox)source).Render(args.NewValue as string ?? "");

    private static void OnAppearanceChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        var box = (SelectableFormattedChatBox)source;
        if (args.OldValue is INotifyPropertyChanged oldAppearance) oldAppearance.PropertyChanged -= box.AppearanceChanged;
        if (args.NewValue is INotifyPropertyChanged newAppearance) newAppearance.PropertyChanged += box.AppearanceChanged;
        box.Render(box.ContentText);
    }

    private void AppearanceChanged(object? sender, PropertyChangedEventArgs e) => Render(ContentText);

    private void Render(string text)
    {
        var appearance = Appearance ?? new ChatAppearanceSettings();
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = MakeBrush(appearance.TextColor, "#F3F6FF"),
            FontSize = appearance.FontSize,
            FontFamily = FontFamily,
            TextAlignment = TextAlignment.Left
        };
        var paragraph = new Paragraph { Margin = new Thickness(0), Padding = new Thickness(0), LineHeight = Math.Max(20, appearance.FontSize + 7) };
        document.Blocks.Add(paragraph);
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] == '\n') { paragraph.Inlines.Add(new LineBreak()); index++; continue; }
            if (appearance.FormatBold && StartsWith(text, index, "**") && TryFind(text, "**", index + 2, out var boldEnd))
            {
                paragraph.Inlines.Add(new Run(text[(index + 2)..boldEnd]) { FontWeight = FontWeights.SemiBold });
                index = boldEnd + 2; continue;
            }
            if (appearance.FormatCode && text[index] == '`' && TryFind(text, "`", index + 1, out var codeEnd))
            {
                paragraph.Inlines.Add(new Run(text[(index + 1)..codeEnd]) { Foreground = MakeBrush(appearance.CodeColor, "#C084FC"), FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.SemiBold, Background = MakeBrush("#251E38", "#251E38") });
                index = codeEnd + 1; continue;
            }
            if (appearance.FormatActions && text[index] == '*' && TryFind(text, "*", index + 1, out var actionEnd))
            {
                paragraph.Inlines.Add(new Run(text[(index + 1)..actionEnd]) { Foreground = MakeBrush(appearance.ActionColor, "#F4B860"), FontStyle = FontStyles.Italic });
                index = actionEnd + 1; continue;
            }
            if (appearance.FormatQuotes && IsQuoteStart(text[index], out var quoteEnd) && TryFind(text, quoteEnd.ToString(), index + 1, out var quoteClose))
            {
                paragraph.Inlines.Add(new Run(text[index..(quoteClose + 1)]) { Foreground = MakeBrush(appearance.QuoteColor, "#8ECCFF"), FontWeight = FontWeights.SemiBold });
                index = quoteClose + 1; continue;
            }
            var start = index++;
            while (index < text.Length && text[index] != '\n' && text[index] != '*' && text[index] != '`' && !IsQuoteStart(text[index], out _)) index++;
            paragraph.Inlines.Add(new Run(text[start..index]));
        }
        Document = document;
    }

    private static bool StartsWith(string value, int index, string marker) => index + marker.Length <= value.Length && string.CompareOrdinal(value, index, marker, 0, marker.Length) == 0;
    private static bool TryFind(string value, string marker, int start, out int end) { end = value.IndexOf(marker, start, StringComparison.Ordinal); return end >= start; }
    private static bool IsQuoteStart(char value, out char end) { end = value switch { '«' => '»', '“' => '”', '"' => '"', _ => '\0' }; return end != '\0'; }
    private static Brush MakeBrush(string? value, string fallback)
    {
        try { return (Brush)new BrushConverter().ConvertFromString(string.IsNullOrWhiteSpace(value) ? fallback : value)!; }
        catch (FormatException) { return (Brush)new BrushConverter().ConvertFromString(fallback)!; }
    }
}
