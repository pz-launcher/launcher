using System.Buffers.Binary;
using PzLauncher.Core.Java;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Tests.TestSupport;

/// <summary>Fake Java homes: <c>bin/&lt;exe&gt;</c> for the given OS plus a <c>release</c> file.</summary>
public sealed class JavaHomeBuilder(string baseDirectory, PlatformKind os)
{
    public const ushort PeAmd64 = 0x8664;
    public const ushort PeArm64 = 0xAA64;
    public const ushort PeI386 = 0x014C;

    public string ExecutableName => JavaLocations.ExecutableName(os);

    public static string Release(string? version = "21.0.9", string? vendor = "Eclipse Adoptium", string? arch = "x86_64", string? graalVersion = null)
    {
        var lines = new List<string>();
        if (vendor is not null)
        {
            lines.Add($"IMPLEMENTOR=\"{vendor}\"");
        }

        if (version is not null)
        {
            lines.Add($"JAVA_VERSION=\"{version}\"");
        }

        if (arch is not null)
        {
            lines.Add($"OS_ARCH=\"{arch}\"");
        }

        if (graalVersion is not null)
        {
            lines.Add($"GRAALVM_VERSION=\"{graalVersion}\"");
        }

        return string.Join("\r\n", lines) + "\r\n";
    }

    public static byte[] PeExecutable(ushort machine)
    {
        const int peOffset = 0x80;
        var bytes = new byte[peOffset + 6];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3C), peOffset);
        "PE\0\0"u8.CopyTo(bytes.AsSpan(peOffset));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(peOffset + 4), machine);
        return bytes;
    }

    public static byte[] ElfExecutable(bool is64Bit) => [0x7F, (byte)'E', (byte)'L', (byte)'F', is64Bit ? (byte)2 : (byte)1, 1, 1, 0];

    /// <summary>A 64-bit home whose <c>release</c> says so; returns the home path.</summary>
    public string Create(string relativePath, string? release = null, byte[]? executable = null)
    {
        var home = Path.Combine(baseDirectory, relativePath);
        var bin = Directory.CreateDirectory(Path.Combine(home, JavaLocations.BinFolder));
        File.WriteAllBytes(Path.Combine(bin.FullName, ExecutableName), executable ?? []);
        File.WriteAllText(Path.Combine(home, JavaLocations.ReleaseFile), release ?? Release());
        return home;
    }

    /// <summary>A home without a <c>release</c> file; the executable header decides the bitness.</summary>
    public string CreateWithoutRelease(string relativePath, byte[] executable)
    {
        var home = Create(relativePath, executable: executable);
        File.Delete(Path.Combine(home, JavaLocations.ReleaseFile));
        return home;
    }

    public string ExecutableOf(string home) => Path.Combine(home, JavaLocations.BinFolder, ExecutableName);
}
