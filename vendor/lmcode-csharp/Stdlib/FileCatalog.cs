using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LmCode.Stdlib;

/// <summary>Optional raw-byte regular-file helpers.</summary>
public static class FileCatalog
{
    public static void RegisterFile(Registry r)
    {
        r.BeginCompatibilityRegistration();
        try
        {
            r.Function("file_read", ["string"], "string", static (_, a) => Value.Str(VmString.FromOwned(Read("file_read", a[0].AsStr()))));
            r.Function("file_write", ["string", "string"], "void", static (_, a) => { Write("file_write", a[0].AsStr(), a[1].AsStr().Span, append: false); return Value.None; });
            r.Function("file_append", ["string", "string"], "void", static (_, a) => { Write("file_append", a[0].AsStr(), a[1].AsStr().Span, append: true); return Value.None; });
            r.Function("file_exists", ["string"], "bool", static (_, a) => Value.Bool(Exists("file_exists", a[0].AsStr())));
            r.Function("file_lines", ["string"], "StrList", static (c, a) =>
            {
                byte[] bytes = Read("file_lines", a[0].AsStr()); var list = new StrList(); int start = 0;
                for (int i = 0; i < bytes.Length; ++i) if (bytes[i] == (byte)'\n') { list.Items.Add(new VmString(bytes.AsSpan(start, i - start))); start = i + 1; }
                list.Items.Add(new VmString(bytes.AsSpan(start))); return Value.Object("StrList", CollectionsCatalog.AllocateStrList(c, list));
            });
        }
        finally { r.EndCompatibilityRegistration(); }
    }

