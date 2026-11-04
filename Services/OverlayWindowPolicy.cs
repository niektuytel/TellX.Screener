using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WorkflowTracker.Services;

internal static class OverlayWindowPolicy
{
    internal const long ToolWindow = 0x00000080L;
    internal const long AppWindow = 0x00040000L;
    private const int ExtendedStyleIndex = -20;

    public static void Apply(IntPtr handle)
    {
        long style = ReadStyle(handle);
        var updated = new IntPtr((style | ToolWindow) & ~AppWindow);
        Marshal.SetLastPInvokeError(0);
        IntPtr previous = IntPtr.Size == 8
            ? SetWindowLongPtr64(handle, ExtendedStyleIndex, updated)
            : new IntPtr(SetWindowLong32(handle, ExtendedStyleIndex, updated.ToInt32()));
        if (previous == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    internal static long ReadStyle(IntPtr handle) => IntPtr.Size == 8
        ? GetWindowLongPtr64(handle, ExtendedStyleIndex).ToInt64()
        : GetWindowLong32(handle, ExtendedStyleIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr handle, int index, int value);
}
