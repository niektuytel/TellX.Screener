using System.Runtime.InteropServices;

namespace WorkflowTracker.Services;

public interface IUserActivity
{
    uint? GetLastInputTick();
}

/// <summary>Alleen het laatste invoertijdstip van deze Windows-sessie; geen toetsen of posities.</summary>
public sealed class UserActivity : IUserActivity
{
    private bool _failureLogged;

    public uint? GetLastInputTick()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (GetLastInputInfo(ref info)) return info.Tick;
        if (!_failureLogged)
        {
            AppLog.Write("Laatste invoertijdstip niet beschikbaar; activiteitsfilter tijdelijk overgeslagen");
            _failureLogged = true;
        }
        return null; // Bij een OS-fout gewoon opnemen, niet permanent stilvallen.
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size; public uint Tick; }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
