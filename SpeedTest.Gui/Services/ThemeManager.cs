using System.Windows;

namespace SpeedTest.Gui.Services;

/// <summary>
/// Schaltet zwischen Dark- und Light-Theme um, indem das Farb-Dictionary
/// (MergedDictionaries[0] der App) ausgetauscht wird; alle DynamicResource-Verweise
/// färben dadurch zur Laufzeit um.
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; } = true;

    public static void Apply(bool dark)
    {
        IsDark = dark;
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri($"Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative),
        };
    }
}
