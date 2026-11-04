using System.Text.Json.Serialization;

namespace WorkflowTracker.Models;

public sealed record TextObservation(
    [property: JsonPropertyName("vastgelegd_op_utc")] DateTimeOffset CapturedAtUtc,
    [property: JsonPropertyName("screenshot_tekst")] string ScreenshotText,
    [property: JsonPropertyName("activiteit")] string Activity,
    [property: JsonPropertyName("doel")] string Goal)
{
    [JsonPropertyName("rapport")]
    public string Report { get; init; } = "";
    [JsonPropertyName("applicaties")]
    public string[] Applications { get; init; } = [];
    [JsonPropertyName("stappenplan")]
    public string[] Steps { get; init; } = [];
    [JsonPropertyName("voltooid")]
    public bool Completed { get; init; }
}
