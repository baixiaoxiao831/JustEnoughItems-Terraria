using System;
using Microsoft.Xna.Framework;
using ReLogic.Localization.IME;
using ReLogic.OS;

namespace JustEnoughItemsTerraria;

public static partial class BrowserUI
{
    private static CatalogFilters suggestionCatalog;
    private static string suggestionQuery;
    private static int suggestionCaret, suggestionStart, suggestionEnd, suggestionOffset;
    private static int suggestionIndex = -1;
    private static ModSuggestion[] suggestions = [];

    private static Rectangle SuggestionArea(Rectangle field, int top, out int rowHeight, out int visible)
    {
        rowHeight = Math.Max(16, (int)MathF.Round(16 * unit));
        visible = 0;
        if (!IsTyping || InputBlocked) return Rectangle.Empty;
        var ime = Platform.Get<IImeService>();
        if (ime.IsCandidateListVisible || !string.IsNullOrEmpty(ime.CompositionString)) return Rectangle.Empty;
        int caret = searchSelection.Caret;
        if (suggestionCatalog != index.CatalogFilters || suggestionQuery != search || suggestionCaret != caret)
        {
            suggestionCatalog = index.CatalogFilters;
            suggestionQuery = search;
            suggestionCaret = caret;
            suggestionOffset = 0;
            suggestionIndex = -1;
            suggestions = ModSuggestions.TryToken(search, caret, out suggestionStart, out suggestionEnd, out var prefix)
                ? ModSuggestions.Find(suggestionCatalog.Mods, prefix) : [];
        }
        visible = Math.Min(Math.Min(8, suggestions.Length), Math.Max(0, (field.Top - top - 4) / rowHeight));
        if (visible == 0) return Rectangle.Empty;
        suggestionOffset = Math.Clamp(suggestionOffset, 0, suggestions.Length - visible);
        return new Rectangle(field.Left, field.Top - visible * rowHeight - 3, field.Width, visible * rowHeight);
    }

    private static void CompleteNextSuggestion(int visible)
    {
        if (visible == 0) return;
        suggestionIndex = Math.Min(suggestionIndex + 1, suggestions.Length - 1);
        suggestionOffset = Math.Clamp(suggestionOffset, Math.Max(0, suggestionIndex - visible + 1), suggestionIndex);
        string token = "@\"" + suggestions[suggestionIndex].Id + "\"";
        SetQueryText(search[..suggestionStart] + token + search[suggestionEnd..]);
        suggestionEnd = suggestionStart + token.Length;
        searchSelection.Focus(search, suggestionEnd);
        // Retain the original prefix matches while Tab walks the list.
        suggestionQuery = search;
        suggestionCaret = suggestionEnd;
        recalled = -1;
    }

    private static void DrawSuggestions(Rectangle area, int rowHeight, int visible, int wheelDelta)
    {
        if (area.IsEmpty) return;
        int hover = area.Contains(mouse) ? (mouse.Y - area.Y) / rowHeight : -1;
        int maxOffset = suggestions.Length - visible;
        if (hover >= 0 && wheelDelta != 0)
            suggestionOffset = Math.Clamp(suggestionOffset + Math.Sign(wheelDelta) * Math.Max(1, Math.Abs(wheelDelta / 120)), 0, maxOffset);
        for (int i = 0; i < visible; i++)
        {
            var option = suggestions[suggestionOffset + i];
            var row = new Rectangle(area.X, area.Y + i * rowHeight, area.Width, rowHeight);
            Fill(row, i + suggestionOffset == suggestionIndex ? new Color(48, 88, 137) : i == hover ? new Color(60, 78, 117) : new Color(29, 32, 42));
            var label = new Rectangle(row.X + 5, row.Y, Math.Max(1, row.Width - 14), row.Height);
            Label(option.Name, label, 12, Color.White, align: 0);
            if (i == hover && leftClick)
            {
                SetQueryText(search[..suggestionStart] + "@\"" + option.Id + "\"" + search[suggestionEnd..]);
                EndTyping();
                PlayClick();
            }
        }
        if (maxOffset > 0)
        {
            int thumb = Math.Max(6, area.Height * visible / suggestions.Length);
            int y = area.Y + (area.Height - thumb) * suggestionOffset / maxOffset;
            Fill(new Rectangle(area.Right - 4, y, 3, thumb), Color.LightGray);
        }
    }
}
