using System.Windows;
using System.Windows.Controls;
using SoulExe.ViewModels;

namespace SoulExe.Views;

public partial class MobileAccessView : UserControl
{
    private const double CompactWidth = 680;

    public MobileAccessView()
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
        MobileAccessLayout.ColumnDefinitions.Clear();
        MobileAccessLayout.RowDefinitions.Clear();

        if (compact)
        {
            MobileAccessLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            MobileAccessLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(MobileAccessControls, 0);
            Grid.SetColumn(MobileAccessControls, 0);
            Grid.SetRow(MobileAccessStatus, 1);
            Grid.SetColumn(MobileAccessStatus, 0);
            MobileAccessStatus.Margin = new Thickness(0, 16, 0, 0);
            NetworkAddressRow.LastChildFill = false;
            DockPanel.SetDock(CopyNetworkAddressButton, Dock.Top);
            CopyNetworkAddressButton.Margin = new Thickness(0, 0, 0, 8);
            NetworkStartRow.Orientation = Orientation.Vertical;
            NetworkStartHint.Margin = new Thickness(0, 10, 0, 0);
        }
        else
        {
            MobileAccessLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            MobileAccessLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            MobileAccessLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            Grid.SetRow(MobileAccessControls, 0);
            Grid.SetColumn(MobileAccessControls, 0);
            Grid.SetRow(MobileAccessStatus, 0);
            Grid.SetColumn(MobileAccessStatus, 2);
            MobileAccessStatus.Margin = new Thickness();
            NetworkAddressRow.LastChildFill = true;
            DockPanel.SetDock(CopyNetworkAddressButton, Dock.Right);
            CopyNetworkAddressButton.Margin = new Thickness(10, 0, 0, 0);
            NetworkStartRow.Orientation = Orientation.Horizontal;
            NetworkStartHint.Margin = new Thickness(12, 0, 0, 0);
        }
    }

    private void MobileAccessPassword_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is PasswordBox passwordBox)
            viewModel.MobileAccessPassword = passwordBox.Password;
    }
}
