using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SoulExe;

/// <summary>Uses a transparent chat surface when the active character avatar is the stage background.</summary>
public sealed class AvatarAwareChatSurfaceBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var useAvatar = values.Length > 0 && values[0] is true;
        var isScene = values.Length > 1 && values[1] is true;
        var avatarPath = values.Length > 3 && isScene ? values[3] as string : values.Length > 2 ? values[2] as string : null;
        if (useAvatar && !string.IsNullOrWhiteSpace(avatarPath)) return Brushes.Transparent;

        var fallback = values.Length > 4 ? values[4] : null;
        if (fallback is Brush brush) return brush;
        if (fallback is string color && new BrushConverter().ConvertFromInvariantString(color) is Brush converted) return converted;
        return Brushes.Transparent;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
