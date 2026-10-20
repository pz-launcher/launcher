using System;
using Avalonia.Controls;

namespace PzLauncher.Desktop.Views.Mods.Workshop;

/// <summary>
/// The columns of a Workshop item row and of the header above the rows, so both line up: selection, title, item id, mods, size, updated,
/// state, collections, star, Steam page. Selection and updated take their cells' fixed width, none when the list hides them.
/// </summary>
public sealed class WorkshopItemGrid : Grid
{
    public WorkshopItemGrid()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,2*,120,1.5*,90,Auto,130,1.5*,32,40");
    }

    protected override Type StyleKeyOverride => typeof(Grid);
}
