using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SpeedTest.Gui.Interop;

/// <summary>Schaltet die Fenster-Titelleiste über die DWM-API dunkel.</summary>
internal static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Schaltet den dunklen Titelleisten-Modus ein oder aus; frühestens ab
    /// SourceInitialized aufrufen, weil vorher kein Fenster-Handle existiert. Ein
    /// Fehler-HRESULT (ältere Windows-Versionen kennen das Attribut nicht) wird
    /// bewusst ignoriert.
    /// </summary>
    public static void Set(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }
}
