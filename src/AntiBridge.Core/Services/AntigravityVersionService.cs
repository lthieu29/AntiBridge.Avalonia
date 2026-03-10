using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace AntiBridge.Core.Services;

/// <summary>
/// Service to detect Antigravity IDE version and determine token format.
/// Ported from Antigravity-Manager version detection logic.
/// </summary>
public class AntigravityVersionService
{
    /// <summary>
    /// Result of version detection.
    /// </summary>
    /// <param name="Version">Semantic version string (e.g. "1.16.5")</param>
    /// <param name="IsNewFormat">True if version >= 1.16.5 (New_Format)</param>
    public record VersionResult(string Version, bool IsNewFormat);

    private const string VersionThreshold = "1.16.5";

    /// <summary>
    /// Compare two semantic version strings component by component.
    /// Returns: >0 if v1 > v2, 0 if equal, &lt;0 if v1 &lt; v2.
    /// </summary>
    public static int CompareVersions(string v1, string v2)
    {
        var parts1 = v1.Split('.');
        var parts2 = v2.Split('.');

        var maxLen = Math.Max(parts1.Length, parts2.Length);

        for (int i = 0; i < maxLen; i++)
        {
            var num1 = i < parts1.Length && int.TryParse(parts1[i], out var n1) ? n1 : 0;
            var num2 = i < parts2.Length && int.TryParse(parts2[i], out var n2) ? n2 : 0;

            if (num1 != num2)
                return num1.CompareTo(num2);
        }

        return 0;
    }

    /// <summary>
    /// Check if the given version uses New_Format (>= 1.16.5).
    /// </summary>
    public static bool IsNewVersion(string version)
    {
        return CompareVersions(version, VersionThreshold) >= 0;
    }

    /// <summary>
    /// Detect the installed Antigravity IDE version.
    /// Returns null if version cannot be determined.
    /// </summary>
    public VersionResult? GetAntigravityVersion()
    {
        try
        {
            var exePath = AntigravityProcessService.GetAntigravityExecutablePath();
            if (string.IsNullOrEmpty(exePath))
                return null;

            string? version = null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                version = GetVersionWindows(exePath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                version = GetVersionMacOS(exePath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                version = GetVersionLinux(exePath);
            }

            if (string.IsNullOrEmpty(version))
                return null;

            return new VersionResult(version, IsNewVersion(version));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get version on Windows using PowerShell to read file version info.
    /// </summary>
    private static string? GetVersionWindows(string exePath)
    {
        try
        {
            // Find the actual .exe path (exePath might be .cmd)
            var exeDir = Path.GetDirectoryName(exePath);
            var actualExe = exePath;

            // If it's a .cmd file, look for the .exe in parent directory
            if (exePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) && exeDir != null)
            {
                var parentDir = Path.GetDirectoryName(exeDir);
                if (parentDir != null)
                {
                    var possibleExe = Path.Combine(parentDir, "Antigravity.exe");
                    if (File.Exists(possibleExe))
                        actualExe = possibleExe;
                }
            }

            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -Command \"(Get-Item '{actualExe}').VersionInfo.FileVersion\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(10000);

            if (proc.ExitCode != 0 || string.IsNullOrEmpty(output))
                return null;

            return NormalizeVersion(output);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get version on macOS by parsing Info.plist from the .app bundle.
    /// </summary>
    private static string? GetVersionMacOS(string exePath)
    {
        try
        {
            // Navigate from .app/Contents/MacOS/Antigravity to .app/Contents/Info.plist
            var macosDir = Path.GetDirectoryName(exePath);
            if (macosDir == null) return null;

            var contentsDir = Path.GetDirectoryName(macosDir);
            if (contentsDir == null) return null;

            var plistPath = Path.Combine(contentsDir, "Info.plist");
            if (!File.Exists(plistPath))
                return null;

            var doc = XDocument.Load(plistPath);
            var dict = doc.Root?.Element("dict");
            if (dict == null) return null;

            // Find CFBundleShortVersionString in the plist
            var elements = dict.Elements().ToList();
            for (int i = 0; i < elements.Count - 1; i++)
            {
                if (elements[i].Name == "key" &&
                    elements[i].Value == "CFBundleShortVersionString")
                {
                    var versionElement = elements[i + 1];
                    if (versionElement.Name == "string")
                        return NormalizeVersion(versionElement.Value);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get version on Linux by running --version or reading package.json.
    /// </summary>
    private static string? GetVersionLinux(string exePath)
    {
        // Try --version first
        var version = GetVersionFromCommand(exePath);
        if (!string.IsNullOrEmpty(version))
            return version;

        // Fallback: try reading package.json from resources/app/
        return GetVersionFromPackageJson(exePath);
    }

    /// <summary>
    /// Try to get version by running the executable with --version flag.
    /// </summary>
    private static string? GetVersionFromCommand(string exePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(10000);

            if (proc.ExitCode != 0 || string.IsNullOrEmpty(output))
                return null;

            return NormalizeVersion(output);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Try to get version from package.json in the installation directory.
    /// </summary>
    private static string? GetVersionFromPackageJson(string exePath)
    {
        try
        {
            var exeDir = Path.GetDirectoryName(exePath);
            if (exeDir == null) return null;

            // Try common locations for package.json relative to the executable
            var possiblePaths = new[]
            {
                Path.Combine(exeDir, "..", "resources", "app", "package.json"),
                Path.Combine(exeDir, "resources", "app", "package.json"),
                Path.Combine(exeDir, "..", "share", "antigravity", "resources", "app", "package.json")
            };

            foreach (var packagePath in possiblePaths)
            {
                var fullPath = Path.GetFullPath(packagePath);
                if (!File.Exists(fullPath)) continue;

                var json = File.ReadAllText(fullPath);
                // Simple JSON parsing for "version" field
                var version = ExtractVersionFromJson(json);
                if (!string.IsNullOrEmpty(version))
                    return NormalizeVersion(version);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extract "version" field from a JSON string using simple parsing.
    /// Avoids dependency on System.Text.Json for this simple case.
    /// </summary>
    public static string? ExtractVersionFromJson(string json)
    {
        // Look for "version" : "x.y.z" pattern
        const string key = "\"version\"";
        var idx = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        // Find the colon after the key
        var colonIdx = json.IndexOf(':', idx + key.Length);
        if (colonIdx < 0) return null;

        // Find the opening quote of the value
        var openQuote = json.IndexOf('"', colonIdx + 1);
        if (openQuote < 0) return null;

        // Find the closing quote
        var closeQuote = json.IndexOf('"', openQuote + 1);
        if (closeQuote < 0) return null;

        return json.Substring(openQuote + 1, closeQuote - openQuote - 1);
    }

    /// <summary>
    /// Normalize a version string by extracting the semantic version part.
    /// Handles formats like "1.16.5", "1.16.5.0", "Antigravity 1.16.5", etc.
    /// </summary>
    public static string? NormalizeVersion(string rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
            return null;

        // Try to find a version pattern (digits separated by dots)
        var parts = rawVersion.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0 && char.IsDigit(trimmed[0]) && trimmed.Contains('.'))
            {
                // Remove trailing .0 if it's a 4-part version (e.g., "1.16.5.0" -> "1.16.5")
                var versionParts = trimmed.Split('.');
                if (versionParts.Length >= 3)
                    return string.Join(".", versionParts.Take(3));
            }
        }

        return null;
    }
}
