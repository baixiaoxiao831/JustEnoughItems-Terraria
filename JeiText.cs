using Terraria.Localization;

namespace JustEnoughItemsTerraria;

internal static class JeiText
{
    private const string Prefix = "Mods.JustEnoughItemsTerraria.Browser.";
    internal static bool Chinese => Language.ActiveCulture.Name == "zh-Hans";
    internal static string Get(string key) => string.IsNullOrEmpty(key) ? "" : Language.GetTextValue(Prefix + key);
    internal static string Get(string key, params object[] args) => Language.GetTextValue(Prefix + key, args);
}
