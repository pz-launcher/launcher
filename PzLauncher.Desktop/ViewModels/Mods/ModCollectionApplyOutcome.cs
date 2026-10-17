using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Collections;

namespace PzLauncher.Desktop.ViewModels.Mods;

public abstract record ModCollectionApplyOutcome
{
    private ModCollectionApplyOutcome()
    {
    }

    public sealed record Applied(ApplyModCollectionResult Result) : ModCollectionApplyOutcome;

    /// <summary>The user cancelled a prompt; <c>default.txt</c> was not written.</summary>
    public sealed record Cancelled : ModCollectionApplyOutcome;

    public sealed record Failed(Error Error) : ModCollectionApplyOutcome;
}
