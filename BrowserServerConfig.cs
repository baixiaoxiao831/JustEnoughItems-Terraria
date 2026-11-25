using Terraria;
using Terraria.Localization;
using Terraria.ModLoader.Config;

namespace JustEnoughItemsTerraria;

public sealed class BrowserServerConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ServerSide;
    public bool AllowCheatItems;

    public override bool AcceptClientChanges(ModConfig pendingConfig, int whoAmI, ref NetworkText message)
    {
        if (whoAmI >= 0 && whoAmI < Main.maxPlayers && Netplay.Clients?[whoAmI]?.Socket != null &&
            NetMessage.DoesPlayerSlotCountAsAHost(whoAmI)) return true;
        message = NetworkText.FromKey("Mods.JustEnoughItemsTerraria.HostOnly");
        return false;
    }

    public override void OnChanged()
    {
        if (!AllowCheatItems) BrowserUI.DisableCheats();
    }
}
