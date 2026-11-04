using WorkflowTracker.Models;

namespace WorkflowTracker.Services;

public sealed record RecordingWindow(bool IsActive, DateTimeOffset? NextChangeUtc);

public static class RecordingSchedule
{
    public static RecordingWindow Evaluate(TrackerSettings settings, DateTimeOffset utcNow, TimeZoneInfo zone,
        bool manualOverride = false)
    {
        if (manualOverride || !settings.DailyScheduleEnabled) return new(true, null);
        if (!TrackerSettings.TryParseRecordingTime(settings.RecordingStartTime, out var start) ||
            !TrackerSettings.TryParseRecordingTime(settings.RecordingEndTime, out var end) || start == end)
            throw new ArgumentException("Ongeldig opnamevenster.");

        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        var time = TimeOnly.FromDateTime(local.DateTime);
        bool active = start < end ? time >= start && time < end : time >= start || time < end;
        // Bereken de grens in de lokale tijdzone, zodat de nacht bij zomer-/wintertijd geen vaste 12 uur is.
        var boundaries = new List<DateTimeOffset>();
        for (int day = 0; day < 3; day++)
        foreach (var boundary in new[] { start, end })
        {
            var candidate = DateTime.SpecifyKind(local.Date.AddDays(day).Add(boundary.ToTimeSpan()), DateTimeKind.Unspecified);
            // Een overgeslagen kloktijd schuift door naar het eerstvolgende geldige lokale tijdstip.
            while (zone.IsInvalidTime(candidate)) candidate = candidate.AddMinutes(1);
            var offsets = zone.IsAmbiguousTime(candidate) ? zone.GetAmbiguousTimeOffsets(candidate) : [zone.GetUtcOffset(candidate)];
            foreach (var offset in offsets)
            {
                var instant = new DateTimeOffset(candidate, offset).ToUniversalTime();
                if (instant > utcNow) boundaries.Add(instant);
            }
        }
        return new(active, boundaries.Min());
    }
}
