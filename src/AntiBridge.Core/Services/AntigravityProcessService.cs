using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AntiBridge.Core.Services;

/// <summary>
/// Service to manage Antigravity process (restart after token sync)
/// </summary>
public class AntigravityProcessService
{
    public event Action<string>? OnStatusChanged;
    public event Action<string>? OnError;

    /// <summary>
    /// Restart Antigravity IDE (close and reopen)
    /// </summary>
    public bool RestartAntigravity()
    {
        try
        {
            // Kill existing Antigravity processes
            OnStatusChanged?.Invoke("Closing Antigravity...");
            KillAntigravity();

            // Wait a bit for process to fully close
            Thread.Sleep(1500);

            // Launch Antigravity
            OnStatusChanged?.Invoke("Launching Antigravity...");
            LaunchAntigravity();

            OnStatusChanged?.Invoke("Antigravity restarted successfully");
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Failed to restart Antigravity: {ex.Message}");
            return false;
        }
    }

    private void KillAntigravity()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Kill Windows processes
            KillProcess("antigravity.exe");
            KillProcess("antigravity");
        }
        else
        {
            // Kill Linux/macOS processes
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pkill",
                    Arguments = "-f antigravity",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi)?.WaitForExit(5000);
            }
            catch { /* Ignore */ }
        }
    }

    private static void KillProcess(string processName)
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName(processName.Replace(".exe", "")))
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
                catch { /* Ignore */ }
            }
        }
        catch { /* Ignore */ }
    }

    private void LaunchAntigravity()
    {
        string? antigravityPath = GetAntigravityPath();
        if (string.IsNullOrEmpty(antigravityPath))
        {
            throw new FileNotFoundException("Antigravity executable not found");
        }

        var psi = new ProcessStartInfo
        {
            FileName = antigravityPath,
            UseShellExecute = true,
            CreateNoWindow = false
        };

        Process.Start(psi);
    }

    private static string? GetAntigravityPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows paths
            var paths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Antigravity", "bin", "antigravity.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Antigravity", "Antigravity.exe"),
                @"C:\Program Files\Antigravity\bin\antigravity.cmd"
            };

            foreach (var path in paths)
            {
                if (File.Exists(path)) return path;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux paths
            var paths = new[]
            {
                "/usr/bin/antigravity",
                "/usr/local/bin/antigravity",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local", "share", "antigravity", "bin", "antigravity"),
                "/opt/Antigravity/bin/antigravity"
            };

            foreach (var path in paths)
            {
                if (File.Exists(path)) return path;
            }

            // Try to find via which command
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "antigravity",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(3000);
                    if (!string.IsNullOrEmpty(output) && File.Exists(output))
                        return output;
                }
            }
            catch { /* Ignore */ }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS paths
            var paths = new[]
            {
                "/Applications/Antigravity.app/Contents/MacOS/Antigravity",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Applications", "Antigravity.app", "Contents", "MacOS", "Antigravity")
            };

            foreach (var path in paths)
            {
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }
}
