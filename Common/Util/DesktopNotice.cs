using System.Runtime.InteropServices;

namespace MikuSB.Util;

/// <summary>
///     Last-resort dialogs for problems the operator has to fix by hand: a port another program already owns, a
///     second copy of the server being started. Console output alone is not enough when the app was launched by
///     double clicking the exe, because that window disappears together with the message.
/// </summary>
/// <remarks>
///     <see cref="ShowError" /> blocks the calling thread until the box is dismissed, so background callers have to
///     push it onto their own thread. Dialogs can be suppressed with <c>MIKUSB_DIALOG=0</c> (tests, service hosts).
/// </remarks>
public static class DesktopNotice
{
    private const uint IconError = 0x00000010;
    private const uint IconWarning = 0x00000030;
    private const uint SetForeground = 0x00010000;
    private const uint TopMost = 0x00040000;

    private const string DialogToggleVariable = "MIKUSB_DIALOG";

    public static bool IsEnabled()
    {
        var toggle = Environment.GetEnvironmentVariable(DialogToggleVariable)?.Trim();
        if (!string.IsNullOrEmpty(toggle) &&
            (toggle == "0" || toggle.Equals("false", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return Environment.UserInteractive;
    }

    public static void ShowError(string title, string message) => Show(title, message, IconError);

    public static void ShowWarning(string title, string message) => Show(title, message, IconWarning);

    private static void Show(string title, string message, uint icon)
    {
        if (!IsEnabled())
        {
            return;
        }

        try
        {
            MessageBoxW(IntPtr.Zero, message, title, icon | SetForeground | TopMost);
        }
        catch
        {
            // A missing user32 or a window station that refuses the box must never take the server down.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
