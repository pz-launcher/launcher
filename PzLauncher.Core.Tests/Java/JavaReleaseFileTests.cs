using PzLauncher.Core.Java;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Java;

public class JavaReleaseFileTests
{
    [Fact]
    public void Parse_PzBundledJre_ReadsVersionVendorAndArch()
    {
        var info = JavaReleaseFile.Parse(Fixtures.Read("Java", "release-pz-jre64"));

        Assert.Equal(new JavaReleaseInfo(new JavaVersion(25, "25.0.1"), "Azul Systems, Inc.", JavaArch.Bits64, HasGraalCompiler: false), info);
    }

    [Fact]
    public void Parse_MixedLineEndingsUnknownKeysAndMalformedLines_ReadsTheKnownKeys()
    {
        const string text = "IMPLEMENTOR=\"Eclipse Adoptium\"\r\nnot a key value line\r\n=\"orphan\"\nJAVA_VERSION=\"21.0.9\"\r\nFULL_VERSION=\"21.0.9+10-LTS\"\nOS_ARCH=\"amd64\"";

        var info = JavaReleaseFile.Parse(text);

        Assert.Equal(new JavaReleaseInfo(new JavaVersion(21, "21.0.9"), "Eclipse Adoptium", JavaArch.Bits64, HasGraalCompiler: false), info);
    }

    [Theory]
    [InlineData("IMPLEMENTOR=\"GraalVM Community\"\nJAVA_RUNTIME_VERSION=\"25.0.2+10-jvmci-b01\"\nGRAALVM_VERSION=\"25.0.2\"", true)]
    [InlineData("IMPLEMENTOR=\"Oracle Corporation\"\nGRAALVM_VERSION=\"25.0.1\"", true)]
    [InlineData("JAVA_RUNTIME_VERSION=\"21.0.2+13-jvmci-23.1-b30\"", true)]
    [InlineData("IMPLEMENTOR=\"Oracle Corporation\"\nJAVA_RUNTIME_VERSION=\"25.0.1+8-LTS-27\"", false)]
    [InlineData("GRAALVM_VERSION=\"\"", false)]
    public void Parse_GraalMarkers_SetHasGraalCompiler(string text, bool expected) =>
        Assert.Equal(expected, JavaReleaseFile.Parse(text).HasGraalCompiler);

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("IMPLEMENTOR=\"\"\nJAVA_VERSION=\"\"")]
    public void Parse_NothingUsable_ReturnsUnknowns(string text) =>
        Assert.Equal(JavaReleaseInfo.Empty, JavaReleaseFile.Parse(text));

    [Theory]
    [InlineData("1.8.0_392", 8)]
    [InlineData("1.7.0_80", 7)]
    [InlineData("17", 17)]
    [InlineData("9.0.4", 9)]
    [InlineData("21.0.9", 21)]
    [InlineData("11.0.9.1", 11)]
    [InlineData("26-ea", 26)]
    public void ParseVersion_KnownFormats_ReturnsMajorAndFullString(string text, int major) =>
        Assert.Equal(new JavaVersion(major, text), JavaReleaseFile.ParseVersion(text));

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("1.x")]
    public void ParseVersion_Unparsable_ReturnsNull(string? text) => Assert.Null(JavaReleaseFile.ParseVersion(text));

    [Theory]
    [InlineData("x86_64", true)]
    [InlineData("amd64", true)]
    [InlineData("aarch64", true)]
    [InlineData("X86_64", true)]
    [InlineData("x86", false)]
    [InlineData("i386", false)]
    [InlineData("i586", false)]
    [InlineData("ppc64le", null)]
    [InlineData(null, null)]
    public void ParseArch_OsArchValues_Classified(string? osArch, bool? is64Bit)
    {
        var expected = is64Bit switch { true => JavaArch.Bits64, false => JavaArch.Bits32, null => JavaArch.Unknown };
        Assert.Equal(expected, JavaReleaseFile.ParseArch(osArch));
    }
}
