using System.Security.Cryptography;

namespace WorkflowTracker.Services;

internal static class ScreenshotFingerprint
{
    public static byte[] Compute(MemoryStream screenshot)
    {
        // ScreenCapture gebruikt steeds dezelfde PNG-encoder zonder variabele metadata.
        // Alleen 32 hashbytes blijven bewaard, geen kopie van de vorige screenshot.
        return screenshot.TryGetBuffer(out var buffer)
            ? SHA256.HashData(buffer.AsSpan())
            : SHA256.HashData(screenshot.ToArray());
    }
}
