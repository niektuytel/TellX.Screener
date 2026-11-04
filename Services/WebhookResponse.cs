using System.Net;
using System.Text.Json;
using WorkflowTracker.Models;

namespace WorkflowTracker.Services;

public static class WebhookResponse
{
    public const int MaxCommandBytes = 4096;
    public const int MaxResponseBytes = 128 * 1024;

    public static WebhookResult Parse(HttpStatusCode statusCode, string? mediaType, ReadOnlyMemory<byte> body)
    {
        if (statusCode != HttpStatusCode.OK ||
            !string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
            body.Length == 0 || body.Length > MaxResponseBytes)
            return WebhookResult.Accepted;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return WebhookResult.Accepted;
            var properties = root.EnumerateObject().ToArray();
            if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                return WebhookResult.Accepted;
            // Het bestaande verwijdercontract blijft exact hetzelfde, inclusief de kleinere limiet.
            if (root.TryGetProperty("action", out var action) || root.TryGetProperty("confirm", out _))
                return body.Length <= MaxCommandBytes && properties.Length == 2 &&
                    action.ValueKind == JsonValueKind.String && action.GetString() == "uninstall" &&
                    root.TryGetProperty("confirm", out var confirm) && confirm.ValueKind == JsonValueKind.String &&
                    confirm.GetString() == "WorkflowTracker"
                    ? WebhookResult.UninstallRequested : WebhookResult.Accepted;

            if (!root.TryGetProperty("settings", out var settings)) return WebhookResult.Accepted;
            try { return new WebhookResult(SettingsPatch: RemoteSettingsPatch.Parse(settings)); }
            catch (JsonException) { return new WebhookResult(SettingsError: "Webhook-instellingen geweigerd: ongeldige of niet-toegestane velden."); }
        }
        catch (JsonException) { return WebhookResult.Accepted; }
    }

    public static async Task<WebhookResult> ReadAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.StatusCode != HttpStatusCode.OK ||
            !string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
            response.Content.Headers.ContentLength > MaxResponseBytes)
            return WebhookResult.Accepted;

        // Lees ook bij chunked responses nooit een onbegrensde commandobody.
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var bytes = new byte[MaxResponseBytes + 1];
        int read = 0;
        while (read < bytes.Length)
        {
            int next = await stream.ReadAsync(bytes.AsMemory(read), token).ConfigureAwait(false);
            if (next == 0) break;
            read += next;
        }
        token.ThrowIfCancellationRequested();
        return Parse(response.StatusCode, response.Content.Headers.ContentType?.MediaType, bytes.AsMemory(0, read));
    }
}
