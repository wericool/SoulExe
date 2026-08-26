using System.ComponentModel;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SoulExe.Services;
using SoulExe.ViewModels;

namespace SoulExe;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public string BuildInfoDisplay => $"build {SoulExe.BuildInfo.BuildNumber} · {SoulExe.BuildInfo.BuildTime}";

    public MainWindow()
    {
        InitializeComponent();
        ApplyExecutableIconSafely();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    // --- Развёртывание без перекрытия панели задач. ---
    // WindowStyle=None разворачивается на весь монитор; WM_GETMINMAXINFO
    // принудительно ограничивает развёрнутый прямоугольник рабочей областью
    // монитора, на котором находится окно.
    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ((HwndSource?)PresentationSource.FromVisual(this))?.AddHook(MaximizeWorkAreaHook);
    }

    private static IntPtr MaximizeWorkAreaHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO();
        info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            mmi.ptMaxPosition = new POINT
            {
                X = Math.Abs(info.rcWork.Left - info.rcMonitor.Left),
                Y = Math.Abs(info.rcWork.Top - info.rcMonitor.Top)
            };
            mmi.ptMaxSize = new POINT
            {
                X = Math.Abs(info.rcWork.Right - info.rcWork.Left),
                Y = Math.Abs(info.rcWork.Bottom - info.rcWork.Top)
            };
        }
        Marshal.StructureToPtr(mmi, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private void ApplyExecutableIconSafely()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/SoulExeWindowIcon.png", UriKind.Absolute));
            if (resource?.Stream is null) return;

            using (resource.Stream)
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = resource.Stream;
                bitmap.EndInit();
                bitmap.Freeze();
                Icon = bitmap;
            }
        }
        catch (Exception exception)
        {
            AppLog.Write("Не удалось назначить иконку окна SoulExe", exception);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel = await MainViewModel.CreateAsync();
            DataContext = _viewModel;
        }
        catch (Exception exception)
        {
            AppLog.Write("Failed to initialise the main window.", exception);
            MessageBox.Show(
                $"Не удалось загрузить локальные данные SoulExe.\n\n{exception.Message}\n\nЖурнал: {AppLog.LogFilePath}",
                "SoulExe — ошибка запуска",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_viewModel is not null)
        {
            await _viewModel.DisposeAsync();
            _viewModel = null;
            DataContext = null;
        }
    }

    // Сохранённый «нормальный» размер: ручная установка Width/Height в Maximized
    // затирает RestoreBounds, и после «свернуть из полноэкранного» окно остаётся огромным.
    private Rect _normalBounds = Rect.Empty;

    private void MinimizeWindowButton_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void ToggleMaximizeWindowButton_OnClick(object sender, RoutedEventArgs e) => ToggleWindowState();

    private void CloseWindowButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void ToggleWindowState()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            RestoreNormalBounds();
            return;
        }

        CaptureNormalBounds();
        WindowState = WindowState.Maximized;
    }

    private void CaptureNormalBounds()
    {
        // Пока окно в Normal — запоминаем актуальные границы (и для системного maximize).
        if (WindowState != WindowState.Normal) return;
        if (double.IsNaN(Left) || double.IsNaN(Top) || Width <= 0 || Height <= 0) return;
        _normalBounds = new Rect(Left, Top, Width, Height);
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (WindowState == WindowState.Normal) CaptureNormalBounds();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (WindowState == WindowState.Normal) CaptureNormalBounds();
    }

    private void RestoreNormalBounds()
    {
        ClearValue(MaxWidthProperty);
        ClearValue(MaxHeightProperty);
        if (_normalBounds.IsEmpty) return;

        // Принудительно возвращаем размер до развёртывания.
        Left = _normalBounds.X;
        Top = _normalBounds.Y;
        Width = Math.Max(MinWidth, _normalBounds.Width);
        Height = Math.Max(MinHeight, _normalBounds.Height);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Maximized)
        {
            // На всякий случай, если развернули системно (не только нашей кнопкой).
            if (_normalBounds.IsEmpty && RestoreBounds.Width > 0 && RestoreBounds.Height > 0)
                _normalBounds = RestoreBounds;
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            ClearValue(MaxWidthProperty);
            ClearValue(MaxHeightProperty);
            // После Maximized Width/Height часто остаются равными экрану — возвращаем сохранённые.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RestoreNormalBounds));
        }
    }

}
