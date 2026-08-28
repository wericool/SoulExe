using System.Globalization;
using System.Windows.Data;

namespace SoulExe;

/// <summary>Shows the chosen solid stage colour only when the active avatar cannot be used.</summary>
public sealed class AvatarBackgroundFallbackOpacityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var useAvatar = values.Length > 0 && values[0] is true;
        var isScene = values.Length > 1 && values[1] is true;
        var avatarPath = values.Length > 3 && isScene ? values[3] as string : values.Length > 2 ? values[2] as string : null;
        return useAvatar && !string.IsNullOrWhiteSpace(avatarPath) ? 0d : 1d;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
