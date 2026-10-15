using System.Buffers.Binary;

namespace PzLauncher.Core.Java;

// Bitness from the PE (Windows) or ELF (Linux) header, for runtimes whose release file has no OS_ARCH.
internal static class JavaExecutableHeader
{
    private const int PeHeaderOffsetField = 0x3C;
    private const int MaxPeHeaderOffset = 64 * 1024;

    // Unreadable or unrecognised files are Unknown: the caller treats them as "not a Java".
    public static async Task<JavaArch> ReadArchAsync(string executablePath, CancellationToken ct)
    {
        try
        {
            var stream = new FileStream(
                executablePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, FileOptions.Asynchronous);
            await using var _ = stream.ConfigureAwait(false);
            var start = new byte[PeHeaderOffsetField + 4];
            var read = await stream.ReadAtLeastAsync(start, start.Length, throwOnEndOfStream: false, ct).ConfigureAwait(false);

            if (read >= 5 && start[0] == 0x7F && start[1] == (byte)'E' && start[2] == (byte)'L' && start[3] == (byte)'F')
            {
                return start[4] switch
                {
                    2 => JavaArch.Bits64,
                    1 => JavaArch.Bits32,
                    _ => JavaArch.Unknown,
                };
            }

            if (read < start.Length || start[0] != (byte)'M' || start[1] != (byte)'Z')
            {
                return JavaArch.Unknown;
            }

            var peOffset = BinaryPrimitives.ReadInt32LittleEndian(start.AsSpan(PeHeaderOffsetField));
            if (peOffset < start.Length || peOffset > MaxPeHeaderOffset)
            {
                return JavaArch.Unknown;
            }

            stream.Position = peOffset;
            var pe = new byte[6];
            if (await stream.ReadAtLeastAsync(pe, pe.Length, throwOnEndOfStream: false, ct).ConfigureAwait(false) < pe.Length
                || pe[0] != (byte)'P' || pe[1] != (byte)'E' || pe[2] != 0 || pe[3] != 0)
            {
                return JavaArch.Unknown;
            }

            return BinaryPrimitives.ReadUInt16LittleEndian(pe.AsSpan(4)) switch
            {
                0x8664 or 0xAA64 => JavaArch.Bits64,
                0x014C or 0x01C4 => JavaArch.Bits32,
                _ => JavaArch.Unknown,
            };
        }
        catch (IOException)
        {
            return JavaArch.Unknown;
        }
        catch (UnauthorizedAccessException)
        {
            return JavaArch.Unknown;
        }
    }
}
