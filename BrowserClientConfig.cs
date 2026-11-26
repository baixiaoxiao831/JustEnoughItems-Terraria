using System.ComponentModel;
using Newtonsoft.Json;
using Terraria.Localization;
using Terraria.ModLoader.Config;

namespace JustEnoughItemsTerraria;

public sealed class BrowserClientConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ClientSide;

    [Range(-1, 1)]
    [DefaultValue(0)]
    public int Density;

    [Range(0, 64), DefaultValue(0)]
    public int CatalogColumns;

    [Range(0, 64), DefaultValue(0)]
    public int CatalogRows;

    [Range(-1, 1)]
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
    public int TerminalDensity;

    [System.Runtime.Serialization.OnDeserializing]
    private void BeforeRead(System.Runtime.Serialization.StreamingContext context) => TerminalDensity = int.MinValue;

    [System.Runtime.Serialization.OnDeserialized]
    private void AfterRead(System.Runtime.Serialization.StreamingContext context)
    {
        if (TerminalDensity == int.MinValue) TerminalDensity = Density;
    }

    [DefaultValue(false)]
    public bool BackpackMultiStepCrafting;

    public bool NewBookmarksFirst;

    [JsonIgnore, ShowDespiteJsonIgnore]
    public string SearchHelp => Language.GetTextValue("Mods.JustEnoughItemsTerraria.HoverForHelp");
}
