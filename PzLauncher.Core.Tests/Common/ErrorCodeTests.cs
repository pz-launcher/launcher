using System.Text.Json;
using System.Text.RegularExpressions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Serialization;
using PzLauncher.Core.Steam;

namespace PzLauncher.Core.Tests.Common;

public partial class ErrorCodeTests
{
    public static TheoryData<ErrorCode> AllCodes => [.. Enum.GetValues<ErrorCode>()];

    private static readonly Dictionary<ErrorCode, (Error Error, ErrorKind Kind)> Factories = new()
    {
        [ErrorCode.ResultUninitialized] = (ResultErrors.Uninitialized, ErrorKind.Unexpected),
        [ErrorCode.SteamNotInstalled] = (SteamErrors.NotInstalled(), ErrorKind.NotFound),
        [ErrorCode.SteamLibraryUnreadable] = (SteamErrors.LibraryUnreadable("p", "r"), ErrorKind.External),
        [ErrorCode.SteamManifestUnreadable] = (SteamErrors.ManifestUnreadable("p", "r"), ErrorKind.External),
        [ErrorCode.SteamVdfMalformed] = (SteamErrors.VdfMalformed("r"), ErrorKind.Validation),
        [ErrorCode.InstallFilesMissing] = (InstallErrors.FilesMissing("p", 1), ErrorKind.NotFound),
        [ErrorCode.InstallInvalidPath] = (InstallErrors.InvalidPath("p"), ErrorKind.Validation),
        [ErrorCode.InstallPathNotFound] = (InstallErrors.PathNotFound("p"), ErrorKind.NotFound),
        [ErrorCode.InstallPathUnreadable] = (InstallErrors.PathUnreadable("p", "r"), ErrorKind.External),
        [ErrorCode.InstallNotAPzInstall] = (InstallErrors.NotAPzInstall("p"), ErrorKind.Validation),
        [ErrorCode.JavaInvalidPath] = (JavaErrors.InvalidPath("p"), ErrorKind.Validation),
        [ErrorCode.JavaPathNotFound] = (JavaErrors.PathNotFound("p"), ErrorKind.NotFound),
        [ErrorCode.JavaNotAJavaHome] = (JavaErrors.NotAJavaHome("p"), ErrorKind.Validation),
        [ErrorCode.JavaNot64Bit] = (JavaErrors.Not64Bit("p"), ErrorKind.Validation),
        [ErrorCode.JavaSourceUnreadable] = (JavaErrors.SourceUnreadable("p", "r"), ErrorKind.External),
        [ErrorCode.JavaReleaseUnreadable] = (JavaErrors.ReleaseUnreadable("p", "r"), ErrorKind.External),
    };

    [GeneratedRegex(@"^[a-z]+\.[a-z0-9]+(_[a-z0-9]+)*$")]
    private static partial Regex WireNameFormat();

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void ToWireName_EveryCode_MatchesFeatureDotSnakeCase(ErrorCode code) =>
        Assert.Matches(WireNameFormat(), code.ToWireName());

    [Fact]
    public void ToWireName_AllCodes_AreUnique()
    {
        var names = Enum.GetValues<ErrorCode>().Select(c => c.ToWireName()).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void Json_EveryCode_RoundTripsThroughWireName(ErrorCode code)
    {
        var json = JsonSerializer.Serialize(code, CoreJsonContext.Default.ErrorCode);

        Assert.Equal($"\"{code.ToWireName()}\"", json);
        Assert.Equal(code, JsonSerializer.Deserialize(json, CoreJsonContext.Default.ErrorCode));
    }

    // Wire names are a public contract: this pins the existing ones so a rename shows up in review.
    [Theory]
    [InlineData(ErrorCode.ResultUninitialized, "result.uninitialized")]
    [InlineData(ErrorCode.SteamNotInstalled, "steam.not_installed")]
    [InlineData(ErrorCode.SteamLibraryUnreadable, "steam.library_unreadable")]
    [InlineData(ErrorCode.SteamManifestUnreadable, "steam.manifest_unreadable")]
    [InlineData(ErrorCode.SteamVdfMalformed, "steam.vdf_malformed")]
    [InlineData(ErrorCode.InstallFilesMissing, "install.files_missing")]
    [InlineData(ErrorCode.InstallInvalidPath, "install.invalid_path")]
    [InlineData(ErrorCode.InstallPathNotFound, "install.path_not_found")]
    [InlineData(ErrorCode.InstallPathUnreadable, "install.path_unreadable")]
    [InlineData(ErrorCode.InstallNotAPzInstall, "install.not_a_pz_install")]
    [InlineData(ErrorCode.JavaInvalidPath, "java.invalid_path")]
    [InlineData(ErrorCode.JavaPathNotFound, "java.path_not_found")]
    [InlineData(ErrorCode.JavaNotAJavaHome, "java.not_a_java_home")]
    [InlineData(ErrorCode.JavaNot64Bit, "java.not_64_bit")]
    [InlineData(ErrorCode.JavaSourceUnreadable, "java.source_unreadable")]
    [InlineData(ErrorCode.JavaReleaseUnreadable, "java.release_unreadable")]
    public void ToWireName_ExistingCodes_AreStable(ErrorCode code, string wireName) =>
        Assert.Equal(wireName, code.ToWireName());

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void Factory_EveryCode_BuildsThatCodeWithItsKind(ErrorCode code)
    {
        Assert.True(Factories.TryGetValue(code, out var entry), $"Add {code} to {nameof(Factories)}.");
        Assert.Equal(code, entry.Error.Code);
        Assert.Equal(entry.Kind, entry.Error.Kind);
    }
}
