using CommunityToolkit.Mvvm.ComponentModel;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

public enum WorkshopActionItemState
{
    Pending,
    Running,
    Done,
    Failed,
}

/// <param name="name">The item's title, else its id.</param>
public sealed partial class WorkshopActionItemRow(ulong id, string name) : ObservableObject
{
    public ulong Id { get; } = id;

    public string Name { get; } = name;

    [ObservableProperty] public partial WorkshopActionItemState State { get; private set; }

    /// <summary>0 to 100 while downloading; <c>null</c> until Steam knows the size.</summary>
    [ObservableProperty] public partial int? Percent { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailure))]
    public partial WorkshopItemFailure? Failure { get; private set; }

    /// <summary>Steam's EResult for <see cref="Failure"/>; 0 when Steam gave none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSteamResult))]
    public partial int SteamResult { get; private set; }

    /// <summary>Steam reported the download done, but the installed copy still differs from the latest version.</summary>
    [ObservableProperty] public partial bool IsStillOutdated { get; private set; }

    public bool HasFailure => Failure is not null;

    public bool HasSteamResult => SteamResult != 0;

    internal void ShowProgress(ulong bytes, ulong total)
    {
        State = WorkshopActionItemState.Running;
        Percent = ProgressPercent.Of(bytes, total);
    }

    internal void Finish(WorkshopItemOutcome outcome)
    {
        Failure = outcome.Failure;
        SteamResult = outcome.SteamResult;
        State = outcome.Failure is null ? WorkshopActionItemState.Done : WorkshopActionItemState.Failed;
    }

    internal void MarkStillOutdated()
    {
        IsStillOutdated = true;
        State = WorkshopActionItemState.Failed;
    }
}
