using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UI.Console;

public static class FolderOpener
{
    public static void Open(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", $"\"{path}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    Process.Start("fly-fm", $"\"{path}\"");
                }
                catch {
                    try
                    {
                        Process.Start("xdg-open", $"\"{path}\"");
                    }
                    catch
                    {

                    }
                }

            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[WARNING] Could not open folder: {ex.Message}");
        }
    }
}