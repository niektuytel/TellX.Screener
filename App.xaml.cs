using System.Windows;
using WorkflowTracker.Services;

namespace WorkflowTracker;

public partial class App : Application
{
    private Mutex? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Local = één instantie per aangemelde Windows-sessie.
        _instance = new Mutex(true, @"Local\WorkflowTracker.Overlay", out bool created);
        if (!created)
        {
            Shutdown();
            return;
        }

        try
        {
            // Een expliciete uitschakeling via het contextmenu blijft behouden.
            if (!StartupRegistration.IsOptedOut)
                StartupRegistration.EnsureRegistered();
        }
        catch (Exception ex)
        {
            AppLog.Write("Automatisch opstarten registreren is mislukt", ex);
            MessageBox.Show("Automatisch opstarten kon niet worden geregistreerd. " +
                "De app kan wel handmatig worden gebruikt. Zie het lokale logbestand.",
                "Workflow Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }
}
