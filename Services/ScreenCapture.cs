using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WorkflowTracker.Services;

public interface IScreenCapture
{
    MemoryStream CapturePrimaryScreen();
}

public sealed class ScreenCapture : IScreenCapture
{
    public MemoryStream CapturePrimaryScreen()
    {
        // Geen Forms.Screen/SystemEvents: alleen primaire schermmaten en GDI, zonder venster of proces te starten.
        var bounds = new Rectangle(0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("Geen primair scherm beschikbaar.");
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);

        var stream = new MemoryStream();
        try
        {
            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;
            return stream; // De aanroeper wordt eigenaar en moet de stream disposen.
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
