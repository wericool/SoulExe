using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoulExe.Views;

public partial class ModelsView : UserControl
{
    private const double CompactWidth = 1050;

    public ModelsView()
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
        var root = Content as Grid;
        if (root is null || root.Children.Count < 5) return;

        NormalizeRefreshButtons(root);

        var compact = ActualWidth < CompactWidth;
        var runtimeGrid = FindDescendant<Grid>(root.Children[1]);
        var runtimeActions = runtimeGrid?.Children.Count > 1 ? runtimeGrid.Children[1] as StackPanel : null;
        if (runtimeGrid is not null && runtimeActions is not null)
        {
            runtimeGrid.RowDefinitions.Clear();
            if (compact)
            {
                runtimeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                runtimeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            runtimeActions.Orientation = compact ? Orientation.Vertical : Orientation.Horizontal;
            runtimeActions.Margin = compact ? new Thickness(0, 12, 0, 0) : new Thickness(16, 0, 0, 0);
            Grid.SetColumn(runtimeActions, compact ? 0 : 1);
            Grid.SetRow(runtimeActions, compact ? 1 : 0);
        }

        var catalogGrid = FindDescendant<Grid>(root.Children[2]);
        if (catalogGrid is not null)
        {
            var contentGrid = catalogGrid.Children.Count > 1 ? catalogGrid.Children[1] as Grid : null;
            if (contentGrid is not null && contentGrid.Children.Count == 3)
                SetCatalogLayout(contentGrid, compact);
        }

        if (root.Children[3] is Grid recommendationsGrid && recommendationsGrid.Children.Count == 2)
            SetRecommendationsLayout(recommendationsGrid, compact);
    }

    private static void NormalizeRefreshButtons(DependencyObject root)
    {
        if (root is Button { Content: string text } button && text.Contains("Обновить", StringComparison.OrdinalIgnoreCase))
        {
            button.Width = 96;
            button.MinWidth = 0;
            button.HorizontalAlignment = HorizontalAlignment.Right;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            NormalizeRefreshButtons(VisualTreeHelper.GetChild(root, index));
    }

    private static void SetCatalogLayout(Grid grid, bool compact)
    {
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        if (compact)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(220) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SetVerticalPosition(grid.Children[0], 0, new Thickness());
            SetVerticalPosition(grid.Children[1], 1, new Thickness(0, 14, 0, 0));
            SetVerticalPosition(grid.Children[2], 2, new Thickness(0, 14, 0, 0));
        }
        else
        {
            for (var index = 0; index < 3; index++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SetWidePosition(grid.Children[0], 0, new Thickness());
            SetWidePosition(grid.Children[1], 1, new Thickness(14, 0, 14, 0));
            SetWidePosition(grid.Children[2], 2, new Thickness());
        }
    }

    private static void SetRecommendationsLayout(Grid grid, bool compact)
    {
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        if (compact)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(300) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SetVerticalPosition(grid.Children[0], 0, new Thickness(0, 0, 0, 12));
            SetVerticalPosition(grid.Children[1], 1, new Thickness());
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            SetWidePosition(grid.Children[0], 0, new Thickness(0, 0, 12, 0));
            SetWidePosition(grid.Children[1], 1, new Thickness());
        }
    }

    private static void SetVerticalPosition(UIElement element, int row, Thickness margin)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, 0);
        if (element is FrameworkElement frameworkElement) frameworkElement.Margin = margin;
    }

    private static void SetWidePosition(UIElement element, int column, Thickness margin)
    {
        Grid.SetRow(element, 0);
        Grid.SetColumn(element, column);
        if (element is FrameworkElement frameworkElement) frameworkElement.Margin = margin;
    }

    private static T? FindDescendant<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null) return null;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            var descendant = FindDescendant<T>(child);
            if (descendant is not null) return descendant;
        }

        return null;
    }
}
