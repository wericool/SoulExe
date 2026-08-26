using System.Windows;
using System.Windows.Controls;

namespace SoulExe.Views;

public partial class GatewayView : UserControl
{
    private const double CompactWidth = 900;

    public GatewayView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SizeChanged += OnSizeChanged;
        UpdateLayoutMode();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => SizeChanged -= OnSizeChanged;

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutMode();

    private void UpdateLayoutMode()
    {
        var compact = ActualWidth < CompactWidth;
        GatewayContentGrid.ColumnDefinitions.Clear();
        GatewayContentGrid.RowDefinitions.Clear();

        if (compact)
        {
            GatewayContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
            GatewayContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            SetCompactPosition(GatewayCatalogPanel, 0, new Thickness(0, 0, 0, 12));
            SetCompactPosition(GatewayDetailsPanel, 1, new Thickness());
            SetCompactPosition(GatewayEmptyDetailsPanel, 1, new Thickness());
        }
        else
        {
            GatewayContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            GatewayContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            SetWidePosition(GatewayCatalogPanel, 0, new Thickness(0, 0, 12, 0));
            SetWidePosition(GatewayDetailsPanel, 1, new Thickness());
            SetWidePosition(GatewayEmptyDetailsPanel, 1, new Thickness());
        }
    }

    private static void SetCompactPosition(FrameworkElement element, int row, Thickness margin)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, 0);
        element.Margin = margin;
    }

    private static void SetWidePosition(FrameworkElement element, int column, Thickness margin)
    {
        Grid.SetRow(element, 0);
        Grid.SetColumn(element, column);
        element.Margin = margin;
    }
}
