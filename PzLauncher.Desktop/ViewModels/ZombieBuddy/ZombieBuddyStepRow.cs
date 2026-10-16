using CommunityToolkit.Mvvm.ComponentModel;
using PzLauncher.Core.ZombieBuddy;

namespace PzLauncher.Desktop.ViewModels.ZombieBuddy;

public enum ZombieBuddyStepState
{
    Pending,
    Running,
    Done,
    Failed,
}

public sealed partial class ZombieBuddyStepRow(ZombieBuddyStep step) : ObservableObject
{
    public ZombieBuddyStep Step { get; } = step;

    [ObservableProperty] public partial ZombieBuddyStepState State { get; internal set; }
}
