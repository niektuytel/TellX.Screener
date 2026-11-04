using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace WorkflowTracker.Services;

internal sealed record UninstallPlan(
    int TargetPid, string TargetStartTicks, string TargetExe, string ExeSha256,
    string DataRoot, string ReadyEvent, string ProceedEvent, string ResultEvent);

public sealed class UninstallCoordinator
{
    public async Task ScheduleAsync()
    {
        using var current = Process.GetCurrentProcess();
        string exe = Path.GetFullPath(Environment.ProcessPath ?? throw new InvalidOperationException("Exe-pad ontbreekt."));
        if (!Path.GetExtension(exe).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Verwijderen vereist de zelfstandige exe.");

        string id = Guid.NewGuid().ToString("N");
        string prefix = @"Local\WorkflowTracker.Uninstall." + id;
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + ".Ready");
        using var proceed = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + ".Proceed");
        using var success = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + ".Success");
        string hash;
        using (var stream = File.OpenRead(exe)) hash = Convert.ToHexString(SHA256.HashData(stream));
        var plan = new UninstallPlan(current.Id,
            current.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            exe, hash,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkflowTracker"),
            prefix + ".Ready", prefix + ".Proceed", prefix + ".Success");

        using var helper = Process.Start(BuildHelperStartInfo(plan))
            ?? throw new InvalidOperationException("Opruimproces kon niet worden gestart.");
        bool started = await Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(15))).ConfigureAwait(false);
        if (!started || helper.HasExited)
            throw new InvalidOperationException("Opruimproces kon niet worden voorbereid.");

        // Twee fasen: bij een fout krijgt de helper geen toestemming en verwijdert hij niets.
        StartupRegistration.RemoveForUninstall();
        if (!proceed.Set()) throw new InvalidOperationException("Opruimproces kon niet worden vrijgegeven.");
        // De helper heeft beide eventhandles al geopend en wacht nu op het einde van deze app.
    }

    internal static ProcessStartInfo BuildHelperStartInfo(UninstallPlan plan)
    {
        using var resource = typeof(UninstallCoordinator).Assembly
            .GetManifestResourceStream("WorkflowTracker.Services.UninstallCleanup.ps1")
            ?? throw new InvalidOperationException("Opruimscript ontbreekt.");
        using var reader = new StreamReader(resource);
        var start = new ProcessStartInfo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetTempPath()
        };
        // Rebuild Windows PowerShell 5.1's own module path when the app was launched from pwsh 7.
        // Inheriting pwsh's PSModulePath can hide built-ins such as Get-FileHash.
        start.Environment.Remove("PSModulePath");
        foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", reader.ReadToEnd() })
            start.ArgumentList.Add(argument);
        // Uitsluitend data in de environment; paden worden nooit in scriptcode geïnterpoleerd.
        start.Environment["WORKFLOWTRACKER_UNINSTALL_PLAN"] = JsonSerializer.Serialize(plan);
        return start;
    }
}
