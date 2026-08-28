using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SoulExe.Views;

public partial class SetupView : UserControl
{
    private const double CompactWidth = 680;

    public SetupView()
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
        var backendGrid = FindDescendant<UniformGrid>(SetupInitialFocus);
        if (backendGrid is not null) backendGrid.Columns = compact ? 1 : 3;
        SetEngineInstallLayout(compact);
        SetModelLayout(compact);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T result) return result;
            var descendant = FindDescendant<T>(child);
            if (descendant is not null) return descendant;
        }

        return null;
    }

    private void SetEngineInstallLayout(bool compact)
    {
        SetupEngineInstallLayout.ColumnDefinitions.Clear();
        SetupEngineInstallLayout.RowDefinitions.Clear();
        var details = SetupEngineInstallLayout.Children[0];
        var actions = SetupEngineInstallLayout.Children[1];
        if (compact)
        {
            SetupEngineInstallLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SetupEngineInstallLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SetCompactPosition(details, 0, new Thickness());
            SetCompactPosition(actions, 1, new Thickness(0, 14, 0, 0));
        }
        else
        {
            SetupEngineInstallLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SetupEngineInstallLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SetWidePosition(details, 0, new Thickness());
            SetWidePosition(actions, 1, new Thickness(20, 0, 0, 0));
        }
    }

    private void SetModelLayout(bool compact)
    {
        SetupModelLayout.ColumnDefinitions.Clear();
        SetupModelLayout.RowDefinitions.Clear();
        var list = SetupModelLayout.Children[0];
        var details = SetupModelLayout.Children[1];
        if (compact)
        {
            SetupModelLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(300) });
            SetupModelLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            SetCompactPosition(list, 0, new Thickness());
            SetCompactPosition(details, 1, new Thickness(0, 16, 0, 0));
        }
        else
        {
            SetupModelLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            SetupModelLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            SetupModelLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SetWidePosition(list, 0, new Thickness());
            SetWidePosition(details, 2, new Thickness());
        }
    }

    private static void SetCompactPosition(UIElement element, int row, Thickness margin)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, 0);
        ((FrameworkElement)element).Margin = margin;
    }

    private static void SetWidePosition(UIElement element, int column, Thickness margin)
    {
        Grid.SetRow(element, 0);
        Grid.SetColumn(element, column);
        ((FrameworkElement)element).Margin = margin;
    }

    public void FocusInitialControl() => SetupInitialFocus.Focus();
}
