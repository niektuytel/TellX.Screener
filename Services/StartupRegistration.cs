using Microsoft.Win32;

namespace WorkflowTracker.Services;

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string PreferencesKey = @"Software\WorkflowTracker";
    private const string ValueName = "WorkflowTracker";

    public static bool IsOptedOut
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(PreferencesKey);
            return key?.GetValue("StartupDisabled") is int value && value == 1;
        }
    }

    public static bool IsRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string command &&
                string.Equals(command, BuildCommand(), StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string BuildCommand()
    {
        // Assembly.Location is leeg bij single-file; ProcessPath wijst naar de echte exe.
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Exe-pad ontbreekt.");
        if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Start de app via WorkflowTracker.exe voor automatische opstart.");
        string command = $"\"{exe}\"";
        if (command.Length > 260) throw new InvalidOperationException("Exe-pad is te lang voor de Run-key.");
        return command;
    }

    public static void EnsureRegistered()
    {
        string command = BuildCommand();
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Run-key kon niet worden geopend.");
        if (!string.Equals(key.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase))
            key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled) EnsureRegistered();
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        using var preferences = Registry.CurrentUser.CreateSubKey(PreferencesKey, writable: true)
            ?? throw new InvalidOperationException("Voorkeuren konden niet worden opgeslagen.");
        preferences.SetValue("StartupDisabled", enabled ? 0 : 1, RegistryValueKind.DWord);
    }

    public static void RemoveForUninstall()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            // Bewaar een eventuele registratie die inmiddels naar een andere exe wijst.
            if (string.Equals(key?.GetValue(ValueName) as string, BuildCommand(), StringComparison.OrdinalIgnoreCase))
                key!.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(PreferencesKey, throwOnMissingSubKey: false);
    }
}
