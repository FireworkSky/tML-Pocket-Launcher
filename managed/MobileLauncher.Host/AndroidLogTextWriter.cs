using System.Runtime.InteropServices;
using System.Text;

namespace MobileLauncher.Host;

/// <summary>Buffers console fragments into bounded UTF-8 logcat entries.</summary>
internal sealed class AndroidLogTextWriter(int priority) : TextWriter
{
    private readonly StringBuilder _line = new();
    private readonly object _sync = new();

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        lock (_sync)
        {
            Append(value);
        }
    }

    public override void Write(string? value)
    {
        if (value is null) return;
        lock (_sync)
        {
            foreach (var character in value) Append(character);
        }
    }

    public override void WriteLine(string? value)
    {
        lock (_sync)
        {
            Write(value);
            Emit();
        }
    }

    public override void Flush()
    {
        lock (_sync)
        {
            Emit();
        }
    }

    private void Append(char value)
    {
        if (value == '\n')
        {
            Emit();
            return;
        }

        // A UTF-16 character uses at most three UTF-8 bytes. Keep entries below
        // logcat's payload limit, and never split a surrogate pair.
        _line.Append(value == '\0' ? '\uFFFD' : value);
        if (_line.Length >= 1000 && !char.IsHighSurrogate(value)) Emit();
    }

    private void Emit()
    {
        if (_line.Length == 0) return;
        var message = _line.ToString().TrimEnd('\r');
        WriteSessionLog(priority, message);
        NativeWrite(priority, "tMLPocketCoreClr", message);
        _line.Clear();
    }

    [DllImport("main", EntryPoint = "tml_pocket_managed_log", CallingConvention = CallingConvention.Cdecl)]
    private static extern void WriteSessionLog(int priority, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);

    [DllImport("liblog.so", EntryPoint = "__android_log_write", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeWrite(
        int priority,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string tag,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string message);
}
