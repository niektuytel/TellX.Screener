using System.Windows;
using WorkflowTracker.Models;

namespace WorkflowTracker;

public partial class SettingsWindow : Window
{
    public TrackerSettings Settings { get; private set; }

    public SettingsWindow(TrackerSettings settings)
    {
        InitializeComponent();
        MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 48);
        Settings = settings;
        EndpointBox.Text = settings.VisionEndpoint;
        ApiKeyBox.Password = settings.VisionApiKey;
        ModelBox.Text = settings.VisionModel;
        WebhookBox.Text = settings.WebhookUrl;
        WebhookTokenBox.Password = settings.WebhookBearerToken;
        SendScreenshotsBox.IsChecked = settings.SendScreenshots;
        SetCaptureFields(settings);
        SetPromptFields(settings);
    }

    private void SetCaptureFields(TrackerSettings settings)
    {
        IntervalBox.Text = settings.CaptureIntervalSeconds.ToString();
        TimeoutBox.Text = settings.RequestTimeoutSeconds.ToString();
        ScheduleEnabledBox.IsChecked = settings.DailyScheduleEnabled;
        RecordingStartBox.Text = settings.RecordingStartTime;
        RecordingEndBox.Text = settings.RecordingEndTime;
        SkipWhenIdleBox.IsChecked = settings.SkipWhenIdle;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApiKeyBox.Focus();
        SettingsScroll.ScrollToTop();
    }

    private void ResetCaptureDefaults_Click(object sender, RoutedEventArgs e) => SetCaptureFields(new TrackerSettings());

    private void SetPromptFields(TrackerSettings settings)
    {
        AnalysisPromptBox.Text = settings.AnalysisPrompt;
        ContextPrefixBox.Text = settings.PreviousContextPrefix;
    }

    private void ResetPrompts_Click(object sender, RoutedEventArgs e) => SetPromptFields(new TrackerSettings());

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IntervalBox.Text, out int interval) || !int.TryParse(TimeoutBox.Text, out int timeout))
        {
            CaptureSettingsExpander.IsExpanded = true;
            ErrorText.Text = "Gebruik hele getallen voor interval en timeout.";
            return;
        }
        var settings = new TrackerSettings
        {
            VisionEndpoint = EndpointBox.Text.Trim(),
            VisionApiKey = ApiKeyBox.Password.Trim(),
            VisionModel = ModelBox.Text.Trim(),
            WebhookUrl = WebhookBox.Text.Trim(),
            WebhookBearerToken = WebhookTokenBox.Password.Trim(),
            CaptureIntervalSeconds = interval,
            RequestTimeoutSeconds = timeout,
            SendScreenshots = SendScreenshotsBox.IsChecked == true,
            DailyScheduleEnabled = ScheduleEnabledBox.IsChecked == true,
            RecordingStartTime = RecordingStartBox.Text.Trim(),
            RecordingEndTime = RecordingEndBox.Text.Trim(),
            SkipWhenIdle = SkipWhenIdleBox.IsChecked == true,
            AnalysisPrompt = AnalysisPromptBox.Text,
            PreviousContextPrefix = ContextPrefixBox.Text
        };
        if (settings.ValidationError() is { } error)
        {
            CaptureSettingsExpander.IsExpanded = true;
            ErrorText.Text = error;
            return;
        }
        Settings = settings;
        DialogResult = true;
    }
}
