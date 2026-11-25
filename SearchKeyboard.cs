using Terraria;

namespace JustEnoughItemsTerraria;

internal static class SearchKeyboard
{
    internal static string ReadText(string current) =>
        Main.GetInputText(current).Replace('＠', '@').Replace('＃', '#');
}