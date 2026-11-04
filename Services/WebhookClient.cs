using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WorkflowTracker.Models;

namespace WorkflowTracker.Services;

public interface IWebhookClient
{
    Task<WebhookResult> SendAsync(WorkflowResult workflow, MemoryStream screenshot, CancellationToken cancellationToken);
}

public sealed class WebhookClient(HttpClient http, Func<TrackerSettings> getSettings) : IWebhookClient
{
    public WebhookClient(HttpClient http, TrackerSettings settings) : this(http, () => settings) { }

    public async Task<WebhookResult> SendAsync(WorkflowResult workflow, MemoryStream screenshot, CancellationToken cancellationToken)
    {
        var settings = getSettings();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        cancellationToken = timeout.Token;
        cancellationToken.ThrowIfCancellationRequested();
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(JsonSerializer.Serialize(workflow), Encoding.UTF8, "application/json"),
            "workflow");
        if (settings.SendScreenshots)
        {
            // Alleen na expliciet inschakelen in Geavanceerd wordt een afbeelding meegestuurd.
            var image = new ByteArrayContent(screenshot.ToArray());
            image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            multipart.Add(image, "screenshot", $"screenshot-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.WebhookUrl) { Content = multipart };
        if (!string.IsNullOrWhiteSpace(settings.WebhookBearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.WebhookBearerToken);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await WebhookResponse.ReadAsync(response, cancellationToken).ConfigureAwait(false);
    }
}
