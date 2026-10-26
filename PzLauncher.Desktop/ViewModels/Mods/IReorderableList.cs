namespace PzLauncher.Desktop.ViewModels.Mods;

/// <summary>A list whose rows a drag reorders: the view moves rows by index in the order shown.</summary>
public interface IReorderableList
{
    /// <summary>Rows may be dragged to a new place now.</summary>
    bool CanReorder { get; }

    /// <summary>Indexes out of range change nothing.</summary>
    void Move(int from, int to);
}
