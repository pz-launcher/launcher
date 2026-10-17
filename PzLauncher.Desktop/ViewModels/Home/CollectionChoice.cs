namespace PzLauncher.Desktop.ViewModels.Home;

/// <summary>An entry of Home's collection picker; <see cref="None"/> launches with the game's enabled list as it is.</summary>
public sealed record CollectionChoice(string? Name)
{
    public static CollectionChoice None { get; } = new((string?)null);

    public bool IsNone => Name is null;
}