    private static byte[] Read(string function, VmString path)
    {
        ValidatePath(function, path);
        if (TryTextPath(path, out string? text))
        {
            try { EnsureRegular(function, text!); return File.ReadAllBytes(text!); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { throw Failure(function, path, e.Message); }
        }
        return UnixRead(function, path.Span);
    }
    private static void Write(string function, VmString path, ReadOnlySpan<byte> bytes, bool append)
    {
        ValidatePath(function, path);
        if (TryTextPath(path, out string? text))
        {
            try
            {
                EnsureRegular(function, text!, allowMissing: true);
                using var stream = new FileStream(text!, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
                stream.Write(bytes); stream.Flush(true); return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { throw Failure(function, path, e.Message); }
        }
        UnixWrite(function, path.Span, bytes, append);
    }
    private static bool Exists(string function, VmString path)
    {
        ValidatePath(function, path);
        if (TryTextPath(path, out string? text))
        {
            if (text!.Length == 0) return false;
            return WindowsRegular(function, path, text) == true;
        }
        byte[] native = NativePath(path.Span); byte[] stat = new byte[512];
        if (UnixStat(native, stat) != 0) { int error = Marshal.GetLastPInvokeError(); return error is 2 or 20 ? false : throw Failure(function, path, Errno(error)); }
        return StatRegular(stat);
    }
    private static void EnsureRegular(string function, string path, bool allowMissing = false)
    {
        bool? regular = WindowsRegular(function, VmString.FromString(path), path);
        if (regular == true || (regular is null && allowMissing)) return;
        throw Failure(function, VmString.FromString(path), new Win32Exception(regular is null ? 2 : 87).Message);
    }
    // Query with no read/write access: write-only files still support file_write,
    // and existence does not depend on content permissions. The native handle
    // follows symlinks and identifies DOS devices, including NUL.txt aliases.
    private static bool? WindowsRegular(string function, VmString original, string path)
    {
        string native;
        try
        {
            native = Path.GetFullPath(path);
            if (native.Length >= 248 && !native.StartsWith(@"\\?\", StringComparison.Ordinal) && !native.StartsWith(@"\\.\", StringComparison.Ordinal))
                native = native.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + native[2..] : @"\\?\" + native;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { throw Failure(function, original, e.Message); }
        using SafeFileHandle handle = CreateFileW(native, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error is 2 or 3) return null;
            throw Failure(function, original, new Win32Exception(error).Message);
        }
        uint kind = GetFileType(handle.DangerousGetHandle());
        if (kind != 1)
        {
            int error = Marshal.GetLastPInvokeError();
            if (kind == 0 && error != 0) throw Failure(function, original, new Win32Exception(error).Message);
            return false;
        }
        if (!GetFileInformationByHandle(handle, out FileInformation info))
            throw Failure(function, original, new Win32Exception(Marshal.GetLastPInvokeError()).Message);
        return (info.Attributes & 0x10) == 0;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh,
            WriteLow, WriteHigh, VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    private static void ValidatePath(string function, VmString path)
    {
        if (path.Span.IndexOf((byte)0) >= 0) throw Failure(function, path, "invalid argument");
        if (OperatingSystem.IsWindows())
        {
            try { _ = new UTF8Encoding(false, true).GetString(path.Span); }
            catch (DecoderFallbackException e) { throw Failure(function, path, e.Message); }
        }
    }
    private static bool TryTextPath(VmString path, out string? text)
    {
        try { text = OperatingSystem.IsWindows() ? new UTF8Encoding(false, true).GetString(path.Span) : null; return OperatingSystem.IsWindows(); }
        catch (DecoderFallbackException) { text = null; return false; }
    }
    private static Error Failure(string function, VmString path, string reason) => Error.Host($"{function}: {Display(path)}: {(string.IsNullOrEmpty(reason) ? "file operation failed" : reason)}");
    private static string Display(VmString path)
    {
        try { return new UTF8Encoding(false, true).GetString(path.Span); }
        catch (DecoderFallbackException) { return "bytes:" + Convert.ToHexString(path.Span).ToLowerInvariant(); }
    }
    private static string Errno(int error) => new Win32Exception(error).Message;

    [DllImport("libc", SetLastError = true, EntryPoint = "open")] private static extern int UnixOpen(byte[] path, int flags, int mode);
    // Apple arm64 puts variadic open(mode) in the first stack slot, after x0-x7.
    private static int NativeOpen(byte[] path, int flags, int mode) => OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? MacOpenArm64(path, flags, 0, 0, 0, 0, 0, 0, mode) : UnixOpen(path, flags, mode);
    [DllImport("libc", SetLastError = true, EntryPoint = "open")] private static extern int MacOpenArm64(byte[] path, int flags, nint pad2, nint pad3, nint pad4, nint pad5, nint pad6, nint pad7, nint mode);
    [DllImport("libc", SetLastError = true, EntryPoint = "read")] private static extern nint UnixRead(int fd, byte[] buffer, nuint count);
    [DllImport("libc", SetLastError = true, EntryPoint = "write")] private static extern nint UnixWriteRaw(int fd, byte[] buffer, nuint count);
    [DllImport("libc", SetLastError = true, EntryPoint = "write")] private static extern nint UnixWriteRaw(int fd, IntPtr buffer, nuint count);
    [DllImport("libc", SetLastError = true, EntryPoint = "fsync")] private static extern int UnixFsync(int fd);
    [DllImport("libc", SetLastError = true, EntryPoint = "close")] private static extern int UnixClose(int fd);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(IntPtr handle);
    private static byte[] UnixRead(string function, ReadOnlySpan<byte> path)
    {
        byte[] stat = new byte[512];
        if (UnixStat(NativePath(path), stat) != 0) throw Failure(function, new VmString(path), Errno(Marshal.GetLastPInvokeError()));
        if (!StatRegular(stat)) throw Failure(function, new VmString(path), "invalid argument");
        int fd = NativeOpen(NativePath(path), 0, 0); if (fd < 0) throw Failure(function, new VmString(path), Errno(Marshal.GetLastPInvokeError()));
        try { using var result = new MemoryStream(); var buffer = new byte[65536]; while (true) { nint n = UnixRead(fd, buffer, (nuint)buffer.Length); int error = Marshal.GetLastPInvokeError(); if (n == 0) break; if (n < 0 && error == 4) continue; if (n < 0) throw Failure(function, new VmString(path), Errno(error)); result.Write(buffer, 0, (int)n); } return result.ToArray(); }
        finally { if (UnixClose(fd) != 0) throw Failure(function, new VmString(path), Errno(Marshal.GetLastPInvokeError())); }
    }
    private static void UnixWrite(string function, ReadOnlySpan<byte> path, ReadOnlySpan<byte> bytes, bool append)
    {
        byte[] native = NativePath(path); byte[] stat = new byte[512]; int statResult = UnixStat(native, stat); if (statResult == 0 && !StatRegular(stat)) throw Failure(function, new VmString(path), "invalid argument"); if (statResult != 0 && Marshal.GetLastPInvokeError() is not (2 or 20)) throw Failure(function, new VmString(path), Errno(Marshal.GetLastPInvokeError()));
        int flags = 1 | (OperatingSystem.IsMacOS() ? 0x200 : 64) | (append ? (OperatingSystem.IsMacOS() ? 0x8 : 1024) : (OperatingSystem.IsMacOS() ? 0x400 : 512)); int fd = NativeOpen(native, flags, 438); if (fd < 0) throw Failure(function, new VmString(path), Errno(Marshal.GetLastPInvokeError()));
        try { byte[] data = bytes.ToArray(); var pinned = GCHandle.Alloc(data, GCHandleType.Pinned); try { int at = 0; while (at < data.Length) { nint n = UnixWriteRaw(fd, pinned.AddrOfPinnedObject() + at, (nuint)(data.Length - at)); int error = Marshal.GetLastPInvokeError(); if (n < 0 && error == 4) continue; if (n <= 0) throw Failure(function, new VmString(path), Errno(n == 0 ? 5 : error)); at += (int)n; } } finally { pinned.Free(); } int sync; do { sync = UnixFsync(fd); } while (sync != 0 && Marshal.GetLastPInvokeError() == 4); if (sync != 0) throw Failure(function, new VmString(path), Errno(Marshal.GetLastPInvokeError())); }
        finally { if (UnixClose(fd) != 0) throw Failure(function, new VmString(path), Errno(Marshal.GetLastPInvokeError())); }
    }
    private static byte[] NativePath(ReadOnlySpan<byte> path) { byte[] result = new byte[path.Length + 1]; path.CopyTo(result); return result; }
    private static bool StatRegular(byte[] stat)
    {
        int offset = OperatingSystem.IsMacOS() ? 4 : (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16 : 24); uint mode = BitConverter.ToUInt32(stat, offset); return (mode & 0xF000) == 0x8000;
    }
    private static int UnixStat(byte[] path, byte[] buffer) => OperatingSystem.IsMacOS() ? (RuntimeInformation.ProcessArchitecture == Architecture.X64 ? MacStat64(path, buffer) : MacStatNative(path, buffer)) : LinuxStat(path, buffer);
    [DllImport("libc", SetLastError = true, EntryPoint = "stat")] private static extern int LinuxStat(byte[] path, byte[] buffer);
    [DllImport("libc", SetLastError = true, EntryPoint = "stat$INODE64")] private static extern int MacStat64(byte[] path, byte[] buffer);
    [DllImport("libc", SetLastError = true, EntryPoint = "stat")] private static extern int MacStatNative(byte[] path, byte[] buffer);
    [DllImport("libc", SetLastError = true, EntryPoint = "fstat")] private static extern int Fstat(int fd, byte[] buffer);
}
