using WorkflowTracker.Models;

namespace WorkflowTracker.Services;

public sealed record RemoteSettingsUpdate(TrackerSettings Settings, bool Changed);

public static class RemoteSettingsUpdater
{
    public static RemoteSettingsUpdate Apply(TrackerSettings current, RemoteSettingsPatch patch,
        Action<TrackerSettings> persist)
    {
        var updated = patch.ApplyTo(current);
        if (updated == current) return new(current, Changed: false);
        // Eerst valideren en duurzaam opslaan; pas daarna mag de aanroeper live omschakelen.
        persist(updated);
        return new(updated, Changed: true);
    }
}
