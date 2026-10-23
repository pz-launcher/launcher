using System.Collections;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PzLauncher.Core.Platform;

/// <summary><c>CreateProcessW</c> with no console and <c>NUL</c> std handles, which <c>Process.Start</c> can't do.</summary>
internal static unsafe partial class WindowsDetachedStart
{
    private const uint DetachedProcessFlag = 0x00000008;
    private const uint CreateNewProcessGroup = 0x00000200;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const int StartfUseStdHandles = 0x00000100;
    private const nint ProcThreadAttributeHandleList = 0x00020002;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;

    [SupportedOSPlatform("windows")]
    public static DetachedProcess Start(ProcessStartRequest request)
    {
        using var nul = OpenInheritableNul();
        var commandLine = (CommandLine(request.FileName, request.Arguments) + '\0').ToCharArray();
        var environment = EnvironmentBlock(request.Environment);
        var flags = DetachedProcessFlag | CreateNewProcessGroup | CreateUnicodeEnvironment | ExtendedStartupInfoPresent;

        nint listSize = 0;
        InitializeProcThreadAttributeList(null, 1, 0, &listSize);
        var attributes = NativeMemory.Alloc((nuint)listSize);
        var initialized = false;
        var addedRef = false;
        try
        {
            nul.DangerousAddRef(ref addedRef);
            var handle = nul.DangerousGetHandle();
            initialized = InitializeProcThreadAttributeList(attributes, 1, 0, &listSize);
            if (!initialized)
            {
                throw new Win32Exception();
            }

            // Only NUL is inherited: any other inheritable handle (e.g. a concurrent Process.Start's pipe) would stay open in the server.
            if (!UpdateProcThreadAttribute(attributes, 0, ProcThreadAttributeHandleList, &handle, sizeof(nint), null, null))
            {
                throw new Win32Exception();
            }

            var startup = new StartupInfoEx { AttributeList = attributes };
            startup.StartupInfo.Size = sizeof(StartupInfoEx);
            startup.StartupInfo.Flags = StartfUseStdHandles;
            startup.StartupInfo.StdInput = handle;
            startup.StartupInfo.StdOutput = handle;
            startup.StartupInfo.StdError = handle;

            ProcessInformation info;
            fixed (char* application = request.FileName)
            fixed (char* command = commandLine)
            fixed (char* block = environment)
            fixed (char* directory = request.WorkingDirectory)
            {
                if (!CreateProcess(application, command, null, null, true, flags, block, directory, &startup, &info))
                {
                    throw new Win32Exception();
                }
            }

            try
            {
                long creation, exit, kernel, user;
                return GetProcessTimes(info.Process, &creation, &exit, &kernel, &user)
                    ? new DetachedProcess(info.ProcessId, new DateTimeOffset(DateTime.FromFileTimeUtc(creation)))
                    : throw new Win32Exception();
            }
            finally
            {
                CloseHandle(info.Process);
                CloseHandle(info.Thread);
            }
        }
        finally
        {
            if (initialized)
            {
                DeleteProcThreadAttributeList(attributes);
            }

            NativeMemory.Free(attributes);
            if (addedRef)
            {
                nul.DangerousRelease();
            }
        }
    }

    // The MSVCRT rules the JVM's launcher parses with.
    internal static string CommandLine(string fileName, IReadOnlyList<string> arguments)
    {
        var line = new StringBuilder();
        line.Append('"').Append(fileName).Append('"');
        foreach (var argument in arguments)
        {
            line.Append(' ');
            AppendQuoted(line, argument);
        }

        return line.ToString();
    }

    private static void AppendQuoted(StringBuilder line, string argument)
    {
        if (argument.Length > 0 && argument.AsSpan().IndexOfAny(" \t\n\v\"") < 0)
        {
            line.Append(argument);
            return;
        }

        line.Append('"');
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            line.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes).Append(c);
            backslashes = 0;
        }

        line.Append('\\', backslashes * 2).Append('"');
    }

    // null inherits the caller's environment as is.
    private static char[]? EnvironmentBlock(IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides.Count == 0)
        {
            return null;
        }

        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            variables[(string)entry.Key] = entry.Value as string ?? "";
        }

        foreach (var (name, value) in overrides)
        {
            variables[name] = value;
        }

        var block = new StringBuilder();
        foreach (var (name, value) in variables)
        {
            block.Append(name).Append('=').Append(value).Append('\0');
        }

        return block.Append('\0').ToString().ToCharArray();
    }

    private static SafeFileHandle OpenInheritableNul()
    {
        var security = new SecurityAttributes { Length = sizeof(SecurityAttributes), InheritHandle = 1 };
        var handle = CreateFile("NUL", GenericRead | GenericWrite, FileShareReadWrite, &security, OpenExisting, 0, 0);
        return handle.IsInvalid ? throw new Win32Exception() : handle;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(
        string fileName, uint access, uint share, SecurityAttributes* security, uint creation, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcess(
        char* applicationName,
        char* commandLine,
        void* processAttributes,
        void* threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        char* environment,
        char* currentDirectory,
        StartupInfoEx* startupInfo,
        ProcessInformation* processInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(void* list, int count, int flags, nint* size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(
        void* list, uint flags, nint attribute, void* value, nint size, void* previousValue, nint* returnSize);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(void* list);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(nint process, long* creation, long* exit, long* kernel, long* user);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public void* SecurityDescriptor;
        public int InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public char* Reserved;
        public char* Desktop;
        public char* Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public byte* Reserved2;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public void* AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }
}
