using System;
using System.Collections.Generic;
using System.Linq;

namespace PzLauncher.Desktop.ViewModels.Mods;

/// <summary>The sort behind a list's sortable column headers.</summary>
internal static class SortedRows
{
    /// <param name="tieBreak">Orders rows the column doesn't, ascending in both directions.</param>
    public static TRow[] Sort<TRow, TColumn>(
        IEnumerable<TRow> rows,
        TColumn column,
        bool descending,
        Func<TRow, TColumn, bool> isBlank,
        Func<TRow, TRow, TColumn, int> compareColumn,
        Comparison<TRow> tieBreak)
    {
        var sign = descending ? -1 : 1;
        return [.. rows.Order(Comparer<TRow>.Create((x, y) =>
        {
            // Blank cells stay at the bottom in both directions.
            var xBlank = isBlank(x, column);
            var yBlank = isBlank(y, column);
            if (xBlank != yBlank)
            {
                return xBlank ? 1 : -1;
            }

            var order = xBlank ? 0 : sign * compareColumn(x, y, column);
            return order != 0 ? order : tieBreak(x, y);
        }))];
    }
}
