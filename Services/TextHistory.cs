using WorkflowTracker.Models;

namespace WorkflowTracker.Services;

/// <summary>Twee volledige analyseresultaten zonder afbeeldingen of recursieve voorgeschiedenis.</summary>
public sealed class TextHistory : IDisposable
{
    public const int Capacity = 2;
    private readonly Queue<TextObservation> _observations = new();
    public IReadOnlyList<TextObservation> Observations => _observations.ToArray();

    public void Add(WorkflowResult result)
    {
        if (_observations.Count == Capacity) _observations.Dequeue();
        _observations.Enqueue(new TextObservation(result.CapturedAtUtc, result.ScreenshotText, result.Activity, result.Doel)
        {
            Report = result.Report,
            Applications = result.Applicaties.ToArray(),
            Steps = result.Stappenplan.ToArray(),
            Completed = result.Completed
        });
    }

    public void Clear() => _observations.Clear();
    public void Dispose() => Clear();
}
