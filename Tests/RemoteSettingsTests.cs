using System.Net;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using WorkflowTracker.Models;
using WorkflowTracker.Services;

internal static partial class Program
{
    private static WebhookResult ParseReply(string json) => WebhookResponse.Parse(
        HttpStatusCode.OK, "application/json", Encoding.UTF8.GetBytes(json));

    private static void RemoteSettingsUnitTests()
    {
        var initial = Settings();
        var reply = ParseReply("{\"ok\":true,\"settings\":{\"AnalysisPrompt\":\"Nieuwe opdracht\",\"VisionModel\":\"ander-model\",\"SendScreenshots\":true}}");
        int saves = 0;
        var update = RemoteSettingsUpdater.Apply(initial, reply.SettingsPatch!, _ => saves++);
        Assert(update.Changed && saves == 1 && update.Settings.AnalysisPrompt == "Nieuwe opdracht" &&
            update.Settings.VisionModel == "ander-model" && update.Settings.SendScreenshots,
            "webhook-settingsobject past alleen aanwezige toegestane instellingen toe");
        Assert(update.Settings.WebhookUrl == initial.WebhookUrl && update.Settings.WebhookBearerToken == initial.WebhookBearerToken &&
            update.Settings.VisionApiKey == initial.VisionApiKey && update.Settings.CaptureIntervalSeconds == initial.CaptureIntervalSeconds,
            "webhookadres, webhook-token en ontbrekende instellingen blijven behouden");
        var repeated = RemoteSettingsUpdater.Apply(update.Settings, reply.SettingsPatch!, _ => saves++);
        Assert(!repeated.Changed && saves == 1, "dezelfde instellingen zijn idempotent en worden niet steeds opnieuw opgeslagen");
        var empty = RemoteSettingsUpdater.Apply(initial, ParseReply("{\"settings\":{}}").SettingsPatch!, _ => saves++);
        Assert(!empty.Changed && saves == 1, "een leeg settingsobject verandert niets");

        foreach (string bad in new[]
        {
            "{\"WebhookUrl\":\"https://ander.example.test\"}", "{\"WebhookBearerToken\":\"ander-token\"}",
            "{\"AnalysisPrompt\":\"nieuwe opdracht\",\"WebhookUrl\":\"https://ander.example.test\"}",
            "{\"VisionModel\":null}", "{\"CaptureIntervalSeconds\":\"10\"}", "{\"CaptureIntervalSeconds\":5.5}",
            "{\"SendScreenshots\":\"true\"}", "{\"VisionModel\":\"a\",\"VisionModel\":\"b\"}", "{\"Unknown\":true}", "null", "[]"
        })
        {
            var rejected = ParseReply("{\"settings\":" + bad + "}");
            Assert(rejected.SettingsPatch is null && rejected.SettingsError is not null && !rejected.ShouldUninstall,
                "verboden, dubbele, null- of onjuist getypeerde remote-instellingen worden volledig geweigerd");
        }
        foreach (string badValue in new[]
        {
            "{\"CaptureIntervalSeconds\":1}", "{\"RequestTimeoutSeconds\":601}",
            "{\"VisionEndpoint\":\"http://onveilig.example.test\"}", "{\"AnalysisPrompt\":\" \"}"
        })
        {
            var patch = ParseReply("{\"settings\":" + badValue + "}").SettingsPatch!;
            ExpectThrows<ArgumentException>(() => RemoteSettingsUpdater.Apply(initial, patch, _ => saves++),
                "ongeldige remote-waarde wordt geweigerd voor het opslaan");
        }
        Assert(saves == 1, "geweigerde updates schrijven geen gedeeltelijke instellingen");
        var failure = new RemoteSettingsPatch { VisionModel = "nieuw-model" };
        ExpectThrows<IOException>(() => RemoteSettingsUpdater.Apply(initial, failure, _ => throw new IOException("fake opslagfout")),
            "een opslagfout voorkomt dat de update als toegepast wordt teruggegeven");
        Assert(ParseReply("{\"action\":\"uninstall\",\"confirm\":\"WorkflowTracker\",\"settings\":{\"VisionModel\":\"a\"}}") == WebhookResult.Accepted,
            "verwijderen en instellingen combineren activeert geen van beide");
        Assert(ParseReply("{\"settings\":{\"AnalysisPrompt\":\"" + new string('x', 6000) + "\"}}").SettingsPatch is not null,
            "instellingenupdates mogen groter dan de oude 4096-byte verwijderlimiet zijn");
        Assert(ParseReply(new string(' ', WebhookResponse.MaxResponseBytes) + "{}") == WebhookResult.Accepted,
            "te grote webhookresponses worden niet als instellingen verwerkt");
        var explicitLegacy = initial with { AnalysisPrompt = SettingsStore.LegacyAnalysisPrompt,
            PreviousContextPrefix = SettingsStore.LegacyContextPrefix };
        Assert(SettingsStore.DeserializeSettings(SettingsStore.SerializeSettings(explicitLegacy)) == explicitLegacy,
            "expliciet gekozen prompts blijven na herladen exact behouden, ook als ze op oude standaardteksten lijken");
        Assert(SettingsStore.DeserializeSettings(JsonSerializer.SerializeToUtf8Bytes(explicitLegacy)).AnalysisPrompt == TrackerSettings.DefaultAnalysisPrompt,
            "alleen oude opslagformaten krijgen nog automatische promptmigratie");
    }

