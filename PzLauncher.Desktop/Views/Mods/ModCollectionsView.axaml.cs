using Avalonia.Controls;
using PzLauncher.Desktop.ViewModels.Mods;

namespace PzLauncher.Desktop.Views.Mods;

public partial class ModCollectionsView : UserControl
{
    /// <summary>Every entry row's height, spacing included: the list is virtualized, so the drag counts rows instead of measuring them.</summary>
    public const double EntryRowHeight = 44;

    public ModCollectionsView()
    {
        InitializeComponent();
        _ = new ListReorderDrag(this, EntriesList, EntriesScroller, DragFloat, EntryRowHeight, () => DataContext as IReorderableList);
    }
}
