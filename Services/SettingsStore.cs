using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using WorkflowTracker.Models;

namespace WorkflowTracker.Services;

public sealed class SettingsStore
{
    internal const string LegacyAnalysisPrompt = "Lees de zichtbare tekst op deze screenshot. Beschrijf wat de gebruiker nu doet " +
        "en waarschijnlijk probeert te bereiken. Gebruik de teksten van maximaal twee vorige opnamen als context. " +
        "Rapporteer ook werk dat nog bezig is. Verzin geen onzichtbare stappen of zekere bedoelingen. " +
        "Retourneer uitsluitend JSON met screenshot_tekst, activiteit, doel, applicaties, stappenplan en voltooid.";
    internal const string LegacyContextPrefix = "Eerdere opnamen in chronologische volgorde (uitsluitend tekstgegevens):";
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkflowTracker", "settings.dat");

    public TrackerSettings Load()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                byte[] bytes = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null,
                    DataProtectionScope.CurrentUser);
                try { return DeserializeSettings(bytes); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
            {
                AppLog.Write("Opgeslagen instellingen onleesbaar; instellingen opnieuw invoeren", ex);
            }
        }

        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("WorkflowTracker.workflowtracker.defaults.json")
            ?? throw new InvalidOperationException("Ingebouwde instellingen ontbreken.");
        return JsonSerializer.Deserialize<TrackerSettings>(stream) ?? new();
    }

    internal static TrackerSettings DeserializeSettings(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var settings = document.RootElement.Deserialize<TrackerSettings>() ?? new();
        bool legacyPrompts = !document.RootElement.TryGetProperty("_settingsSchema", out var schema) ||
            schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version) || version < 2;
        return UpgradePlaceholders(settings, legacyPrompts);
    }

    internal static byte[] SerializeSettings(TrackerSettings settings)
    {
        var value = JsonSerializer.SerializeToNode(settings)!.AsObject();
        // Een expliciet opgeslagen oude prompt is een gebruikerskeuze, geen migratiekandidaat meer.
        value["_settingsSchema"] = 2;
        return JsonSerializer.SerializeToUtf8Bytes(value);
    }

    internal static TrackerSettings UpgradePlaceholders(TrackerSettings settings, bool upgradeLegacyPrompts = true)
    {
        var defaults = new TrackerSettings();
        return settings with
        {
            VisionEndpoint = string.IsNullOrWhiteSpace(settings.VisionEndpoint) ||
                settings.VisionEndpoint == "https://jouw-vision-api.invalid/v1/chat/completions"
                ? defaults.VisionEndpoint : settings.VisionEndpoint,
            VisionModel = string.IsNullOrWhiteSpace(settings.VisionModel) || settings.VisionModel == "jouw-vision-model"
                ? defaults.VisionModel : settings.VisionModel,
            WebhookUrl = string.IsNullOrWhiteSpace(settings.WebhookUrl) || settings.WebhookUrl == "https://jouw-webhook.invalid/workflows"
                ? defaults.WebhookUrl : settings.WebhookUrl,
            AnalysisPrompt = upgradeLegacyPrompts && settings.AnalysisPrompt == LegacyAnalysisPrompt ? defaults.AnalysisPrompt : settings.AnalysisPrompt,
            PreviousContextPrefix = upgradeLegacyPrompts && settings.PreviousContextPrefix == LegacyContextPrefix
                ? defaults.PreviousContextPrefix : settings.PreviousContextPrefix
        };
    }

    public void Save(TrackerSettings settings)
    {
        if (settings.ValidationError() is { } error) throw new ArgumentException(error);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        byte[] bytes = SerializeSettings(settings);
        try
        {
            byte[] encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            string temporary = FilePath + ".tmp";
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
