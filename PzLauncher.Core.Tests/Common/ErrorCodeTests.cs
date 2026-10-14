using System.Text.Json;
using System.Text.RegularExpressions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Serialization;

namespace PzLauncher.Core.Tests.Common;

public partial class ErrorCodeTests
{
    public static TheoryData<ErrorCode> AllCodes => [.. Enum.GetValues<ErrorCode>()];

    [GeneratedRegex(@"^[a-z]+\.[a-z]+(_[a-z]+)*$")]
    private static partial Regex WireNameFormat();

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void ToWireName_EveryCode_MatchesFeatureDotSnakeCase(ErrorCode code)
    {
        Assert.Matches(WireNameFormat(), code.ToWireName());
    }

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
    public void ToWireName_ExistingCodes_AreStable(ErrorCode code, string wireName)
    {
        Assert.Equal(wireName, code.ToWireName());
    }
}
