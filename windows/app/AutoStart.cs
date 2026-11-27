using System;
using System.IO;
using System.Reflection;
using System.Security;
using Microsoft.Win32;

internal static class AutoStart
{
    internal const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "KeycatDesktop";

    public static string CurrentExecutablePath()
    {
        return Assembly.GetExecutingAssembly().Location;
    }

    public static bool IsEnabled()
    {
        return IsEnabled(DefaultRunKeyPath, ValueName, CurrentExecutablePath());
    }

    public static void SetEnabled(bool enable)
    {
        SetEnabled(DefaultRunKeyPath, ValueName, CurrentExecutablePath(), enable);
    }

    internal static bool IsEnabled(string runKeyPath, string valueName, string expectedExecutablePath)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKeyPath, false))
            {
                if (key == null) return false;
                string raw = key.GetValue(valueName) as string;
                if (String.IsNullOrWhiteSpace(raw)) return false;
                string commandPath = ExtractCommandPath(raw);
                return SamePath(commandPath, expectedExecutablePath);
            }
        }
        catch (SecurityException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    internal static void SetEnabled(string runKeyPath, string valueName, string executablePath, bool enable)
    {
        if (enable)
        {
            string fullPath = Path.GetFullPath(executablePath);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(runKeyPath))
            {
                if (key == null) throw new InvalidOperationException("Unable to open startup registry key.");
                key.SetValue(valueName, "\"" + fullPath + "\"", RegistryValueKind.String);
            }
        }
        else
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKeyPath, true))
            {
                if (key != null) key.DeleteValue(valueName, false);
            }
        }
    }

    internal static string ExtractCommandPath(string command)
    {
        if (String.IsNullOrWhiteSpace(command)) return String.Empty;
        string trimmed = command.Trim();
        if (trimmed[0] == '"')
        {
            int endQuote = trimmed.IndexOf('"', 1);
            return endQuote > 1 ? trimmed.Substring(1, endQuote - 1) : trimmed.Trim('"');
        }
        int exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0) return trimmed.Substring(0, exeIndex + 4);
        int space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed.Substring(0, space) : trimmed;
    }

    internal static bool SamePath(string a, string b)
    {
        if (String.IsNullOrWhiteSpace(a) || String.IsNullOrWhiteSpace(b)) return false;
        try
        {
            return String.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }
}
