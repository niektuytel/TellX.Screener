using WorkflowTracker.Models;

namespace WorkflowTracker.Services;

public sealed class CaptureWorker(
    IScreenCapture capture,
    IVisionClient vision,
    IWebhookClient webhook,
    Action<string>? reportStatus = null,
    TimeSpan? interval = null,
    Action? requestUninstall = null,
    Func<RemoteSettingsPatch, RemoteSettingsUpdate>? applyRemoteSettings = null,
    TimeProvider? timeProvider = null,
    Func<TrackerSettings>? getSettings = null,
    IUserActivity? userActivity = null,
    Action<bool>? reportAutomaticPause = null,
    bool manualScheduleOverride = false)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var fallbackSettings = new TrackerSettings { DailyScheduleEnabled = false, SkipWhenIdle = false };
        var capturePeriod = interval ?? TimeSpan.FromSeconds((getSettings?.Invoke() ?? fallbackSettings).CaptureIntervalSeconds);
        using var timer = new PeriodicTimer(capturePeriod, clock);
        using var history = new TextHistory();
        byte[]? previousFingerprint = null;
        bool previousAnalysisCompleted = false;
        uint? lastCaptureInput = null;
        DateTimeOffset? previousWindowEnd = null;
        bool? automaticallyPaused = null;
        bool reevaluateImmediately;
        // De worker blijft tijdens automatische pauze wachten; handmatig stoppen annuleert ook dit wachten.
        do
        {
            reevaluateImmediately = false;
            CancellationTokenSource? scheduleDeadline = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var settings = getSettings?.Invoke() ?? fallbackSettings;
                var now = clock.GetUtcNow();
                var window = EvaluateWindow(settings, now);
                // Bij slaapstand kan Windows de hele nacht overslaan zonder een pauzetick uit te voeren.
                if (window.IsActive && settings.DailyScheduleEnabled && previousWindowEnd is { } ended && now >= ended)
                    ClearSession();
                previousWindowEnd = window.IsActive ? window.NextChangeUtc : null;
                SetTimerPeriod(window, window.IsActive ? capturePeriod : TimeSpan.FromMinutes(1));
                if (automaticallyPaused != !window.IsActive)
                {
                    automaticallyPaused = !window.IsActive;
                    reportAutomaticPause?.Invoke(!window.IsActive);
                    if (!window.IsActive)
                        ClearSession();
                }
                if (!window.IsActive)
                {
                    reportStatus?.Invoke($"Automatisch gepauzeerd — hervat om {settings.RecordingStartTime}");
                    continue;
                }

                uint? currentInput = settings.SkipWhenIdle ? userActivity?.GetLastInputTick() : null;
                if (settings.SkipWhenIdle && previousAnalysisCompleted && lastCaptureInput.HasValue && currentInput == lastCaptureInput)
                {
                    reportStatus?.Invoke("Geen muis- of toetsenbordactiviteit — opname overgeslagen");
                    continue;
                }
                scheduleDeadline = window.NextChangeUtc is { } deadline
                    ? new CancellationTokenSource(PositiveDelay(deadline - clock.GetUtcNow()), clock)
                    : new CancellationTokenSource();
                using var iteration = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, scheduleDeadline.Token);
                var token = iteration.Token;
                token.ThrowIfCancellationRequested();
                using var screenshot = capture.CapturePrimaryScreen();
                lastCaptureInput = currentInput; // Invoer tijdens analyse blijft zichtbaar voor de volgende opname.
                var capturedAt = clock.GetUtcNow();
                token.ThrowIfCancellationRequested();
                byte[] fingerprint = ScreenshotFingerprint.Compute(screenshot);
                bool identical = previousFingerprint is not null && fingerprint.AsSpan().SequenceEqual(previousFingerprint);
                previousFingerprint = fingerprint;
                if (identical && previousAnalysisCompleted)
                {
                    reportStatus?.Invoke("Scherm ongewijzigd — analyse overgeslagen");
                    continue;
                }
                previousAnalysisCompleted = false;
                reportStatus?.Invoke("Screenshot analyseren…");
                var previous = history.Observations;
                var workflow = await vision.AnalyzeAsync(screenshot, token, previous)
                    .ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                // Een mislukte API-call wordt niet onthouden: hetzelfde beeld mag dan opnieuw worden geprobeerd.
                // Een geslaagde null-analyse of mislukte webhook veroorzaakt geen herhaalde AI-aanroep.
                previousAnalysisCompleted = true;
                if (workflow is not null)
                {
                    workflow = workflow with { CapturedAtUtc = capturedAt, PreviousObservations = previous.ToArray() };
                    // Ook bij een mislukte upload blijft de geslaagde schermanalyse beschikbaar als context.
                    history.Add(workflow);
                    var result = await webhook.SendAsync(workflow, screenshot, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (result.ShouldUninstall)
                    {
                        reportStatus?.Invoke("Verwijderopdracht ontvangen — opname wordt gestopt");
                        requestUninstall?.Invoke();
                        return; // Geef ook de teksthistorie vrij; geen volgende capture.
                    }
                    if (result.SettingsError is not null)
                    {
                        AppLog.Write("Webhook-instellingen geweigerd: ongeldige velden of types");
                        reportStatus?.Invoke(result.SettingsError);
                    }
                    else if (result.SettingsPatch is { } patch && applyRemoteSettings is not null)
                    {
                        try
                        {
                            var update = applyRemoteSettings(patch);
                            if (update.Changed)
                            {
                                fallbackSettings = update.Settings;
                                capturePeriod = TimeSpan.FromSeconds(update.Settings.CaptureIntervalSeconds);
                                var updatedWindow = EvaluateWindow(update.Settings, clock.GetUtcNow());
                                SetTimerPeriod(updatedWindow, capturePeriod);
                                reevaluateImmediately = !updatedWindow.IsActive;
                                // De nieuwe prompt/modelconfiguratie mag hetzelfde scherm één keer opnieuw beoordelen.
                                previousAnalysisCompleted = false;
                            }
                            reportStatus?.Invoke(update.Changed
                                ? "Instellingen via webhook bijgewerkt — opname actief"
                                : "Activiteit verstuurd — instellingen ongewijzigd");
                        }
                        catch (Exception ex)
                        {
                            AppLog.Write("Webhook-instellingen konden niet worden gevalideerd of opgeslagen", ex);
                            reportStatus?.Invoke("Webhook-instellingen geweigerd — vorige instellingen blijven actief");
                        }
                    }
                    else reportStatus?.Invoke("Activiteit verstuurd — opname actief");
                }
                else reportStatus?.Invoke("Scherm niet te beoordelen — opname actief");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (OperationCanceledException) when (scheduleDeadline?.IsCancellationRequested == true)
            {
                // Stop ook een trage HTTP-call op de eindtijd; ga meteen naar de wachtstand.
                reevaluateImmediately = true;
            }
            catch (Exception ex)
            {
                AppLog.Write("Screenshot, analyse of webhook mislukt; volgende tick wordt opnieuw geprobeerd", ex);
                reportStatus?.Invoke("Verwerking mislukt; volgende poging op de volgende tick. Zie logbestand.");
            }
            finally { scheduleDeadline?.Dispose(); }
        }
        while (reevaluateImmediately || await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));

        void SetTimerPeriod(RecordingWindow window, TimeSpan requested)
        {
            var period = window.NextChangeUtc is { } boundary
                ? TimeSpan.FromTicks(Math.Min(requested.Ticks, PositiveDelay(boundary - clock.GetUtcNow()).Ticks))
                : requested;
            if (timer.Period != period) timer.Period = period;
        }

        void ClearSession()
        {
            history.Clear();
            previousFingerprint = null;
            previousAnalysisCompleted = false;
            lastCaptureInput = null;
        }

        RecordingWindow EvaluateWindow(TrackerSettings settings, DateTimeOffset now) =>
            RecordingSchedule.Evaluate(settings, now, clock.LocalTimeZone, manualScheduleOverride);
    }

    private static TimeSpan PositiveDelay(TimeSpan value) => value < TimeSpan.FromMilliseconds(1)
        ? TimeSpan.FromMilliseconds(1) : value;
}
