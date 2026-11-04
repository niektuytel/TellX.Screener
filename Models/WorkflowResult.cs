using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkflowTracker.Models;

public sealed record WorkflowResult(
    [property: JsonPropertyName("doel")] string Doel,
    [property: JsonPropertyName("applicaties")] string[] Applicaties,
    [property: JsonPropertyName("stappenplan")] string[] Stappenplan)
{
    public const int MaxScreenshotTextLength = 20000;
    public const int MaxReportLength = 8000;
    [JsonPropertyName("rapport")]
    public string Report { get; init; } = "";
    [JsonPropertyName("screenshot_tekst")]
    public string ScreenshotText { get; init; } = "";
    [JsonPropertyName("activiteit")]
    public string Activity { get; init; } = "";
    [JsonPropertyName("voltooid")]
    public bool Completed { get; init; }
    [JsonPropertyName("vastgelegd_op_utc")]
    public DateTimeOffset CapturedAtUtc { get; init; }
    [JsonPropertyName("vorige_opnamen")]
    public TextObservation[] PreviousObservations { get; init; } = [];

    public static WorkflowResult? Parse(string content)
    {
        var json = content.Trim();
        // Sommige compatibele providers retourneren ondanks de prompt een code fence.
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            int lineEnd = json.IndexOf('\n');
            int fenceEnd = json.LastIndexOf("```", StringComparison.Ordinal);
            if (lineEnd < 0 || fenceEnd <= lineEnd) throw new JsonException("Ongeldige JSON-code fence.");
            json = json[(lineEnd + 1)..fenceEnd].Trim();
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Null) return null;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Object of null verwacht.");
        if (!root.TryGetProperty("voltooid", out var completed) ||
            completed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new JsonException("voltooid moet een boolean zijn.");
        return new WorkflowResult(ReadText(root, "doel", 4000), ReadArray(root, "applicaties"), ReadArray(root, "stappenplan"))
        {
            ScreenshotText = ReadText(root, "screenshot_tekst", MaxScreenshotTextLength),
            Activity = ReadText(root, "activiteit", 4000, allowEmpty: false),
            Report = ReadText(root, "rapport", MaxReportLength, allowEmpty: false),
            Completed = completed.GetBoolean()
        };
    }

    private static string ReadText(JsonElement root, string name, int limit, bool allowEmpty = true)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new JsonException($"{name} moet een string zijn.");
        string text = value.GetString()!;
        if (text.Length > limit || (!allowEmpty && string.IsNullOrWhiteSpace(text)))
            throw new JsonException($"{name} is leeg of te lang.");
        return text;
    }

    private static string[] ReadArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            throw new JsonException($"{name} moet een array zijn.");
        var values = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                throw new JsonException($"{name} bevat een ongeldige waarde.");
            values.Add(item.GetString()!);
        }
        return values.ToArray();
    }
}
