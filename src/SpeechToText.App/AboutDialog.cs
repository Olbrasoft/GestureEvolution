using System.Diagnostics;

namespace Olbrasoft.SpeechToText.App;

/// <summary>
/// Shows the About dialog using zenity (GNOME dialog tool).
/// </summary>
public static class AboutDialog
{
    /// <summary>
    /// Shows the About dialog.
    /// </summary>
    /// <param name="version">Application version to display.</param>
    public static void Show(string version)
    {
        try
        {
            var aboutText = $"Speech to Text\n\n" +
                            $"Version: {version}\n\n" +
                            $"Voice transcription using Whisper AI.\n" +
                            $"Press CapsLock to start dictation.\n\n" +
                            $"https://github.com/Olbrasoft/PushToTalk";

            var startInfo = new ProcessStartInfo
            {
                FileName = "zenity",
                Arguments = $"--info --title=\"About Speech to Text\" --text=\"{aboutText.Replace("\"", "\\\"")}\" --width=400",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not show About dialog: {ex.Message}");
            Console.WriteLine($"Speech to Text v{version}");
            Console.WriteLine("https://github.com/Olbrasoft/PushToTalk");
        }
    }
}
