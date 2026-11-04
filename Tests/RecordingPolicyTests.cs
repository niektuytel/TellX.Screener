using System.IO;
using System.Net.Http;
using System.Threading.Channels;
using WorkflowTracker;
using WorkflowTracker.Models;
using WorkflowTracker.Services;

internal static partial class Program
{
    private static void RecordingPolicyUnitTests()
    {
        var settings = Settings();
        RecordingWindow At(string instant, TrackerSettings? config = null, TimeZoneInfo? zone = null) =>
            RecordingSchedule.Evaluate(config ?? settings, DateTimeOffset.Parse(instant), zone ?? TimeZoneInfo.Utc);

        Assert(settings.DailyScheduleEnabled && settings.SkipWhenIdle && settings.RecordingStartTime == "06:00" &&
            settings.RecordingEndTime == "18:00", "opnamevenster 06:00–18:00 en activiteitsfilter staan standaard aan");
        Assert(!At("2026-01-01T05:59:59Z").IsActive && At("2026-01-01T06:00:00Z").IsActive &&
            At("2026-01-01T17:59:59Z").IsActive && !At("2026-01-01T18:00:00Z").IsActive,
            "opnamevenster omvat 06:00 en pauzeert exact om 18:00");
        Assert(At("2026-01-01T18:00:00Z").NextChangeUtc == DateTimeOffset.Parse("2026-01-02T06:00:00Z"),
            "de eerstvolgende start na 18:00 ligt de volgende ochtend");
        var overnight = settings with { RecordingStartTime = "18:00", RecordingEndTime = "06:00" };
        Assert(At("2026-01-01T23:00:00Z", overnight).IsActive && At("2026-01-02T05:00:00Z", overnight).IsActive &&
            !At("2026-01-02T06:00:00Z", overnight).IsActive, "aangepast opnamevenster over middernacht werkt");
        var disabled = At("2026-01-01T23:00:00Z", settings with { DailyScheduleEnabled = false });
        Assert(disabled.IsActive && disabled.NextChangeUtc is null, "uitschakelen van het tijdschema geeft geen automatische pauze");
        var manual = RecordingSchedule.Evaluate(settings, DateTimeOffset.Parse("2026-01-01T19:00:00Z"), TimeZoneInfo.Utc,
            manualOverride: true);
        Assert(manual.IsActive && manual.NextChangeUtc is null && settings.DailyScheduleEnabled,
            "handmatig Play buiten uren negeert het dagvenster voor de sessie zonder instellingen te wijzigen");

        var dutchZone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        Assert(At("2026-01-01T05:00:00Z", zone: dutchZone).IsActive &&
            !At("2026-01-01T17:00:00Z", zone: dutchZone).IsActive &&
            At("2026-07-01T04:00:00Z", zone: dutchZone).IsActive &&
            !At("2026-07-01T16:00:00Z", zone: dutchZone).IsActive,
            "06:00 en 18:00 volgen lokale Windows-tijd in winter en zomer");
        Assert(At("2026-03-28T18:00:00Z", zone: dutchZone).NextChangeUtc == DateTimeOffset.Parse("2026-03-29T04:00:00Z") &&
            At("2026-10-24T18:00:00Z", zone: dutchZone).NextChangeUtc == DateTimeOffset.Parse("2026-10-25T05:00:00Z"),
            "de volgende ochtend houdt rekening met de overgang naar zomer- en wintertijd");
        foreach (var invalid in new[] { settings with { RecordingStartTime = "6:00" }, settings with { RecordingEndTime = "24:00" },
                     settings with { RecordingEndTime = "06:00" }, settings with { RecordingStartTime = "" } })
            Assert(invalid.ValidationError() is not null, "ongeldig of leeg opnamevenster wordt geweigerd");

        var old = SettingsStore.DeserializeSettings(System.Text.Encoding.UTF8.GetBytes(
            "{\"VisionApiKey\":\"fake-key\"}"));
        Assert(old.DailyScheduleEnabled && old.SkipWhenIdle && old.RecordingStartTime == "06:00" && old.RecordingEndTime == "18:00",
            "ook bestaande instellingen krijgen automatisch het dagvenster en activiteitsfilter");
        var patch = ParseReply("{\"settings\":{\"DailyScheduleEnabled\":false,\"RecordingStartTime\":\"07:30\",\"RecordingEndTime\":\"19:00\",\"SkipWhenIdle\":false}}").SettingsPatch!;
        var updated = RemoteSettingsUpdater.Apply(settings, patch, _ => { }).Settings;
        Assert(!updated.DailyScheduleEnabled && !updated.SkipWhenIdle && updated.RecordingStartTime == "07:30" &&
            updated.RecordingEndTime == "19:00" && updated.WebhookUrl == settings.WebhookUrl,
            "opnamevenster en activiteitsfilter zijn via toegestane webhookvelden instelbaar");
        Assert(SettingsStore.DeserializeSettings(SettingsStore.SerializeSettings(updated)) == updated,
            "aangepaste tijden en uitgeschakelde filters blijven bij herladen behouden");
        foreach (var json in new[] { "{\"SkipWhenIdle\":\"false\"}", "{\"DailyScheduleEnabled\":null}", "{\"RecordingStartTime\":600}" })
            Assert(ParseReply("{\"settings\":" + json + "}").SettingsError is not null,
                "nieuwe remote opnamevelden hebben strikte JSON-types");
        ExpectThrows<ArgumentException>(() => RemoteSettingsUpdater.Apply(settings,
            ParseReply("{\"settings\":{\"RecordingEndTime\":\"06:00\"}}").SettingsPatch!, _ => throw new Exception("mag niet opslaan")),
            "een remote tijdwijziging valideert het samengevoegde venster vóór opslag");

        var dialog = new SettingsWindow(settings);
        Assert(((System.Windows.Controls.Expander)dialog.FindName("CaptureSettingsExpander")).IsExpanded == false &&
            ((System.Windows.Controls.CheckBox)dialog.FindName("ScheduleEnabledBox")).IsChecked == true &&
            ((System.Windows.Controls.CheckBox)dialog.FindName("SkipWhenIdleBox")).IsChecked == true &&
            ((System.Windows.Controls.TextBox)dialog.FindName("RecordingStartBox")).Text == "06:00",
            "nieuwe opname-instellingen laden onder de ingeklapte geavanceerde opties");
    }

