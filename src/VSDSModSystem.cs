using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using VSDS.Emu;
using VSDS.Gui;
using VSDS.Item;
using VSDS.Render;

namespace VSDS
{
    public class VSDSModSystem : ModSystem
    {
        public const string NetworkChannelName = "vsds_link";

        public ICoreAPI Api { get; private set; }
        public ICoreClientAPI ClientApi { get; private set; }
        public DSScreenRenderer ScreenRenderer { get; private set; }
        public MelonDSCore EmuCore { get; private set; }
        public string RomsFolderPath { get; private set; }
        public string SavesFolderPath { get; private set; }

        private GuiDialogDsTouchOverlay _touchOverlay;
        private long _slotWatchListenerId;
        private bool _wasFocusedLastTick;

        public override void Start(ICoreAPI api)
        {
            Api = api;
            api.RegisterItemClass("ItemNintendoDS", typeof(ItemNintendoDS));
            api.Network.RegisterChannel(NetworkChannelName);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            ClientApi = api;

            string modDataPath = api.GetOrCreateDataPath("vsds");
            RomsFolderPath = Path.Combine(modDataPath, "roms");
            SavesFolderPath = Path.Combine(modDataPath, "saves");

            Directory.CreateDirectory(RomsFolderPath);
            Directory.CreateDirectory(SavesFolderPath);

            ScreenRenderer = new DSScreenRenderer(api);
            api.Event.RegisterRenderer(ScreenRenderer, EnumRenderStage.Ortho, "vsds_screen_renderer");

            EmuCore = new MelonDSCore(api, ScreenRenderer, modDataPath);
            _touchOverlay = new GuiDialogDsTouchOverlay(api, EmuCore, ScreenRenderer);

            RegisterInputHandlers(api);
            _slotWatchListenerId = api.Event.RegisterGameTickListener(OnClientTick, 100);

            api.ChatCommands.Create("dsrom")
                .WithDescription("Open the Nintendo DS ROM selector")
                .HandleWith(_ =>
                {
                    OpenRomSelector();
                    return TextCommandResult.Success("Opened Nintendo DS ROM selector.");
                });
        }

        public void OpenRomSelector()
        {
            if (ClientApi == null || EmuCore == null) return;
            var dialog = new GuiDialogRomSelector(ClientApi, RomsFolderPath, EmuCore);
            dialog.TryOpen();
        }

        public void SetTouchOverlayOpen(bool open)
        {
            if (_touchOverlay == null) return;

            if (open)
            {
                if (!_touchOverlay.IsOpened())
                {
                    _touchOverlay.TryOpen();
                }
            }
            else if (_touchOverlay.IsOpened())
            {
                _touchOverlay.TryClose();
            }
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            // Local wireless / WFC relay & presence extension goes in the next step
        }

        private void OnClientTick(float dt)
        {
            if (ClientApi?.World?.Player == null) return;

            ItemSlot activeSlot = ClientApi.World.Player.InventoryManager?.ActiveHotbarSlot;
            bool holdingDs = activeSlot?.Itemstack?.Item is ItemNintendoDS;
            bool isFocused = holdingDs && activeSlot.Itemstack.Attributes.GetBool("vsds_focused", false);

            if (!isFocused && _wasFocusedLastTick)
            {
                SetTouchOverlayOpen(false);
                EmuCore?.SetTouchState(false, 0.5f, 0.5f);
                if (ScreenRenderer != null && ScreenRenderer.IsFocused)
                {
                    ScreenRenderer.SetFocused(false);
                }
            }

            if (!holdingDs && activeSlot?.Itemstack?.Attributes != null)
            {
                activeSlot.Itemstack.Attributes.RemoveAttribute("vsds_focused");
            }

            _wasFocusedLastTick = isFocused;
        }

        private void RegisterInputHandlers(ICoreClientAPI api)
        {
            api.Event.KeyDown += OnClientKeyDown;
            api.Event.KeyUp += OnClientKeyUp;
        }

        private void UnregisterInputHandlers(ICoreClientAPI api)
        {
            if (api?.Event == null) return;
            api.Event.KeyDown -= OnClientKeyDown;
            api.Event.KeyUp -= OnClientKeyUp;
        }

        private bool IsDsFocused(ICoreClientAPI api)
        {
            ItemSlot activeSlot = api?.World?.Player?.InventoryManager?.ActiveHotbarSlot;
            if (activeSlot?.Itemstack?.Item is not ItemNintendoDS) return false;
            return activeSlot.Itemstack.Attributes.GetBool("vsds_focused", false);
        }

        private static bool TryMapKeyToJoypadId(GlKeys key, out uint joypadId)
        {
            switch (key)
            {
                case GlKeys.Z: joypadId = 0; return true;         // B
                case GlKeys.A: joypadId = 1; return true;         // Y
                case GlKeys.BackSpace: joypadId = 2; return true; // Select
                case GlKeys.Enter: joypadId = 3; return true;     // Start
                case GlKeys.Up: joypadId = 4; return true;        // Up
                case GlKeys.Down: joypadId = 5; return true;      // Down
                case GlKeys.Left: joypadId = 6; return true;      // Left
                case GlKeys.Right: joypadId = 7; return true;     // Right
                case GlKeys.X: joypadId = 8; return true;         // A
                case GlKeys.S: joypadId = 9; return true;         // X
                case GlKeys.Q: joypadId = 10; return true;        // L
                case GlKeys.E: joypadId = 11; return true;        // R
                default:
                    joypadId = 0;
                    return false;
            }
        }

        private void OnClientKeyDown(KeyEvent e)
        {
            if (ClientApi == null || EmuCore == null) return;
            if (!IsDsFocused(ClientApi)) return;

            if (TryMapKeyToJoypadId((GlKeys)e.KeyCode, out uint joypadId))
            {
                EmuCore.SetButtonState(joypadId, true);
                e.Handled = true;
            }
        }

        private void OnClientKeyUp(KeyEvent e)
        {
            if (ClientApi == null || EmuCore == null) return;

            if (TryMapKeyToJoypadId((GlKeys)e.KeyCode, out uint joypadId))
            {
                EmuCore.SetButtonState(joypadId, false);
                e.Handled = true;
            }
        }

        public override void Dispose()
        {
            SetTouchOverlayOpen(false);

            if (ClientApi != null && _slotWatchListenerId != 0)
            {
                ClientApi.Event.UnregisterGameTickListener(_slotWatchListenerId);
                _slotWatchListenerId = 0;
            }

            UnregisterInputHandlers(ClientApi);

            EmuCore?.ExportSaveRam();
            EmuCore?.Dispose();
            EmuCore = null;

            ClientApi?.Event?.UnregisterRenderer(ScreenRenderer, EnumRenderStage.Ortho);
            ScreenRenderer?.Dispose();
            ScreenRenderer = null;

            base.Dispose();
        }
    }
}