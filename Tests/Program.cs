using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using WorkflowTracker;
using WorkflowTracker.Models;
using WorkflowTracker.Services;

internal static partial class Program
{
    private static readonly WorkflowResult Result = new("Een build maken", ["IDE"], ["Build gestart"])
    {
        ScreenshotText = "Build started...",
        Activity = "De gebruiker bouwt waarschijnlijk het project.",
        Report = "De gebruiker heeft het project geopend en vervolgens een build gestart. Het resultaat is nog niet zichtbaar.",
        Completed = false,
        CapturedAtUtc = DateTimeOffset.Parse("2026-01-01T10:01:20Z")
    };
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
    private static int _passed;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--uninstall-target", var readyName, var stopName])
        {
            using var ready = EventWaitHandle.OpenExisting(readyName);
            using var stop = EventWaitHandle.OpenExisting(stopName);
            ready.Set();
            return stop.WaitOne(TimeSpan.FromSeconds(45)) ? 0 : 70;
        }
        try
        {
            // Alleen XAML instantiëren: geen Show, startup, registry of echte screenshots.
            var window = new MainWindow();
            var settingsWindow = new SettingsWindow(Settings());
            Assert(window.Topmost && window.AllowsTransparency && window.Width == window.Height,
                "WPF overlay en instellingen-XAML laden");
            Assert(!window.ShowInTaskbar && !window.ShowActivated && !settingsWindow.ShowInTaskbar,
                "overlay start zonder focusovername; overlay en instellingen hebben geen taakbalkknop");
            var handle = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            long extendedStyle = OverlayWindowPolicy.ReadStyle(handle);
            Assert((extendedStyle & OverlayWindowPolicy.ToolWindow) != 0 && (extendedStyle & OverlayWindowPolicy.AppWindow) == 0,
                "native overlay heeft toolwindow-stijl zonder geforceerde taakbalkknop");
            window = null!;
            settingsWindow = null!;

            Assert(WorkflowResult.Parse("null") is null, "JSON null");
            var fenced = WorkflowResult.Parse("```json\n" + JsonSerializer.Serialize(Result) + "\n```");
            Assert(fenced?.Doel == Result.Doel && fenced.Applicaties.SequenceEqual(Result.Applicaties) &&
                fenced.Stappenplan.SequenceEqual(Result.Stappenplan), "JSON/code fence");
            ExpectThrows<JsonException>(() => WorkflowResult.Parse("{\"doel\":\"x\"}"), "onvolledige JSON weigeren");
            ExpectThrows<JsonException>(() => WorkflowResult.Parse("{\"doel\":\"x\",\"applicaties\":[1],\"stappenplan\":[]}"),
                "ongeldige arrays weigeren");
            Assert((Settings() with { VisionEndpoint = "http://example.invalid" }).ValidationError() is not null,
                "HTTPS-validatie");
            Assert(new TrackerSettings().ValidationError()?.Contains("Google Gemini API-key") == true,
                "Google-defaults starten geen opnames zonder API-key");
            Assert((new TrackerSettings() with { VisionApiKey = "fake-google-key" }).ValidationError() is null,
                "Google-defaults zijn compleet na invullen van de API-key");
            Assert(SettingsStore.UpgradePlaceholders(Settings()) == Settings(),
                "bestaande eigen endpointgegevens blijven behouden");
            var oldPlaceholders = new TrackerSettings
            {
                VisionEndpoint = "https://jouw-vision-api.invalid/v1/chat/completions",
                VisionModel = "jouw-vision-model",
                WebhookUrl = "https://jouw-webhook.invalid/workflows"
            };
            Assert(SettingsStore.UpgradePlaceholders(oldPlaceholders) == new TrackerSettings(),
                "oude voorbeeldgegevens worden bijgewerkt naar Google en de ingestelde webhook");
            Assert(Settings().ValidationError() is null, "ingevulde configuratie is geldig");
            Assert((Settings() with { CaptureIntervalSeconds = 4 }).ValidationError() is not null &&
                (Settings() with { RequestTimeoutSeconds = 601 }).ValidationError() is not null,
                "grenzen voor instelbare opnametijden");
            Assert((Settings() with { AnalysisPrompt = " \n " }).ValidationError() is not null &&
                (Settings() with { AnalysisPrompt = new string('x', TrackerSettings.MaxAnalysisPromptLength + 1) }).ValidationError() is not null &&
                (Settings() with { PreviousContextPrefix = new string('x', TrackerSettings.MaxPreviousContextPrefixLength + 1) }).ValidationError() is not null,
                "lege en te lange prompts worden geweigerd");
            var customized = Settings() with { AnalysisPrompt = "Mijn analyse\nmet meerdere regels.", PreviousContextPrefix = "Vorige opnamen:\n" };
            Assert(JsonSerializer.Deserialize<TrackerSettings>(JsonSerializer.Serialize(customized)) == customized,
                "eigen prompt en prefix blijven exact behouden bij opslag");
            var migrated = JsonSerializer.Deserialize<TrackerSettings>(
                "{\"VisionEndpoint\":\"https://vision.example.test\",\"VisionModel\":\"vision-model\",\"WebhookUrl\":\"https://webhook.example.test\"}")!;
            Assert(migrated.CaptureIntervalSeconds == 20 && migrated.RequestTimeoutSeconds == 90 &&
                !migrated.SendScreenshots,
                "ook bij oude instellingen staan webhook-screenshots standaard uit");
            Assert(migrated.AnalysisPrompt == TrackerSettings.DefaultAnalysisPrompt &&
                migrated.PreviousContextPrefix == TrackerSettings.DefaultPreviousContextPrefix,
                "bestaande instellingen krijgen de standaardprompt en het standaardprefix");
            var oldPrompts = Settings() with { AnalysisPrompt = SettingsStore.LegacyAnalysisPrompt,
                PreviousContextPrefix = SettingsStore.LegacyContextPrefix };
            Assert(SettingsStore.UpgradePlaceholders(oldPrompts).AnalysisPrompt == TrackerSettings.DefaultAnalysisPrompt &&
                SettingsStore.UpgradePlaceholders(oldPrompts).PreviousContextPrefix == TrackerSettings.DefaultPreviousContextPrefix,
                "oude standaardprompts krijgen de doorlopende rapportinstructies");
            using (var defaultsResource = typeof(SettingsStore).Assembly.GetManifestResourceStream(
                "WorkflowTracker.workflowtracker.defaults.json")!)
            {
                var defaults = JsonSerializer.Deserialize<TrackerSettings>(defaultsResource)!;
                Assert(defaults == new TrackerSettings(), "ingebouwde JSON-defaults en C#-defaults komen overeen");
            }
            TextHistoryTests();
            WebhookCommandTests();
            RemoteSettingsUnitTests();
            RecordingPolicyUnitTests();
            Assert(StartupRegistration.BuildCommand().StartsWith('"') && StartupRegistration.BuildCommand().EndsWith('"'),
                "autostartcommando quote het exe-pad (zonder registry-writes)");

            RunAsync().GetAwaiter().GetResult();
            Console.WriteLine($"OK: {_passed} controles geslaagd. Geen echte screenshots, registry-writes of netwerkrequests.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task RunAsync()
    {
        await VisionAndWebhookAsync();
        await VisionHistoryAsync();
        await RemoteSettingsIntegrationAsync();
        await RecordingPolicyIntegrationAsync();
        await DuplicateCaptureTestsAsync();
        await LoopAsync();
        await UninstallCleanupAsync();
    }

    private static void WebhookCommandTests()
    {
        WebhookResult Parse(string body, HttpStatusCode code = HttpStatusCode.OK, string type = "application/json")
            => WebhookResponse.Parse(code, type, Encoding.UTF8.GetBytes(body));
        const string uninstall = "{\"action\":\"uninstall\",\"confirm\":\"WorkflowTracker\"}";
        Assert(Parse(uninstall) == WebhookResult.UninstallRequested &&
            Parse(" { \"confirm\": \"WorkflowTracker\", \"action\": \"uninstall\" } ") == WebhookResult.UninstallRequested,
            "alleen het afgesproken JSON-object activeert verwijdering");
        foreach (string body in new[]
        {
            "", "null", "uninstall", "{\"action\":\"uninstall\"}",
            "{\"action\":\"uninstall\",\"confirm\":\"AnderProgramma\"}",
            "{\"action\":\"UNINSTALL\",\"confirm\":\"WorkflowTracker\"}",
            "{\"action\":\"uninstall\",\"confirm\":\"WorkflowTracker\",\"path\":\"C:\\\\Users\"}",
            "{\"action\":\"uninstall\",\"action\":\"uninstall\"}",
            "{\"action\":\"uninstall\",\"confirm\":\"WorkflowTracker\",\"confirm\":\"WorkflowTracker\"}",
            "{\"action\":\"uninstall\",\"confirm\":true}"
        }) Assert(Parse(body) == WebhookResult.Accepted, "onjuist of uitgebreid verwijdercommando genegeerd");
        Assert(Parse(uninstall, HttpStatusCode.Accepted) == WebhookResult.Accepted &&
            Parse(uninstall, HttpStatusCode.Gone) == WebhookResult.Accepted &&
            Parse(uninstall, type: "text/html") == WebhookResult.Accepted &&
            Parse(uninstall + new string(' ', WebhookResponse.MaxCommandBytes)) == WebhookResult.Accepted,
            "andere status, mediatype of te grote respons verwijdert niets");
    }

    private static async Task UninstallCleanupAsync()
    {
        string sandbox = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WorkflowTracker.Tests." + Guid.NewGuid().ToString("N")));
        string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (!sandbox.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(sandbox).StartsWith("WorkflowTracker.Tests.", StringComparison.Ordinal))
            throw new InvalidOperationException("Testmap valt buiten de bedoelde tijdelijke map.");
        Directory.CreateDirectory(sandbox);
        Process? target = null;
        Process? helper = null;
        try
        {
            string targetDirectory = Path.Combine(sandbox, "target '[$]");
            Directory.CreateDirectory(targetDirectory);
            foreach (string source in Directory.EnumerateFiles(AppContext.BaseDirectory))
                File.Copy(source, Path.Combine(targetDirectory, Path.GetFileName(source)));
            string targetExe = Path.Combine(targetDirectory, Path.GetFileName(Environment.ProcessPath!));
            string dataRoot = Path.Combine(sandbox, "WorkflowTracker");
            string logs = Path.Combine(dataRoot, "logs");
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(dataRoot, "settings.dat"), "dummy settings");
            File.WriteAllText(Path.Combine(dataRoot, "settings.dat.tmp"), "dummy pending settings");
            File.WriteAllText(Path.Combine(logs, "tracker-test.log"), "dummy log");
            File.WriteAllText(Path.Combine(logs, "keep.txt"), "user file");
            File.WriteAllText(Path.Combine(sandbox, "outside.txt"), "unrelated file");

            string prefix = @"Local\WorkflowTracker.Uninstall." + Guid.NewGuid().ToString("N");
            using var targetReady = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + ".TargetReady");
            using var targetStop = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + ".TargetStop");
            using var helperReady = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + ".Ready");
            using var proceed = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + ".Proceed");
            using var success = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + ".Success");
            var targetStart = new ProcessStartInfo(targetExe) { UseShellExecute = false, CreateNoWindow = true };
            targetStart.ArgumentList.Add("--uninstall-target");
            targetStart.ArgumentList.Add(prefix + ".TargetReady");
            targetStart.ArgumentList.Add(prefix + ".TargetStop");
            target = Process.Start(targetStart)!;
            Assert(await Task.Run(() => targetReady.WaitOne(TimeSpan.FromSeconds(10))), "geïsoleerd testproces gestart");
            var plan = new UninstallPlan(target.Id, target.StartTime.ToUniversalTime().Ticks.ToString(),
                targetExe, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(targetExe))), dataRoot,
                prefix + ".Ready", prefix + ".Proceed", prefix + ".Success");
            var start = UninstallCoordinator.BuildHelperStartInfo(plan);
            start.Environment["LOCALAPPDATA"] = sandbox;
            start.Environment["TEMP"] = sandbox;
            start.Environment["TMP"] = sandbox;
            start.RedirectStandardError = true;
            helper = Process.Start(start)!;
            if (!await Task.Run(() => helperReady.WaitOne(TimeSpan.FromSeconds(12))))
                throw new InvalidOperationException("Cleanup niet voorbereid: " +
                    (helper.HasExited ? await helper.StandardError.ReadToEndAsync() : "helper bleef wachten"));
            Assert(File.Exists(targetExe) && File.Exists(Path.Combine(dataRoot, "settings.dat")),
                "helper verwijdert niets vóór toestemming en procesafsluiting");
            proceed.Set();
            targetStop.Set();
            await target.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            try { await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (TimeoutException)
            {
                string log = Path.Combine(sandbox, "WorkflowTracker-uninstall-error.log");
                throw new InvalidOperationException("Cleanup bleef actief: " + (File.Exists(log) ? File.ReadAllText(log) : "geen log"));
            }
            Assert(helper.ExitCode == 0 && success.WaitOne(0), "cleanuphelper voltooit het verwijderplan");
            Assert(!File.Exists(targetExe) && !File.Exists(Path.Combine(dataRoot, "settings.dat")) &&
                !File.Exists(Path.Combine(dataRoot, "settings.dat.tmp")) && !File.Exists(Path.Combine(logs, "tracker-test.log")),
                "alleen sandbox-exe, instellingen en app-log verwijderd");
            Assert(File.Exists(Path.Combine(logs, "keep.txt")) && File.Exists(Path.Combine(sandbox, "outside.txt")) &&
                Directory.Exists(targetDirectory), "andere bestanden en de exe-map blijven behouden");

            // Een onjuist gegevenspad wordt geweigerd vóór er verwijdertoestemming komt.
            var invalidStart = UninstallCoordinator.BuildHelperStartInfo(plan with { DataRoot = sandbox });
            invalidStart.Environment["LOCALAPPDATA"] = sandbox;
            using var invalid = Process.Start(invalidStart)!;
            await invalid.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert(invalid.ExitCode != 0 && File.Exists(Path.Combine(sandbox, "outside.txt")),
                "cleanup weigert een gegevenspad buiten de vaste appmap");
        }
        finally
        {
            if (helper is { HasExited: false }) helper.Kill();
            if (target is { HasExited: false }) target.Kill();
            if (helper is not null) { await helper.WaitForExitAsync(); helper.Dispose(); }
            if (target is not null) { await target.WaitForExitAsync(); target.Dispose(); }
            // De absolute sandbox is hierboven onder Temp geverifieerd; geen appgegevens buiten deze map.
            Directory.Delete(sandbox, recursive: true);
        }
    }

    private static void TextHistoryTests()
    {
        using var history = new TextHistory();
        Assert(history.Observations.Count == 0, "teksthistorie begint leeg");
        for (int i = 1; i <= 4; i++) history.Add(Result with { ScreenshotText = "screen-" + i });
        Assert(history.Observations.Select(x => x.ScreenshotText).SequenceEqual(new[] { "screen-3", "screen-4" }),
            "alleen de laatste twee schermteksten worden bewaard");
        Assert(history.Observations[1].Report == Result.Report &&
            history.Observations[1].Steps.SequenceEqual(Result.Stappenplan) &&
            history.Observations[1].Applications.SequenceEqual(Result.Applicaties) &&
            history.Observations[1].Completed == Result.Completed,
            "het volledige vorige resultaat inclusief rapport en stappen blijft beschikbaar");
        Assert(!JsonSerializer.Serialize(history.Observations).Contains("vorige_opnamen"),
            "rapportcontext groeit niet recursief met alle oudere resultaten");
        history.Dispose();
        Assert(history.Observations.Count == 0, "teksthistorie wordt vrijgegeven bij stoppen");
        var unreadableGoal = Result with { Doel = "", Applicaties = [], Stappenplan = [], ScreenshotText = "" };
        Assert(WorkflowResult.Parse(JsonSerializer.Serialize(unreadableGoal))?.Activity == Result.Activity,
            "lege schermtekst en een onbekend doel zijn toegestaan");
        ExpectThrows<JsonException>(() => WorkflowResult.Parse(JsonSerializer.Serialize(Result with
            { ScreenshotText = new string('x', WorkflowResult.MaxScreenshotTextLength + 1) })),
            "te lange schermtekst wordt geweigerd");
        ExpectThrows<JsonException>(() => WorkflowResult.Parse(JsonSerializer.Serialize(Result with
            { Report = new string('x', WorkflowResult.MaxReportLength + 1) })), "te lang doorlopend rapport wordt geweigerd");
    }

    private static async Task VisionHistoryAsync()
    {
        var settings = Settings() with
        {
            AnalysisPrompt = "Beschrijf de bewerking van de machinecode.\nGebruik de onderstaande context.",
            PreviousContextPrefix = "MIJN VORIGE OPNAMEN:"
        };
        using var http = new HttpClient(new FakeHandler(async (request, token) =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var parts = payload.RootElement.GetProperty("messages")[1].GetProperty("content");
            Assert(parts.EnumerateArray().Count(x => x.GetProperty("type").GetString() == "image_url") == 1,
                "Vision ontvangt alleen de huidige afbeelding");
            string text = parts[0].GetProperty("text").GetString()!;
            Assert(text.StartsWith(settings.AnalysisPrompt + "\n\n" + settings.PreviousContextPrefix + "\n", StringComparison.Ordinal),
                "bewerkbare analyseprompt en contextprefix worden werkelijk naar de API gestuurd");
            Assert(payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!
                .Contains("\"screenshot_tekst\"", StringComparison.Ordinal), "vaste JSON-instructies blijven apart aanwezig");
            Assert(!text.Contains("old-screen") && text.IndexOf("previous-one", StringComparison.Ordinal) <
                text.IndexOf("previous-two", StringComparison.Ordinal) && text.Contains("previous-two"),
                "Vision ontvangt exact twee eerdere teksten in chronologische volgorde");
            Assert(text.Contains("Dit is de volgende screenshot", StringComparison.Ordinal) &&
                text.Contains("voorgaand-samenhangend-rapport", StringComparison.Ordinal) &&
                text.Contains("vorige-handeling", StringComparison.Ordinal),
                "vervolgopname krijgt vorig rapport en vorige stappen met expliciete chronologische koppeling");
            return JsonResponse("{\"choices\":[{\"message\":{\"content\":\"null\"}}]}");
        }));
        using var screenshot = new MemoryStream(Png);
        await new VisionClient(http, settings).AnalyzeAsync(screenshot, CancellationToken.None,
        [
            new TextObservation(DateTimeOffset.Parse("2026-01-01T09:59:20Z"), "old-screen", "old", "old"),
            new TextObservation(DateTimeOffset.Parse("2026-01-01T10:00:00Z"), "previous-one", "Code schrijven", "Build maken"),
            new TextObservation(DateTimeOffset.Parse("2026-01-01T10:00:20Z"), "previous-two", "Build starten", "Build maken")
            { Report = "voorgaand-samenhangend-rapport", Steps = ["vorige-handeling"], Applications = ["IDE"] }
        ]);
    }

    private static async Task VisionAndWebhookAsync()
    {
        var settings = Settings() with
        {
            AnalysisPrompt = "Mijn aangepaste analyse zonder eerdere opnamen.",
            PreviousContextPrefix = "PREFIX_ALLEEN_BIJ_GESCHIEDENIS"
        };
        using var screenshot = new MemoryStream(Png);
        screenshot.Position = screenshot.Length;
        using var visionHttp = new HttpClient(new FakeHandler(async (request, token) =>
        {
            Assert(request.Headers.Authorization?.Parameter == "vision-key", "Vision-authenticatie");
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var root = payload.RootElement;
            Assert(root.GetProperty("model").GetString() == "vision-model", "model in vision-request");
            var parts = root.GetProperty("messages")[1].GetProperty("content");
            Assert(parts[0].GetProperty("text").GetString() == settings.AnalysisPrompt,
                "zonder geschiedenis wordt alleen de aangepaste analyseprompt gebruikt");
            Assert(parts[1].GetProperty("image_url").GetProperty("url").GetString() ==
                "data:image/png;base64," + Convert.ToBase64String(Png), "huidige PNG naar Vision");
            return JsonResponse(JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = JsonSerializer.Serialize(Result) } } }
            }));
        }));
        var workflow = await new VisionClient(visionHttp, settings).AnalyzeAsync(screenshot, CancellationToken.None);
        Assert(workflow is { Completed: false } && workflow.ScreenshotText == Result.ScreenshotText,
            "lopende activiteit wordt als geldige analyse verwerkt");

        foreach (bool includeImage in new[] { false, true })
        {
            using var http = new HttpClient(new FakeHandler(async (request, token) =>
            {
                Assert(request.Headers.Authorization?.Parameter == "webhook-key", "afzonderlijke webhook-authenticatie");
                var sections = ((MultipartFormDataContent)request.Content!).ToArray();
                Assert(sections.Length == (includeImage ? 2 : 1), includeImage
                    ? "aangevinkt: JSON en PNG naar webhook" : "standaard: uitsluitend JSON-tekst naar webhook");
                Assert(sections[0].Headers.ContentDisposition?.Name?.Trim('"') == "workflow" &&
                    sections[0].Headers.ContentType?.MediaType == "application/json", "workflow-part blijft JSON");
                string text = await sections[0].ReadAsStringAsync(token);
                using var data = JsonDocument.Parse(text);
                var root = data.RootElement;
                Assert(root.GetProperty("rapport").GetString() == Result.Report &&
                    root.GetProperty("screenshot_tekst").GetString() == Result.ScreenshotText &&
                    root.GetProperty("activiteit").GetString() == Result.Activity &&
                    !root.GetProperty("voltooid").GetBoolean() &&
                    root.GetProperty("vorige_opnamen").GetArrayLength() == 2,
                    "webhook bevat het doorlopende rapport, de huidige analyse en eerdere resultaten");
                Assert(!text.Contains(Convert.ToBase64String(Png)), "geen afbeelding of base64 in de JSON");
                if (includeImage)
                    Assert(sections[1].Headers.ContentDisposition?.Name?.Trim('"') == "screenshot" &&
                        sections[1].Headers.ContentType?.MediaType == "image/png" &&
                        (await sections[1].ReadAsByteArrayAsync(token)).SequenceEqual(Png), "optionele ongewijzigde PNG-part");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }));
            var report = Result with { PreviousObservations =
            [
                new TextObservation(Result.CapturedAtUtc.AddSeconds(-80), "Code open", "Bewerken", Result.Doel),
                new TextObservation(Result.CapturedAtUtc.AddSeconds(-40), "Build gestart", "Bouwen", Result.Doel)
            ] };
            await new WebhookClient(http, Settings() with { SendScreenshots = includeImage })
                .SendAsync(report, screenshot, CancellationToken.None);
            Assert(screenshot.CanRead, "screenshotstream blijft van aanroeper");
        }
        using var nullHttp = new HttpClient(new FakeHandler((_, _) => Task.FromResult(JsonResponse(
            "{\"choices\":[{\"message\":{\"content\":\"null\"}}]}"))));
        Assert(await new VisionClient(nullHttp, Settings()).AnalyzeAsync(screenshot, CancellationToken.None) is null,
            "onleesbare opname kan null opleveren");
        using var errorHttp = new HttpClient(new FakeHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Unauthorized))));
        await ExpectThrowsAsync<HttpRequestException>(
            () => new VisionClient(errorHttp, Settings()).AnalyzeAsync(screenshot, CancellationToken.None), "HTTP-fout wordt doorgegeven");
        using var uninstallHttp = new HttpClient(new FakeHandler((_, _) => Task.FromResult(
            JsonResponse("{\"action\":\"uninstall\",\"confirm\":\"WorkflowTracker\"}"))));
        Assert(await new WebhookClient(uninstallHttp, Settings()).SendAsync(Result, screenshot, CancellationToken.None)
            == WebhookResult.UninstallRequested, "verwijderrespons blijft werken bij tekst-only uploads");
    }

    private static async Task DuplicateCaptureTestsAsync()
    {
        byte[] first = MakePng(1);
        byte[] second = MakePng(2);
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            int analyses = 0;
            int skipped = 0;
            var capture = new SequenceCapture([first, first, second, second, first], cts);
            var webhook = new FakeWebhook();
            var vision = new FakeVision(_ => { analyses++; return Task.FromResult<WorkflowResult?>(Result); });
            await FinishLoop(new CaptureWorker(capture, vision, webhook,
                reportStatus: status => { if (status.Contains("overgeslagen")) skipped++; },
                interval: TimeSpan.FromMilliseconds(5)), cts.Token);
            Assert(analyses == 3 && webhook.Calls == 3 && skipped == 2,
                "opeenvolgende identieke screenshots slaan zowel AI als webhook over; A-B-A wordt wel geanalyseerd");
            Assert(capture.Streams.All(x => !x.CanRead), "ook overgeslagen screenshotstreams worden gedisposed");
        }
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            int analyses = 0;
            var capture = new SequenceCapture([first, first, first], cts);
            var webhook = new FakeWebhook();
            var vision = new FakeVision(_ => ++analyses == 1
                ? Task.FromException<WorkflowResult?>(new HttpRequestException("fake API-fout"))
                : Task.FromResult<WorkflowResult?>(Result));
            await FinishLoop(new CaptureWorker(capture, vision, webhook, interval: TimeSpan.FromMilliseconds(5)), cts.Token);
            Assert(analyses == 2 && webhook.Calls == 1, "na een API-fout wordt een identieke screenshot opnieuw geprobeerd");
        }
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            int analyses = 0;
            var capture = new SequenceCapture([first, first, first], cts);
            var webhook = new FakeWebhook();
            await FinishLoop(new CaptureWorker(capture, new FakeVision(_ =>
                { analyses++; return Task.FromResult<WorkflowResult?>(null); }), webhook,
                interval: TimeSpan.FromMilliseconds(5)), cts.Token);
            Assert(analyses == 1 && webhook.Calls == 0, "een geslaagde null-analyse wordt niet voor hetzelfde beeld herhaald");
        }
        using var positioned = new MemoryStream(first);
        positioned.Position = positioned.Length;
        Assert(ScreenshotFingerprint.Compute(positioned).SequenceEqual(SHA256.HashData(first)),
            "fingerprint gebruikt de volledige screenshot onafhankelijk van streampositie");
    }

    private static async Task LoopAsync()
    {
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            int requests = 0;
            var capture = new FakeCapture();
            var webhook = new FakeWebhook(result: WebhookResult.UninstallRequested);
            await new CaptureWorker(capture, new FakeVision(_ => Task.FromResult<WorkflowResult?>(Result)), webhook,
                interval: TimeSpan.FromMilliseconds(5), requestUninstall: () => requests++).RunAsync(cts.Token);
            Assert(requests == 1 && webhook.Calls == 1 && capture.Streams.Count == 1 && !capture.Streams[0].CanRead,
                "verwijderrespons stopt de worker direct en ruimt de screenshot op");
        }
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var historyVision = new HistoryVision(cts);
            var webhook = new FakeWebhook();
            await FinishLoop(new CaptureWorker(new FakeCapture(), historyVision, webhook,
                interval: TimeSpan.FromMilliseconds(5)), cts.Token);
            Assert(historyVision.Counts.SequenceEqual(new[] { 0, 1, 2, 2 }) &&
                historyVision.SeenTexts[3].SequenceEqual(new[] { "screen-2", "screen-3" }),
                "worker gebruikt uitsluitend de twee vorige geslaagde schermteksten");
            Assert(webhook.Reports.Count == 3 && webhook.Reports.All(x => !x.Completed) &&
                webhook.Reports[2].PreviousObservations.Length == 2 &&
                webhook.Reports.All(x => x.CapturedAtUtc.Year > 2020),
                "ook lopend werk gaat met tijdstip en context naar de webhook");
            Assert(webhook.Reports[2].Report == "rapport-1; rapport-2; rapport-3" &&
                webhook.Reports[2].PreviousObservations.Last().Report == "rapport-1; rapport-2",
                "resultaat van iedere analyse wordt meegenomen om het volgende rapport verder op te bouwen");
        }

        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var capture = new FakeCapture();
            int analyses = 0;
            int active = 0;
            int maxActive = 0;
            var webhook = new FakeWebhook();
            var vision = new FakeVision(async token =>
            {
                maxActive = Math.Max(maxActive, ++active);
                await Task.Delay(25, token);
                active--;
                if (++analyses == 3) cts.Cancel();
                return null;
            });
            await FinishLoop(new CaptureWorker(capture, vision, webhook, interval: TimeSpan.FromMilliseconds(5)), cts.Token);
            Assert(analyses == 3 && maxActive == 1 && webhook.Calls == 0,
                "seriële loop zonder overlap; null slaat webhook over");
            Assert(capture.Streams.All(stream => !stream.CanRead), "alle screenshotstreams worden gedisposed");
        }

        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var capture = new FakeCapture();
            var webhook = new FakeWebhook();
            var vision = new FakeVision(_ => { cts.Cancel(); return Task.FromResult<WorkflowResult?>(Result); });
            await FinishLoop(new CaptureWorker(capture, vision, webhook), cts.Token);
            Assert(webhook.Calls == 0 && capture.Streams.All(stream => !stream.CanRead),
                "pauzeren na analyse voorkomt webhook en ruimt stream op");
        }

        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            int attempts = 0;
            var webhook = new FakeWebhook(() => cts.Cancel());
            var vision = new FakeVision(_ => ++attempts == 1
                ? Task.FromException<WorkflowResult?>(new HttpRequestException("mock fout"))
                : Task.FromResult<WorkflowResult?>(Result));
            await FinishLoop(new CaptureWorker(new FakeCapture(), vision, webhook,
                interval: TimeSpan.FromMilliseconds(10)), cts.Token);
            Assert(attempts == 2 && webhook.Calls == 1, "na API-fout blijft de loop doorlopen");
        }

        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var capture = new FakeCapture();
            var vision = new FakeVision(async token =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Result;
            });
            var worker = new CaptureWorker(capture, vision, new FakeWebhook());
            var task = worker.RunAsync(cts.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cts.Cancel();
            await task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert(capture.Streams.All(stream => !stream.CanRead), "pauzeren annuleert een actieve API-call");
        }
    }

    private static async Task FinishLoop(CaptureWorker worker, CancellationToken token)
    {
        try { await worker.RunAsync(token).WaitAsync(TimeSpan.FromSeconds(6)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static TrackerSettings Settings() => new()
    {
        VisionEndpoint = "https://vision.example.test/chat/completions",
        VisionApiKey = "vision-key",
        VisionModel = "vision-model",
        WebhookUrl = "https://webhook.example.test/workflows",
        WebhookBearerToken = "webhook-key"
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
        _passed++;
    }

    private static void ExpectThrows<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { Assert(true, name); return; }
        throw new InvalidOperationException("Verwachte fout ontbreekt: " + name);
    }

    private static async Task ExpectThrowsAsync<T>(Func<Task> action, string name) where T : Exception
    {
        try { await action(); }
        catch (T) { Assert(true, name); return; }
        throw new InvalidOperationException("Verwachte fout ontbreekt: " + name);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => respond(request, token);
    }

    private sealed class FakeCapture : IScreenCapture
    {
        public List<MemoryStream> Streams { get; } = [];
        public MemoryStream CapturePrimaryScreen()
        {
            var stream = new MemoryStream(MakePng(Streams.Count));
            Streams.Add(stream);
            return stream;
        }
    }

    private static byte[] MakePng(int value)
    {
        using var image = new System.Drawing.Bitmap(1, 1);
        image.SetPixel(0, 0, System.Drawing.Color.FromArgb(255, (value >> 16) & 255, (value >> 8) & 255, value & 255));
        using var output = new MemoryStream();
        image.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        return output.ToArray();
    }

    private sealed class SequenceCapture(byte[][] images, CancellationTokenSource cancel) : IScreenCapture
    {
        public List<MemoryStream> Streams { get; } = [];
        public MemoryStream CapturePrimaryScreen()
        {
            int index = Streams.Count;
            if (index >= images.Length) cancel.Cancel();
            var stream = new MemoryStream(images[Math.Min(index, images.Length - 1)]);
            Streams.Add(stream);
            return stream;
        }
    }

    private sealed class FakeVision(Func<CancellationToken, Task<WorkflowResult?>> analyze) : IVisionClient
    {
        public Task<WorkflowResult?> AnalyzeAsync(MemoryStream stream, CancellationToken token,
            IReadOnlyList<TextObservation>? previousObservations = null) => analyze(token);
    }

    private sealed class HistoryVision(CancellationTokenSource cancel) : IVisionClient
    {
        public List<int> Counts { get; } = [];
        public List<string[]> SeenTexts { get; } = [];
        public Task<WorkflowResult?> AnalyzeAsync(MemoryStream stream, CancellationToken token,
            IReadOnlyList<TextObservation>? previousObservations = null)
        {
            Counts.Add(previousObservations?.Count ?? 0);
            SeenTexts.Add(previousObservations?.Select(x => x.ScreenshotText).ToArray() ?? []);
            if (Counts.Count == 4) cancel.Cancel();
            string priorReport = previousObservations?.LastOrDefault()?.Report ?? "";
            string report = (priorReport.Length == 0 ? "" : priorReport + "; ") + "rapport-" + Counts.Count;
            return Task.FromResult<WorkflowResult?>(Result with { ScreenshotText = "screen-" + Counts.Count, Report = report });
        }
    }

    private sealed class FakeWebhook(Action? onSend = null, WebhookResult? result = null) : IWebhookClient
    {
        public int Calls { get; private set; }
        public List<WorkflowResult> Reports { get; } = [];
        public Task<WebhookResult> SendAsync(WorkflowResult workflow, MemoryStream stream, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++;
            Reports.Add(workflow);
            onSend?.Invoke();
            return Task.FromResult(result ?? WebhookResult.Accepted);
        }
    }
}
