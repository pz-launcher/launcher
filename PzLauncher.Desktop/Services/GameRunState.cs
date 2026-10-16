using CommunityToolkit.Mvvm.ComponentModel;

namespace PzLauncher.Desktop.Services;

/// <summary>App-lifetime: Home sets it around a launch session; pages that must not touch game files while it runs observe it.</summary>
public sealed partial class GameRunState : ObservableObject
{
    /// <summary>A game started from this launcher session is running, whichever install it came from.</summary>
    [ObservableProperty] public partial bool IsGameRunning { get; set; }
}