    private static async Task RemoteSettingsIntegrationAsync()
    {
        using (var stream = new CountingResponseStream(new byte[WebhookResponse.MaxResponseBytes * 2]))
        using (var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) })
        {
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            Assert(await WebhookResponse.ReadAsync(response, CancellationToken.None) == WebhookResult.Accepted &&
                stream.BytesRead == WebhookResponse.MaxResponseBytes + 1,
                "ook chunked webhookresponses worden begrensd gelezen tot maximaal 128 KiB plus een controlebyte");
        }
        var current = Settings();
        var initial = current;
        int saves = 0;
        int analyses = 0;
        int uploads = 0;
        var firstUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondUpload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var skipped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new ManualTimeProvider();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var http = new HttpClient(new FakeHandler(async (request, token) =>
        {
            if (request.Content is not MultipartFormDataContent multipart)
            {
                analyses++;
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var body = document.RootElement;
                Assert(body.GetProperty("model").GetString() == (analyses == 1 ? initial.VisionModel : "remote-model"),
                    "de volgende echte Vision-request gebruikt het remote ingestelde model");
                if (analyses == 2)
                {
                    Assert(request.RequestUri!.AbsoluteUri == "https://new-vision.example.test/chat/completions" &&
                        request.Headers.Authorization?.Parameter == "remote-key" &&
                        body.GetProperty("messages")[1].GetProperty("content")[0].GetProperty("text").GetString()!
                            .StartsWith("REMOTE PROMPT\n\nREMOTE CONTEXT\n", StringComparison.Ordinal),
                        "endpoint, API-key, prompt en contextprefix worden live bijgewerkt");
                }
                return JsonResponse(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content = JsonSerializer.Serialize(Result) } } }
                }));
            }
            uploads++;
            Assert(request.RequestUri!.AbsoluteUri == initial.WebhookUrl && request.Headers.Authorization?.Parameter == initial.WebhookBearerToken,
                "de webhookbestemming en webhook-authenticatie blijven lokaal ingesteld");
            Assert(multipart.Count() == (uploads == 1 ? 1 : 2), "remote screenshotoptie wordt pas bij de volgende upload actief");
            return JsonResponse("{\"settings\":{\"VisionEndpoint\":\"https://new-vision.example.test/chat/completions\",\"VisionApiKey\":\"remote-key\",\"VisionModel\":\"remote-model\",\"AnalysisPrompt\":\"REMOTE PROMPT\",\"PreviousContextPrefix\":\"REMOTE CONTEXT\",\"CaptureIntervalSeconds\":5,\"RequestTimeoutSeconds\":30,\"SendScreenshots\":true}}");
        }));
        var capture = new SequenceCapture([Png, Png, Png], stop);
        var worker = new CaptureWorker(capture, new VisionClient(http, () => current), new WebhookClient(http, () => current),
            reportStatus: status =>
            {
                if (status.StartsWith("Instellingen via webhook bijgewerkt", StringComparison.Ordinal)) firstUpdate.TrySetResult();
                if (status.StartsWith("Activiteit verstuurd — instellingen ongewijzigd", StringComparison.Ordinal)) secondUpload.TrySetResult();
                if (status.Contains("overgeslagen", StringComparison.Ordinal)) skipped.TrySetResult();
            },
            interval: TimeSpan.FromSeconds(20),
            applyRemoteSettings: patch =>
            {
                var update = RemoteSettingsUpdater.Apply(current, patch, _ => saves++);
                if (update.Changed) current = update.Settings;
                return update;
            }, timeProvider: clock);
        var task = worker.RunAsync(stop.Token);
        await firstUpdate.Task.WaitAsync(TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert(analyses == 1, "na een remote intervalwijziging wordt niet te vroeg opnieuw geanalyseerd");
        clock.Advance(TimeSpan.FromSeconds(1));
        await secondUpload.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert(analyses == 2 && current.CaptureIntervalSeconds == 5 && current.RequestTimeoutSeconds == 30,
            "het nieuwe interval van 5 seconden wordt zonder herstart actief");
        clock.Advance(TimeSpan.FromSeconds(5));
        await skipped.Task.WaitAsync(TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromSeconds(5));
        try { await task.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        Assert(saves == 1 && analyses == 2 && uploads == 2,
            "een gewijzigde configuratie analyseert hetzelfde beeld eenmalig opnieuw; herhaalde patches doorbreken deduplicatie niet");
    }

    private sealed class CountingResponseStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }
    }
}
