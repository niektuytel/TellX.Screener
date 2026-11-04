using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WorkflowTracker.Models;
using WorkflowTracker.Services;

namespace WorkflowTracker;

public partial class MainWindow : Window
{
    private readonly SettingsStore _settingsStore = new();
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        // Elke adapter past de instelbare timeout per verzoek toe.
        Timeout = Timeout.InfiniteTimeSpan
    };
    private TrackerSettings _settings = new();
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private bool _transitioning;
    private bool _allowClose;
    private bool _automaticPause;
    private bool _manualScheduleOverride;
    private int _uninstallRequested;
    private Point _mouseDown;
    private bool _dragStarted;
    private string _status = "Gepauzeerd";

    public MainWindow()
    {
        InitializeComponent();
        UpdateAppearance();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        try { OverlayWindowPolicy.Apply(new WindowInteropHelper(this).Handle); }
        catch (Exception ex) { AppLog.Write("Overlay-vensterstijl kon niet worden ingesteld", ex); }
        // Ondersteund vanaf Windows 10 2004: houd de eigen knop uit schermopnamen.
        if (!SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, 0x11))
            AppLog.Write("Windows kon de overlay niet uitsluiten van schermopnamen");
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 24;
        Top = area.Bottom - Height - 24;
        try
        {
            _settings = _settingsStore.Load();
            if (_settings.ValidationError() is not null && !EditSettings())
                return;
            await StartRecordingAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write("Opstarten mislukt", ex);
            SetStatus("Instellingen konden niet worden geladen; open Instellingen via rechtsklik.");
        }
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dragStarted)
        {
            _dragStarted = false;
            return;
        }
        if (_transitioning) return;
        _transitioning = true;
        ToggleButton.IsEnabled = false;
        try
        {
            if (_runCts is not null && !_automaticPause)
                await StopRecordingAsync();
            else
            {
                // De amberkleurige Play-knop moet met één klik meteen opnemen.
                if (_runCts is not null) await StopRecordingAsync();
                if (_settings.ValidationError() is null || EditSettings())
                {
                    bool outsideHours = !RecordingSchedule.Evaluate(_settings, DateTimeOffset.UtcNow, TimeZoneInfo.Local).IsActive;
                    await StartRecordingAsync(manualScheduleOverride: outsideHours);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Opnamestatus wijzigen mislukt", ex);
            SetStatus("Actie mislukt; zie logbestand.");
        }
        finally
        {
            _transitioning = false;
            ToggleButton.IsEnabled = true;
            UpdateAppearance();
            CloseIfUninstallRequested();
        }
    }

    private Task StartRecordingAsync(bool manualScheduleOverride = false)
    {
        if (_runCts is not null || Volatile.Read(ref _uninstallRequested) == 1) return Task.CompletedTask;
        _runCts = new CancellationTokenSource();
        var token = _runCts.Token;
        _manualScheduleOverride = manualScheduleOverride;
        _automaticPause = !RecordingSchedule.Evaluate(_settings, DateTimeOffset.UtcNow, TimeZoneInfo.Local,
            manualScheduleOverride).IsActive;
        var worker = new CaptureWorker(new ScreenCapture(), new VisionClient(_http, () => Volatile.Read(ref _settings)),
            new WebhookClient(_http, () => Volatile.Read(ref _settings)), status => Dispatcher.BeginInvoke(
                new Action(() => { if (!token.IsCancellationRequested) SetStatus(status); })),
            interval: TimeSpan.FromSeconds(_settings.CaptureIntervalSeconds),
            requestUninstall: RequestUninstall,
            applyRemoteSettings: ApplyRemoteSettings,
            getSettings: () => Volatile.Read(ref _settings),
            userActivity: new UserActivity(),
            reportAutomaticPause: paused => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (token.IsCancellationRequested || _automaticPause == paused) return;
                _automaticPause = paused;
                UpdateAppearance();
            })),
            manualScheduleOverride: manualScheduleOverride);
        SetStatus($"Opname actief — elke {_settings.CaptureIntervalSeconds} seconden");
        _runTask = Task.Run(() => worker.RunAsync(token));
        UpdateAppearance();
        return Task.CompletedTask;
    }

    private async Task StopRecordingAsync()
    {
        var cts = _runCts;
        if (cts is null) return;
        // Eerst annuleren; pas opnieuw starten nadat de vorige loop echt is beëindigd.
        cts.Cancel();
        try
        {
            if (_runTask is not null) await _runTask;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write("Capture-loop beëindigd met een fout", ex); }
        finally
        {
            cts.Dispose();
            _runCts = null;
            _runTask = null;
            _automaticPause = false;
            _manualScheduleOverride = false;
            SetStatus("Gepauzeerd");
            UpdateAppearance();
        }
    }

    private RemoteSettingsUpdate ApplyRemoteSettings(RemoteSettingsPatch patch)
    {
        var update = RemoteSettingsUpdater.Apply(Volatile.Read(ref _settings), patch, _settingsStore.Save);
        if (update.Changed) Volatile.Write(ref _settings, update.Settings);
        return update;
    }

    private void ToggleButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDown = e.GetPosition(this);
        _dragStarted = false;
    }

    private void ToggleButton_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStarted) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _mouseDown.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _mouseDown.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragStarted = true;
        // DragMove pas na de sleepdrempel; een gewone klik blijft Play/Pauze.
        ToggleButton.ReleaseMouseCapture();
        try { DragMove(); }
        catch (InvalidOperationException) { /* De muis kan al zijn losgelaten. */ }
        e.Handled = true;
    }

    private bool EditSettings()
    {
        if (Volatile.Read(ref _uninstallRequested) == 1) return false;
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true) return false;
        try
        {
            _settingsStore.Save(dialog.Settings);
            _settings = dialog.Settings;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("Instellingen opslaan mislukt", ex);
            MessageBox.Show(this, "Opslaan mislukt. Zie het logbestand.", "Workflow Tracker");
            return false;
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_transitioning) return;
        _transitioning = true;
        ToggleButton.IsEnabled = false;
        bool wasRunning = _runCts is not null;
        bool resumeManualOverride = _manualScheduleOverride;
        try
        {
            await StopRecordingAsync();
            EditSettings();
            if (wasRunning && _settings.ValidationError() is null)
                await StartRecordingAsync(manualScheduleOverride: resumeManualOverride);
        }
        finally
        {
            _transitioning = false;
            ToggleButton.IsEnabled = true;
            CloseIfUninstallRequested();
        }
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        StatusMenu.Header = _status;
        StartupMenu.IsChecked = StartupRegistration.IsRegistered;
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        try { StartupRegistration.SetEnabled(StartupMenu.IsChecked); }
        catch (Exception ex)
        {
            AppLog.Write("Automatisch opstarten wijzigen mislukt", ex);
            MessageBox.Show(this, "Automatisch opstarten wijzigen is mislukt. Zie het logbestand.",
                "Workflow Tracker");
        }
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLog.DirectoryPath);
            Process.Start(new ProcessStartInfo(AppLog.DirectoryPath) { UseShellExecute = true });
        }
        catch (Exception ex) { AppLog.Write("Logmap openen mislukt", ex); }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void RequestUninstall()
    {
        // De worker stopt meteen; alleen de UI-thread sluit het venster.
        Interlocked.Exchange(ref _uninstallRequested, 1);
        Dispatcher.BeginInvoke(new Action(CloseIfUninstallRequested));
    }

    private void CloseIfUninstallRequested()
    {
        if (Volatile.Read(ref _uninstallRequested) == 1 && !_transitioning) Close();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_transitioning) return;
        _transitioning = true;
        ToggleButton.IsEnabled = false;
        await StopRecordingAsync();
        if (Volatile.Read(ref _uninstallRequested) == 1)
        {
            try
            {
                SetStatus("Workflow Tracker wordt verwijderd…");
                await new UninstallCoordinator().ScheduleAsync();
            }
            catch (Exception ex)
            {
                AppLog.Write("Automatisch verwijderen kon niet worden voorbereid", ex);
                Interlocked.Exchange(ref _uninstallRequested, 0);
                _transitioning = false;
                ToggleButton.IsEnabled = true;
                SetStatus("Verwijderen mislukt — opname gepauzeerd; zie logbestand.");
                MessageBox.Show(this,
                    "Automatisch verwijderen kon niet worden voorbereid. De opname is gepauzeerd. " +
                    "Zie het logbestand of sluit de app en verwijder de exe handmatig.",
                    "Workflow Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        _http.Dispose();
        _allowClose = true;
        Application.Current.Shutdown();
    }

    private void SetStatus(string status)
    {
        if (_status == status && ToggleButton.ToolTip is not null) return;
        _status = status;
        ToggleButton.ToolTip = $"{status}\nKlik: Play/Pauze · Slepen: verplaatsen · Rechtsklik: menu";
        StatusMenu.Header = status;
    }

    private void UpdateAppearance()
    {
        bool running = _runCts is not null && !_automaticPause;
        PlayIcon.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        PauseIcon.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ToggleButton.Background = new SolidColorBrush(_runCts is not null && _automaticPause
            ? Color.FromRgb(169, 111, 18)
            : running ? Color.FromRgb(18, 139, 103) : Color.FromRgb(38, 51, 72));
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
}
