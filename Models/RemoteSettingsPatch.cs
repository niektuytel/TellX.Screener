using System.Text.Json;

namespace WorkflowTracker.Models;

/// <summary>Uitsluitend de instellingen die de ingestelde webhook mag aanpassen.</summary>
public sealed record RemoteSettingsPatch
{
    public string? VisionEndpoint { get; init; }
    public string? VisionApiKey { get; init; }
    public string? VisionModel { get; init; }
    public string? AnalysisPrompt { get; init; }
    public string? PreviousContextPrefix { get; init; }
    public int? CaptureIntervalSeconds { get; init; }
    public int? RequestTimeoutSeconds { get; init; }
    public bool? SendScreenshots { get; init; }
    public bool? DailyScheduleEnabled { get; init; }
    public string? RecordingStartTime { get; init; }
    public string? RecordingEndTime { get; init; }
    public bool? SkipWhenIdle { get; init; }

    public TrackerSettings ApplyTo(TrackerSettings current)
    {
        var updated = current with
        {
            VisionEndpoint = VisionEndpoint ?? current.VisionEndpoint,
            VisionApiKey = VisionApiKey ?? current.VisionApiKey,
            VisionModel = VisionModel ?? current.VisionModel,
            AnalysisPrompt = AnalysisPrompt ?? current.AnalysisPrompt,
            PreviousContextPrefix = PreviousContextPrefix ?? current.PreviousContextPrefix,
            CaptureIntervalSeconds = CaptureIntervalSeconds ?? current.CaptureIntervalSeconds,
            RequestTimeoutSeconds = RequestTimeoutSeconds ?? current.RequestTimeoutSeconds,
            SendScreenshots = SendScreenshots ?? current.SendScreenshots,
            DailyScheduleEnabled = DailyScheduleEnabled ?? current.DailyScheduleEnabled,
            RecordingStartTime = RecordingStartTime ?? current.RecordingStartTime,
            RecordingEndTime = RecordingEndTime ?? current.RecordingEndTime,
            SkipWhenIdle = SkipWhenIdle ?? current.SkipWhenIdle
        };
        if (updated.ValidationError() is { } error) throw new ArgumentException(error);
        return updated;
    }

    internal static RemoteSettingsPatch Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException("settings moet een object zijn.");
        var patch = new RemoteSettingsPatch();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new JsonException("Dubbele instelling geweigerd.");
            patch = property.Name switch
            {
                nameof(VisionEndpoint) => patch with { VisionEndpoint = ReadString(property.Value) },
                nameof(VisionApiKey) => patch with { VisionApiKey = ReadString(property.Value) },
                nameof(VisionModel) => patch with { VisionModel = ReadString(property.Value) },
                nameof(AnalysisPrompt) => patch with { AnalysisPrompt = ReadString(property.Value) },
                nameof(PreviousContextPrefix) => patch with { PreviousContextPrefix = ReadString(property.Value) },
                nameof(CaptureIntervalSeconds) => patch with { CaptureIntervalSeconds = ReadInteger(property.Value) },
                nameof(RequestTimeoutSeconds) => patch with { RequestTimeoutSeconds = ReadInteger(property.Value) },
                nameof(SendScreenshots) => patch with { SendScreenshots = ReadBoolean(property.Value) },
                nameof(DailyScheduleEnabled) => patch with { DailyScheduleEnabled = ReadBoolean(property.Value) },
                nameof(RecordingStartTime) => patch with { RecordingStartTime = ReadString(property.Value) },
                nameof(RecordingEndTime) => patch with { RecordingEndTime = ReadString(property.Value) },
                nameof(SkipWhenIdle) => patch with { SkipWhenIdle = ReadBoolean(property.Value) },
                // Geen reflectie/deserialisatie naar TrackerSettings: webhookvelden en onbekende velden zijn verboden.
                _ => throw new JsonException("Niet-toegestane instelling of onjuist type.")
            };
        }
        return patch;
    }

    private static string ReadString(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw new JsonException("Instelling moet een string zijn; null is niet toegestaan.");

    private static int ReadInteger(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)
        ? number : throw new JsonException("Instelling moet een geheel getal zijn.");

    private static bool ReadBoolean(JsonElement value) => value.ValueKind is JsonValueKind.True or JsonValueKind.False
        ? value.GetBoolean() : throw new JsonException("Instelling moet een boolean zijn.");
}
