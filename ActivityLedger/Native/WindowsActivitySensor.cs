using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ActivityLedger.Native;

/// <summary>A best-effort snapshot of the interactive desktop; contains no keystrokes or screen pixels.</summary>
public sealed record WindowSample(
    DateTimeOffset CapturedAtUtc,
    string ProcessName,
    string ExecutablePath,
    string WindowTitle,
    nint WindowHandle,
    int ProcessId,
    double IdleSeconds,
    bool SessionLocked);

/// <summary>Samples the foreground window of the current user's Windows session without elevation.</summary>
public sealed class WindowsActivitySensor
{
    private const uint DesktopReadObjects = 0x0001;
    private const int UserObjectName = 2;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public WindowSample Sample()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Desktop activity collection requires Windows.");

        var captured = DateTimeOffset.UtcNow;
        var idle = GetIdleSeconds();
        var locked = IsSessionLocked();
        if (locked)
            return new(captured, "", "", "", 0, 0, idle, true);

        var window = GetForegroundWindow();
        if (window == 0)
            return new(captured, "", "", "", 0, 0, idle, false);

        _ = GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId > int.MaxValue)
            return new(captured, "", "", "", window, 0, idle, false);

        var titleLength = Math.Clamp(GetWindowTextLengthW(window), 0, 4095);
        var title = new StringBuilder(titleLength + 1);
        _ = GetWindowTextW(window, title, title.Capacity);

        var path = GetExecutablePath(processId);
        var processName = path.Length > 0 ? Path.GetFileName(path) : GetProcessName((int)processId);
        // The foreground can change while its metadata is being read. Do not join the
        // title of a destroyed/recycled window to an unrelated process.
        if (!IsWindow(window) || GetForegroundWindow() != window)
            return new(captured, "", "", "", 0, 0, idle, false);

        return new(captured, processName, path, title.ToString(), window, (int)processId, idle, false);
    }

    /// <summary>Browser identifiers understood by the bundled Chrome/Edge extension.</summary>
    public static string? GetSupportedBrowser(string processName)
    {
        var name = Path.GetFileNameWithoutExtension(processName.Trim().Replace('\\', '/')).ToLowerInvariant();
        return name switch
        {
            "chrome" => "chrome",
            "msedge" => "msedge",
            _ => null
        };
    }

    private static double GetIdleSeconds()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
            return 0;

        // LASTINPUTINFO and GetTickCount use the same wrapping 32-bit time base.
        // Unsigned subtraction remains correct when the uptime passes 49.7 days.
        var elapsed = unchecked((uint)Environment.TickCount - info.Time);
        return elapsed / 1000d;
    }

    private static bool IsSessionLocked()
    {
        var desktop = OpenInputDesktop(0, false, DesktopReadObjects);
        if (desktop == 0)
            return true; // Secure desktop / disconnected session: never attribute app time.

        try
        {
            var name = new StringBuilder(256);
            if (!GetUserObjectInformationW(desktop, UserObjectName, name, name.Capacity * sizeof(char), out _))
                return true;

            return name.ToString().Equals("Winlogon", StringComparison.OrdinalIgnoreCase)
                || name.ToString().Equals("Disconnect", StringComparison.OrdinalIgnoreCase)
                || name.ToString().Equals("Screen-saver", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _ = CloseDesktop(desktop);
        }
    }

    private static string GetExecutablePath(uint processId)
    {
        using var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid)
            return "";

        var path = new StringBuilder(32768);
        var length = (uint)path.Capacity;
        return QueryFullProcessImageNameW(process, 0, path, ref length) ? path.ToString() : "";
    }

    private static string GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName + ".exe";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return ""; // The process may exit between the window and process queries.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLengthW(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(nint window, StringBuilder title, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint desiredAccess);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformationW(nint handle, int index, StringBuilder information, int informationLength, out uint lengthNeeded);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(nint desktop);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder executableName, ref uint size);
}
