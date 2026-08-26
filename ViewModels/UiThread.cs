using System.Windows;
using System.Windows.Threading;

namespace SoulExe.ViewModels;

/// <summary>Small dispatcher helpers so MainViewModel does not repeat Invoke/BeginInvoke patterns.</summary>
public static class UiThread
{
    public static Dispatcher? Dispatcher => Application.Current?.Dispatcher;

    public static async Task InvokeAsync(Action action)
    {
        var dispatcher = Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }
        await dispatcher.InvokeAsync(action).Task;
    }

    public static async Task InvokeAsync(Func<Task> action)
    {
        var dispatcher = Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            await action();
            return;
        }
        await dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    public static void BeginInvoke(Action action)
    {
        var dispatcher = Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }
        _ = dispatcher.BeginInvoke(action);
    }
}
