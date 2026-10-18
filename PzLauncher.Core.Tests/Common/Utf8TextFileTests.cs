using System.Text;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Common;

public sealed class Utf8TextFileTests
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private static readonly byte[] Body = Encoding.UTF8.GetBytes("a:\r\nB42·通用合集\n");

    private static Error Malformed(string reason) => ModCollectionErrors.FileMalformed("p", reason);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DecodeThenEncode_WithOrWithoutBom_KeepsTheBytes(bool hasBom)
    {
        byte[] bytes = hasBom ? [.. Bom, .. Body] : Body;

        var decoded = ResultAssert.Ok(Utf8TextFile.Decode(bytes, Malformed));

        Assert.Equal(hasBom, decoded.HasBom);
        Assert.Equal(bytes, Utf8TextFile.Encode(decoded.Text, decoded.HasBom));
    }

    [Fact]
    public void Decode_InvalidUtf8_FailsWithTheCallersError()
    {
        var error = ResultAssert.Failed(Utf8TextFile.Decode([.. Encoding.UTF8.GetBytes("a:"), 0xFF, 0xFE], Malformed), ErrorCode.ModCollectionFileMalformed);

        Assert.NotEmpty(error.Details["reason"]);
    }
}
