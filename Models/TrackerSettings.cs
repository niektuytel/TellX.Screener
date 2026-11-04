namespace WorkflowTracker.Models;

public sealed record TrackerSettings
{
    public const string DefaultAnalysisPrompt = "Lees de zichtbare tekst op deze screenshot. Beschrijf wat de gebruiker nu doet " +
        "en waarschijnlijk probeert te bereiken. Deze screenshot is een volgend moment in dezelfde opnamesessie. " +
        "Gebruik de volledige analyseresultaten van de twee vorige opnamen en hun rapport als context. " +
        "Vergelijk met het vorige resultaat en bouw het veld rapport verder uit tot een samenhangend verslag: " +
        "behoud relevante eerdere bevindingen, voeg nieuwe handelingen en veranderingen toe en benoem een eventuele taakwissel. " +
        "Leg alleen verbanden waarvoor aanwijzingen zijn. Rapporteer ook lopend werk; verzin geen onzichtbare stappen of zekere bedoelingen. " +
        "Retourneer JSON met screenshot_tekst, activiteit, doel, applicaties, stappenplan, voltooid en rapport.";
    public const string DefaultPreviousContextPrefix = "Vorige analyseresultaten (oud naar nieuw). De huidige screenshot volgt hierop; " +
        "gebruik deze resultaten en hun doorlopende rapport als samenhangende context:";
    public const int MaxAnalysisPromptLength = 10000;
    public const int MaxPreviousContextPrefixLength = 2000;
    public string VisionEndpoint { get; init; } = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";
    public string VisionApiKey { get; init; } = "";
    public string VisionModel { get; init; } = "gemini-3.1-flash-lite";
    public string WebhookUrl { get; init; } = "https://tellx.nl/tuytelmachinery-assistent/screener";
    public string WebhookBearerToken { get; init; } = "";
    public int CaptureIntervalSeconds { get; init; } = 20;
    public int RequestTimeoutSeconds { get; init; } = 90;
    public bool SendScreenshots { get; init; } = false;
    public bool DailyScheduleEnabled { get; init; } = true;
    public string RecordingStartTime { get; init; } = "06:00";
    public string RecordingEndTime { get; init; } = "18:00";
    public bool SkipWhenIdle { get; init; } = true;
    public string AnalysisPrompt { get; init; } = DefaultAnalysisPrompt;
    public string PreviousContextPrefix { get; init; } = DefaultPreviousContextPrefix;

    public string? ValidationError()
    {
        if (!IsHttps(VisionEndpoint)) return "Vul een geldige HTTPS Vision-endpoint-URL in.";
        if (string.IsNullOrWhiteSpace(VisionModel)) return "Vul de naam van het vision-model in.";
        if (!IsHttps(WebhookUrl)) return "Vul een geldige HTTPS-webhook-URL in.";
        if (new Uri(VisionEndpoint).Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) ||
            new Uri(WebhookUrl).Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) ||
            VisionModel == "jouw-vision-model")
            return "Vervang de voorbeeld-URLs en modelnaam door jouw echte gegevens.";
        if (new Uri(VisionEndpoint).Host.Equals("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(VisionApiKey))
            return "Vul je Google Gemini API-key in. De API, modelnaam en webhook zijn al ingesteld.";
        if (CaptureIntervalSeconds is < 5 or > 3600) return "Capture-interval moet 5–3600 seconden zijn.";
        if (RequestTimeoutSeconds is < 5 or > 600) return "HTTP-timeout moet 5–600 seconden zijn.";
        if (!TryParseRecordingTime(RecordingStartTime, out var start) ||
            !TryParseRecordingTime(RecordingEndTime, out var end) || start == end)
            return "Gebruik twee verschillende opnametijden in HH:mm-formaat (bijvoorbeeld 06:00 en 18:00).";
        if (string.IsNullOrWhiteSpace(AnalysisPrompt) || AnalysisPrompt.Length > MaxAnalysisPromptLength)
            return "Vul een analyseprompt van maximaal 10000 tekens in (Geavanceerd).";
        if (PreviousContextPrefix is null || PreviousContextPrefix.Length > MaxPreviousContextPrefixLength)
            return "Het contextprefix mag maximaal 2000 tekens bevatten (Geavanceerd).";
        if (VisionApiKey.Contains('\r') || VisionApiKey.Contains('\n') ||
            WebhookBearerToken.Contains('\r') || WebhookBearerToken.Contains('\n'))
            return "Tokens mogen geen regeleinden bevatten.";
        return null;
    }

    private static bool IsHttps(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);

    public static bool TryParseRecordingTime(string? value, out TimeOnly time) => TimeOnly.TryParseExact(
        value, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.None, out time);
}
