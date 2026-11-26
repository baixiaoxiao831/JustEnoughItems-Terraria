using System;

namespace JustEnoughItemsTerraria;

internal readonly record struct CatalogGrid(int Columns, int Rows, float RowHeight)
{
    internal static CatalogGrid Fit(float width, float height, float scale, int cellSize, int columns, int rows)
    {
        // Keep custom targets usable in the narrower backpack and terminal sidebars.
        float minimum = Math.Max(8f, 14f / scale);
        int FitAxis(float space, int target) => target <= 0
            ? Math.Max(1, (int)(space / cellSize))
            : Math.Clamp(target, 1, Math.Max(1, (int)(space / minimum)));

        int visibleRows = FitAxis(height, rows);
        float rowHeight = rows > 0 ? Math.Max(1f, height / visibleRows) : cellSize;
        return new(FitAxis(width, columns), visibleRows, rowHeight);
    }
}
