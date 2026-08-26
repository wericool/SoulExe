using System.Windows;
using System.Windows.Controls;

namespace SoulExe.Views;

public partial class SettingsView : UserControl
{
    private const double CompactAppearanceWidth = 900;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SizeChanged += OnSizeChanged;
        UpdateAppearanceLayout();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => SizeChanged -= OnSizeChanged;

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateAppearanceLayout();

    private void UpdateAppearanceLayout()
    {
        var compact = ActualWidth < CompactAppearanceWidth;
        AppearanceLayout.ColumnDefinitions.Clear();
        AppearanceLayout.RowDefinitions.Clear();

        if (compact)
        {
            AppearanceLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            AppearanceLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(AppearanceEditor, 0);
            Grid.SetColumn(AppearanceEditor, 0);
            AppearanceEditor.Padding = new Thickness(0);
            Grid.SetRow(AppearancePreview, 1);
            Grid.SetColumn(AppearancePreview, 0);
            AppearancePreview.MinWidth = 0;
            AppearancePreview.MinHeight = 520;
            AppearancePreview.Margin = new Thickness(0, 16, 0, 0);
        }
        else
        {
            AppearanceLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AppearanceLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            AppearanceLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(AppearanceEditor, 0);
            Grid.SetColumn(AppearanceEditor, 0);
            AppearanceEditor.Padding = new Thickness(0, 0, 8, 0);
            Grid.SetRow(AppearancePreview, 0);
            Grid.SetColumn(AppearancePreview, 2);
            AppearancePreview.MinWidth = 380;
            AppearancePreview.MinHeight = 0;
            AppearancePreview.Margin = new Thickness();
        }
    }
}
