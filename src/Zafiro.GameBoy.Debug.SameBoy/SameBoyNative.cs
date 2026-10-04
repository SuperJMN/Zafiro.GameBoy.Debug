using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;

namespace Zafiro.GameBoy.Debug.SameBoy;

internal static class SameBoyNative
{
    private const string LibraryName = "gameboy_debug_sameboy";

    static SameBoyNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(SameBoyNative).Assembly, ResolveLibrary);
    }

    [DllImport(LibraryName, EntryPoint = "gbmcp_create")]
    internal static extern IntPtr Create();

    [DllImport(LibraryName, EntryPoint = "gbmcp_destroy")]
    internal static extern void Destroy(IntPtr session);

    [DllImport(LibraryName, EntryPoint = "gbmcp_get_last_error", CharSet = CharSet.Ansi)]
    internal static extern int GetLastError(IntPtr session, StringBuilder buffer, UIntPtr bufferLength);

    [DllImport(LibraryName, EntryPoint = "gbmcp_load_rom", CharSet = CharSet.Ansi)]
    internal static extern int LoadRom(
        IntPtr session,
        string path,
        StringBuilder title,
        UIntPtr titleLength,
        StringBuilder model,
        UIntPtr modelLength);

    [DllImport(LibraryName, EntryPoint = "gbmcp_reset")]
    internal static extern int Reset(IntPtr session);

    [DllImport(LibraryName, EntryPoint = "gbmcp_save_state", CharSet = CharSet.Ansi)]
    internal static extern int SaveState(IntPtr session, string path);

    [DllImport(LibraryName, EntryPoint = "gbmcp_load_state", CharSet = CharSet.Ansi)]
    internal static extern int LoadState(IntPtr session, string path);

    [DllImport(LibraryName, EntryPoint = "gbmcp_step")]
    internal static extern int Step(IntPtr session);

    [DllImport(LibraryName, EntryPoint = "gbmcp_run_frame")]
    internal static extern int RunFrame(IntPtr session);

    [DllImport(LibraryName, EntryPoint = "gbmcp_set_joypad")]
    internal static extern int SetJoypad(IntPtr session, byte buttonMask);

    [DllImport(LibraryName, EntryPoint = "gbmcp_read_registers")]
    internal static extern int ReadRegisters(IntPtr session, out NativeRegisters registers);

    [DllImport(LibraryName, EntryPoint = "gbmcp_read_memory")]
    internal static extern int ReadMemory(IntPtr session, ushort address, byte[] buffer, UIntPtr length);

    [DllImport(LibraryName, EntryPoint = "gbmcp_write_memory")]
    internal static extern int WriteMemory(IntPtr session, ushort address, byte[] buffer, UIntPtr length);

    [DllImport(LibraryName, EntryPoint = "gbmcp_disassemble", CharSet = CharSet.Ansi)]
    internal static extern int Disassemble(IntPtr session, ushort address, ushort count, StringBuilder buffer, UIntPtr bufferLength);

    [DllImport(LibraryName, EntryPoint = "gbmcp_read_oam")]
    internal static extern int ReadOam(IntPtr session, byte[] buffer, UIntPtr length);

    [DllImport(LibraryName, EntryPoint = "gbmcp_capture_screen")]
    internal static extern int CaptureScreen(IntPtr session, uint[] buffer, UIntPtr length);

    [DllImport(LibraryName, EntryPoint = "gbmcp_get_last_writer")]
    internal static extern int GetLastWriter(IntPtr session, ushort address, out ushort pc, out byte value, out ulong count);

    [DllImport(LibraryName, EntryPoint = "gbmcp_trace_until_write")]
    internal static extern int TraceUntilWrite(
        IntPtr session,
        ushort address,
        uint maxInstructions,
        out uint instructionsRun,
        out ushort pc,
        out byte value);

    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
        {
            return IntPtr.Zero;
        }

        foreach (var candidate in EnumerateLibraryCandidates())
        {
            if (File.Exists(candidate))
            {
                return NativeLibrary.Load(candidate, assembly, searchPath);
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> EnumerateLibraryCandidates()
    {
        var fileName = GetLibraryFileName();
        var nativeDir = Environment.GetEnvironmentVariable("GAMEBOY_DEBUG_MCP_NATIVE_DIR");
        if (!string.IsNullOrWhiteSpace(nativeDir))
        {
            yield return Path.Combine(nativeDir, fileName);
        }

        foreach (var root in EnumerateRoots(AppContext.BaseDirectory).Concat(EnumerateRoots(Directory.GetCurrentDirectory())))
        {
            yield return Path.Combine(root, fileName);
            yield return Path.Combine(root, "native", "out", GetRuntimeId(), fileName);
        }
    }

    private static IEnumerable<string> EnumerateRoots(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }

    private static string GetRuntimeId()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            return "linux-x64";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            return "osx-arm64";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            return "osx-x64";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            return "win-x64";
        }

        return RuntimeInformation.RuntimeIdentifier;
    }

    private static string GetLibraryFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "gameboy_debug_sameboy.dll";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "libgameboy_debug_sameboy.dylib";
        }

        return "libgameboy_debug_sameboy.so";
    }
}
