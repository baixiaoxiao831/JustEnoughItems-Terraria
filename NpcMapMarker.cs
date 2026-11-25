using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.Map;
using Terraria.ModLoader;
using Terraria.UI;

namespace JustEnoughItemsTerraria;

[Autoload(Side = ModSide.Client)]
public sealed class NpcMapMarker : ModMapLayer
{
    private static NPC target;
    private static bool opening;
    private static int npcId;

    internal static void Track(NPC npc, int id)
    {
        target = npc;
        opening = true;
        npcId = id;
    }

    internal static void Clear()
    {
        target = null;
        opening = false;
    }

    internal static void Update()
    {
        if(target==null)return;
        if(Main.gameMenu || Main.LocalPlayer.dead || !Main.mapEnabled || !target.active || target.life<=0 ||
            (target.type!=npcId && target.netID!=npcId) || Main.npc[target.whoAmI]!=target){Clear();return;}
        if(!opening){if(!Main.mapFullscreen)Clear();return;}
        // Finish the JEI button gesture before handing input to the vanilla map.
        if(Main.mouseLeft)return;
        Main.LocalPlayer.TryOpeningFullscreenMap();
        Main.resetMapFull=false;
        Main.mapFullscreenPos=target.Center/16f;
        opening=false;
    }

    public override void Draw(ref MapOverlayDrawContext context, ref string text)
    {
        if (!Main.mapFullscreen || target == null) return;
        if (!target.active || target.life <= 0 || (target.type != npcId && target.netID != npcId) ||
            Main.npc[target.whoAmI] != target)
        {
            Clear();
            return;
        }

        var frame = new SpriteFrame(1, 5) { CurrentRow = (byte)((Main.GameUpdateCount / 6) % 5) };
        if (context.Draw(TextureAssets.MapPing.Value, target.Center / 16f, frame, Alignment.Center).IsMouseOver)
            text = target.FullName;
    }
}