    private static async Task RecordingPolicyIntegrationAsync()
    {
        await DailyScheduleAsync();
        await ScheduleCancelsRequestAsync(blockWebhook: false);
        await ScheduleCancelsRequestAsync(blockWebhook: true);
        await IdleCaptureAsync();
        await IdleRetryAsync();
        await ActivityStillDeduplicatesAsync();
        await RemoteSchedulePausesAsync();
        await InputDuringAnalysisAsync();
        await ResumeAfterSleepingThroughNightAsync();
        await ManualPlayOutsideHoursAsync();
    }

    private static async Task DailyScheduleAsync()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T05:59:50Z"));
        var capture = new FakeCapture();
        var webhook = new FakeWebhook();
        var statuses = new StatusProbe();
        using var stop = new CancellationTokenSource();
        var worker = new CaptureWorker(capture, new FakeVision(_ => Task.FromResult<WorkflowResult?>(Result)), webhook,
            reportStatus: statuses.Add, timeProvider: clock, getSettings: Settings, userActivity: new FakeActivity());
        var task = worker.RunAsync(stop.Token);
        try
        {
            await statuses.Next("Automatisch gepauzeerd");
            Assert(capture.Streams.Count == 0 && webhook.Calls == 0, "opstarten buiten het venster maakt geen screenshot en verstuurt niets");
            clock.Advance(TimeSpan.FromSeconds(9));
            Assert(capture.Streams.Count == 0, "automatisch hervatten gebeurt niet vóór 06:00");
            clock.Advance(TimeSpan.FromSeconds(1));
            await statuses.Next("Activiteit verstuurd");
            Assert(capture.Streams.Count == 1 && webhook.Calls == 1, "om 06:00 begint de eerste opname automatisch zonder nieuwe invoer");
            clock.Advance(TimeSpan.FromHours(12));
            await statuses.Next("Automatisch gepauzeerd");
            Assert(capture.Streams.Count == 1 && webhook.Calls == 1, "de tick om 18:00 maakt geen nieuwe screenshot");
            clock.Advance(TimeSpan.FromHours(12));
            await statuses.Next("Activiteit verstuurd");
            Assert(capture.Streams.Count == 2 && webhook.Reports[1].PreviousObservations.Length == 0,
                "de volgende ochtend begint met een frisse rapportcontext en invoerbaseline");
            clock.Advance(TimeSpan.FromHours(12));
            await statuses.Next("Automatisch gepauzeerd");
            await StopTestWorker(task, stop);
            clock.Advance(TimeSpan.FromHours(12));
            Assert(capture.Streams.Count == 2, "stoppen van een wachtende worker voorkomt automatisch hervatten");
        }
        finally { await StopTestWorker(task, stop); }
    }

    private static async Task ScheduleCancelsRequestAsync(bool blockWebhook)
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T17:59:50Z"));
        var statuses = new StatusProbe();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = new FakeCapture();
        var webhook = new BlockingWebhook(started, blockWebhook);
        using var stop = new CancellationTokenSource();
        var settings = Settings() with { CaptureIntervalSeconds = 3600 };
        var worker = new CaptureWorker(capture, new FakeVision(async token =>
        {
            if (!blockWebhook)
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
            return Result;
        }), webhook, reportStatus: statuses.Add, timeProvider: clock, getSettings: () => settings);
        var task = worker.RunAsync(stop.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            clock.Advance(TimeSpan.FromSeconds(10));
            await statuses.Next("Automatisch gepauzeerd");
            Assert(capture.Streams.Count == 1 && !webhook.Completed && !capture.Streams[0].CanRead,
                blockWebhook ? "18:00 annuleert een lopende webhookupload en geeft de screenshot vrij"
                    : "18:00 annuleert een trage AI-call, ook bij een interval van een uur");
        }
        finally { await StopTestWorker(task, stop); }
    }

    private static async Task IdleCaptureAsync()
    {
        var clock = new ManualTimeProvider();
        var input = new FakeActivity();
        var capture = new FakeCapture();
        var webhook = new FakeWebhook();
        var statuses = new StatusProbe();
        var settings = Settings();
        using var stop = new CancellationTokenSource();
        var worker = new CaptureWorker(capture, new FakeVision(_ => Task.FromResult<WorkflowResult?>(Result)), webhook,
            reportStatus: statuses.Add, timeProvider: clock, getSettings: () => settings, userActivity: input);
        var task = worker.RunAsync(stop.Token);
        try
        {
            await statuses.Next("Activiteit verstuurd");
            clock.Advance(TimeSpan.FromSeconds(20));
            await statuses.Next("Geen muis-");
            Assert(capture.Streams.Count == 1 && webhook.Calls == 1,
                "geen nieuwe invoer slaat de screenshot zelf, AI en webhook over");
            input.Tick++;
            clock.Advance(TimeSpan.FromSeconds(20));
            await statuses.Next("Activiteit verstuurd");
            Assert(capture.Streams.Count == 2 && webhook.Calls == 2 && webhook.Reports[1].PreviousObservations.Length == 1,
                "nieuwe muis- of toetsactiviteit hervat opname met behoud van rapportcontext");
            input.Tick = null;
            clock.Advance(TimeSpan.FromSeconds(20));
            await statuses.Next("Activiteit verstuurd");
            Assert(capture.Streams.Count == 3, "een onbeschikbaar Windows-invoertijdstip blokkeert opnames niet");
            input.Tick = 123;
            settings = settings with { SkipWhenIdle = false };
            clock.Advance(TimeSpan.FromSeconds(20));
            await statuses.Next("Activiteit verstuurd");
            clock.Advance(TimeSpan.FromSeconds(20));
            await statuses.Next("Activiteit verstuurd");
            Assert(capture.Streams.Count == 5, "uitgeschakeld activiteitsfilter laat achtergrondveranderingen opnieuw beoordelen");
        }
        finally { await StopTestWorker(task, stop); }
    }

    private static async Task IdleRetryAsync()
    {
        var clock = new ManualTimeProvider();
        var statuses = new StatusProbe();
        var capture = new FakeCapture();
        int attempts = 0;
        using var stop = new CancellationTokenSource();
        var worker = new CaptureWorker(capture, new FakeVision(_ => ++attempts == 1
            ? Task.FromException<WorkflowResult?>(new HttpRequestException("fake")) : Task.FromResult<WorkflowResult?>(Result)),
            new FakeWebhook(), reportStatus: statuses.Add, timeProvider: clock, getSettings: Settings, userActivity: new FakeActivity());
        var task = worker.RunAsync(stop.Token);
        try
        {
            await statuses.Next("Verwerking mislukt");
            clock.Advance(TimeSpan.FromSeconds(20));
            await statuses.Next("Activiteit verstuurd");
            Assert(attempts == 2 && capture.Streams.Count == 2, "een AI-fout wordt ook zonder nieuwe invoer opnieuw geprobeerd");
            clock.Advance(TimeSpan.FromSeconds(20));
            await statuses.Next("Geen muis-");
            Assert(attempts == 2, "na een geslaagde retry geldt het activiteitsfilter weer");
        }
        finally { await StopTestWorker(task, stop); }
    }

    private static async Task ActivityStillDeduplicatesAsync()
    {
        var clock = new ManualTimeProvider();
        var input = new FakeActivity();
        var statuses = new StatusProbe();
        int analyses = 0;
        using var stop = new CancellationTokenSource();
        var capture = new SequenceCapture([Png, Png], stop);
        var worker = new CaptureWorker(capture, new FakeVision(_ => { analyses++; return Task.FromResult<WorkflowResult?>(Result); }),
            new FakeWebhook(), reportStatus: statuses.Add, timeProvider: clock, getSettings: Settings, userActivity: input);
        var task = worker.RunAsync(stop.Token);
        try
        {
            await statuses.Next("Activiteit verstuurd");
            input.Tick++;
            clock.Advance(TimeSpan.FromSeconds(20));
            await statuses.Next("Scherm ongewijzigd");
            Assert(capture.Streams.Count == 2 && analyses == 1, "nieuwe invoer omzeilt de bestaande vergelijking van identieke beelden niet");
        }
        finally { await StopTestWorker(task, stop); }
    }

    private static async Task StopTestWorker(Task task, CancellationTokenSource stop)
    {
        stop.Cancel();
        try { await task.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private static async Task RemoteSchedulePausesAsync()
    {
        var clock = new ManualTimeProvider();
        var settings = Settings();
        var statuses = new StatusProbe();
        var capture = new FakeCapture();
        var webhook = new FakeWebhook(result: ParseReply("{\"settings\":{\"RecordingEndTime\":\"09:00\"}}"));
        using var stop = new CancellationTokenSource();
        var worker = new CaptureWorker(capture, new FakeVision(_ => Task.FromResult<WorkflowResult?>(Result)), webhook,
            reportStatus: statuses.Add, timeProvider: clock, getSettings: () => settings, userActivity: new FakeActivity(),
            applyRemoteSettings: patch =>
            {
                var update = RemoteSettingsUpdater.Apply(settings, patch, _ => { });
                settings = update.Settings;
                return update;
            });
        var task = worker.RunAsync(stop.Token);
        try
        {
            await statuses.Next("Automatisch gepauzeerd");
            Assert(capture.Streams.Count == 1 && settings.RecordingEndTime == "09:00",
                "een remote tijdwijziging buiten het huidige venster pauzeert meteen zonder nieuwe capture");
            clock.Advance(TimeSpan.FromHours(20));
            await statuses.Next("Activiteit verstuurd");
            Assert(capture.Streams.Count == 2 && webhook.Reports[1].PreviousObservations.Length == 0,
                "na een remote tijdwijziging blijft hervatten de volgende ochtend werken");
        }
        finally { await StopTestWorker(task, stop); }
    }

    private static async Task InputDuringAnalysisAsync()
    {
        var clock = new ManualTimeProvider();
        var input = new FakeActivity();
        var statuses = new StatusProbe();
        int analyses = 0;
        using var stop = new CancellationTokenSource();
        var worker = new CaptureWorker(new FakeCapture(), new FakeVision(_ =>
        {
            if (++analyses == 1) input.Tick++; // Invoer terwijl het screenshot wordt verwerkt.
            return Task.FromResult<WorkflowResult?>(Result);
        }), new FakeWebhook(), reportStatus: statuses.Add, timeProvider: clock, getSettings: Settings, userActivity: input);
        var task = worker.RunAsync(stop.Token);
        try
        {
            await statuses.Next("Activiteit verstuurd");
            clock.Advance(TimeSpan.FromSeconds(20));
            await statuses.Next("Activiteit verstuurd");
            Assert(analyses == 2, "invoer tijdens een lopende analyse blijft aanleiding voor de volgende opname");
        }
        finally { await StopTestWorker(task, stop); }
    }

    private static async Task ResumeAfterSleepingThroughNightAsync()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T17:00:00Z"));
        var statuses = new StatusProbe();
        var capture = new FakeCapture();
        var webhook = new FakeWebhook();
        using var stop = new CancellationTokenSource();
        var worker = new CaptureWorker(capture, new FakeVision(_ => Task.FromResult<WorkflowResult?>(Result)), webhook,
            reportStatus: statuses.Add, timeProvider: clock, getSettings: Settings, userActivity: new FakeActivity());
        var task = worker.RunAsync(stop.Token);
        try
        {
            await statuses.Next("Activiteit verstuurd");
            clock.Advance(TimeSpan.FromHours(15)); // Geen enkele tick tijdens de nacht: hervatten om 08:00.
            await statuses.Next("Activiteit verstuurd");
            Assert(capture.Streams.Count == 2 && webhook.Reports[1].PreviousObservations.Length == 0,
                "ook na doorslapen van de nacht volgt een verse ochtendopname zonder context van gisteren");
        }
        finally { await StopTestWorker(task, stop); }
    }

    private static async Task ManualPlayOutsideHoursAsync()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T19:00:00Z"));
        var settings = Settings() with { SkipWhenIdle = false };
        var statuses = new StatusProbe();
        var capture = new FakeCapture();
        var states = new List<bool>();
        var webhook = new FakeWebhook(result: ParseReply("{\"settings\":{\"RecordingEndTime\":\"07:00\",\"CaptureIntervalSeconds\":5}}"));
        using var stop = new CancellationTokenSource();
        var worker = new CaptureWorker(capture, new FakeVision(_ => Task.FromResult<WorkflowResult?>(Result)), webhook,
            reportStatus: statuses.Add, timeProvider: clock, getSettings: () => settings,
            reportAutomaticPause: states.Add, manualScheduleOverride: true,
            applyRemoteSettings: patch =>
            {
                var update = RemoteSettingsUpdater.Apply(settings, patch, _ => { });
                settings = update.Settings;
                return update;
            });
        var task = worker.RunAsync(stop.Token);
        try
        {
            await statuses.Next("Instellingen via webhook bijgewerkt");
            Assert(capture.Streams.Count == 1 && webhook.Calls == 1,
                "handmatig Play om 19:00 maakt direct een opname ondanks het dagvenster");
            clock.Advance(TimeSpan.FromSeconds(5));
            await statuses.Next("Activiteit verstuurd");
            Assert(webhook.Calls == 2 && settings.DailyScheduleEnabled && settings.RecordingEndTime == "07:00",
                "remote instellingen blijven toepasbaar maar heffen de handmatige sessie niet op");
            clock.Advance(TimeSpan.FromHours(11));
            await statuses.Next("Activiteit verstuurd");
            Assert(webhook.Reports[2].PreviousObservations.Length == 2,
                "de handmatige avondsessie behoudt zijn rapportcontext de volgende ochtend");
            clock.Advance(TimeSpan.FromHours(12));
            await statuses.Next("Activiteit verstuurd");
            Assert(webhook.Calls == 4 && states.SequenceEqual([false]),
                "de handmatige sessie blijft ook voorbij de volgende eindtijd actief tot Pauze");
            await StopTestWorker(task, stop);
            clock.Advance(TimeSpan.FromMinutes(1));
            Assert(capture.Streams.Count == 4 && capture.Streams.All(stream => !stream.CanRead),
                "Pauze stopt de handmatige sessie en geeft alle screenshots vrij");
        }
        finally { await StopTestWorker(task, stop); }

        // Een nieuwe app-/Play-sessie zonder handmatige override volgt opnieuw het opgeslagen dagvenster.
        var startupStatuses = new StatusProbe();
        using var startupStop = new CancellationTokenSource();
        var startupWorker = new CaptureWorker(capture, new FakeVision(_ => Task.FromResult<WorkflowResult?>(Result)), webhook,
            reportStatus: startupStatuses.Add, timeProvider: clock, getSettings: () => settings);
        var startupTask = startupWorker.RunAsync(startupStop.Token);
        try
        {
            await startupStatuses.Next("Automatisch gepauzeerd");
            Assert(capture.Streams.Count == 4,
                "een volgende automatische start buiten uren volgt opnieuw het dagvenster zonder blijvende override");
        }
        finally { await StopTestWorker(startupTask, startupStop); }
    }

    private sealed class StatusProbe
    {
        private readonly Channel<string> _statuses = Channel.CreateUnbounded<string>();
        public void Add(string status) => _statuses.Writer.TryWrite(status);
        public async Task Next(string prefix)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (!(await _statuses.Reader.ReadAsync(timeout.Token)).StartsWith(prefix, StringComparison.Ordinal)) { }
        }
    }

    private sealed class FakeActivity : IUserActivity
    {
        public uint? Tick { get; set; } = 1;
        public uint? GetLastInputTick() => Tick;
    }

    private sealed class BlockingWebhook(TaskCompletionSource started, bool block) : IWebhookClient
    {
        public bool Completed { get; private set; }
        public async Task<WebhookResult> SendAsync(WorkflowResult workflow, MemoryStream screenshot, CancellationToken token)
        {
            started.TrySetResult();
            if (block) await Task.Delay(Timeout.Infinite, token);
            Completed = true;
            return WebhookResult.Accepted;
        }
    }
}
