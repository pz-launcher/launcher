using System.Buffers.Binary;
using PzLauncher.Core.Java;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Java;

public sealed class JavaExecutableHeaderTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    public static TheoryData<byte[], bool?> Headers => new()
    {
        { JavaHomeBuilder.PeExecutable(JavaHomeBuilder.PeAmd64), true },
        { JavaHomeBuilder.PeExecutable(JavaHomeBuilder.PeArm64), true },
        { JavaHomeBuilder.PeExecutable(JavaHomeBuilder.PeI386), false },
        { JavaHomeBuilder.PeExecutable(0x0200), null },
        { JavaHomeBuilder.ElfExecutable(is64Bit: true), true },
        { JavaHomeBuilder.ElfExecutable(is64Bit: false), false },
        { [], null },
        { "#!/bin/sh\necho java\n"u8.ToArray(), null },
        { [(byte)'M', (byte)'Z', 0, 0], null },
        { PeWithOffset(0x7FFF_FFFF), null },
        { PeWithOffset(0x1000), null },
    };

    [Theory]
    [MemberData(nameof(Headers))]
    public async Task ReadArchAsync_Header_ReturnsBitnessOrUnknown(byte[] content, bool? is64Bit)
    {
        var path = Path.Combine(_temp.Path, "java.bin");
        await File.WriteAllBytesAsync(path, content, TestContext.Current.CancellationToken);

        var expected = is64Bit switch { true => JavaArch.Bits64, false => JavaArch.Bits32, null => JavaArch.Unknown };
        Assert.Equal(expected, await JavaExecutableHeader.ReadArchAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadArchAsync_MissingFile_ReturnsUnknown() =>
        Assert.Equal(
            JavaArch.Unknown,
            await JavaExecutableHeader.ReadArchAsync(Path.Combine(_temp.Path, "nope"), TestContext.Current.CancellationToken));

    private static byte[] PeWithOffset(int offset)
    {
        var bytes = JavaHomeBuilder.PeExecutable(JavaHomeBuilder.PeAmd64);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3C), offset);
        return bytes;
    }
}
