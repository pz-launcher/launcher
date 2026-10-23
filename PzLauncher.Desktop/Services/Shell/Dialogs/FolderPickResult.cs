namespace PzLauncher.Desktop.Services.Shell.Dialogs;

public abstract record FolderPickResult
{
    private FolderPickResult()
    {
    }

    public sealed record Picked(string Path) : FolderPickResult;

    public sealed record Cancelled : FolderPickResult;

    /// <summary>The user picked something with no local file system path (e.g. a remote location from a Linux portal).</summary>
    public sealed record NotLocal(string Location) : FolderPickResult;
}
