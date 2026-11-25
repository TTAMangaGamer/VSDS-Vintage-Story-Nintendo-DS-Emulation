using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VSDS.Item
{
    public class ItemNintendoDS : Vintagestory.API.Common.Item
    {
        public override void OnHeldInteractStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            bool firstEvent,
            ref EnumHandHandling handling)
        {
            if (!firstEvent) return;

            handling = EnumHandHandling.PreventDefault;

            if (api is ICoreClientAPI capi && byEntity == capi.World.Player?.Entity)
            {
                VSDSModSystem modSys = capi.ModLoader.GetModSystem<VSDSModSystem>();

                if (byEntity.Controls.Sneak)
                {
                    modSys?.OpenRomSelector();
                    return;
                }

                bool isFocused = slot.Itemstack.Attributes.GetBool("vsds_focused", false);
                bool nextState = !isFocused;

                slot.Itemstack.Attributes.SetBool("vsds_focused", nextState);
                modSys?.SetTouchOverlayOpen(nextState);

                capi.ShowChatMessage(nextState
                    ? "[VSDS] DS Focused (touchscreen active)"
                    : "[VSDS] DS Unfocused");
            }
        }

        public override void OnHeldDropped(IWorldAccessor world, IPlayer byPlayer, ItemSlot slot, int count, ref EnumHandling handling)
        {
            base.OnHeldDropped(world, byPlayer, slot, count, ref handling);

            if (api is ICoreClientAPI capi)
            {
                slot.Itemstack?.Attributes.SetBool("vsds_focused", false);
                capi.ModLoader.GetModSystem<VSDSModSystem>()?.SetTouchOverlayOpen(false);
            }
        }
    }
}