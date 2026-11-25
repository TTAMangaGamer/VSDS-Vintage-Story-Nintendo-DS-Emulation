using System;
using Vintagestory.API.Client;
using VSDS.Emu;
using VSDS.Render;

namespace VSDS.Gui
{
    public class GuiDialogDsTouchOverlay : GuiDialog
    {
        private readonly MelonDSCore _core;
        private readonly DSScreenRenderer _renderer;

        private bool _touchDragging;
        private bool _hudDragging;
        private double _lastMouseX;
        private double _lastMouseY;

        public override string ToggleKeyCombinationCode => null;
        public override bool PrefersUngrabbedMouse => true;
        public override bool ShouldReceiveMouseEvents() => IsOpened();
        public override bool ShouldReceiveKeyboardEvents() => false;
        public override double DrawOrder => 0.15;

        public GuiDialogDsTouchOverlay(ICoreClientAPI capi, MelonDSCore core, DSScreenRenderer renderer) : base(capi)
        {
            _core = core;
            _renderer = renderer;
        }

        public override bool TryOpen()
        {
            ComposeOverlay();
            return base.TryOpen();
        }

        private void ComposeOverlay()
        {
            double w = capi?.Render?.FrameWidth ?? 1920;
            double h = capi?.Render?.FrameHeight ?? 1080;
            ElementBounds fullBounds = ElementBounds.Fixed(0, 0, w, h);

            SingleComposer = capi.Gui
                .CreateCompo("vsds-touch-overlay", fullBounds)
                .Compose();
        }

        public override void OnGuiClosed()
        {
            _touchDragging = false;
            _hudDragging = false;
            _core?.SetTouchState(false, 0.5f, 0.5f);
            base.OnGuiClosed();
        }

        public override void OnMouseDown(MouseEvent args)
        {
            if (!IsOpened() || _renderer == null)
            {
                base.OnMouseDown(args);
                return;
            }

            double mx = args.X;
            double my = args.Y;

            if (TryGetScreenUv(mx, my, _renderer.BottomX, _renderer.BottomY, _renderer.ScreenW, _renderer.ScreenH, out float u, out float v))
            {
                _touchDragging = true;
                _core?.SetTouchState(true, u, v);
                args.Handled = true;
                return;
            }

            if (IsInsideRect(mx, my, _renderer.TopX, _renderer.TopY, _renderer.ScreenW, _renderer.ScreenH))
            {
                _hudDragging = true;
                _lastMouseX = mx;
                _lastMouseY = my;
                args.Handled = true;
                return;
            }

            base.OnMouseDown(args);
        }

        public override void OnMouseMove(MouseEvent args)
        {
            if (!IsOpened() || _renderer == null)
            {
                base.OnMouseMove(args);
                return;
            }

            double mx = args.X;
            double my = args.Y;

            if (_hudDragging)
            {
                float dx = (float)(mx - _lastMouseX);
                float dy = (float)(my - _lastMouseY);
                _lastMouseX = mx;
                _lastMouseY = my;
                _renderer.NudgeHudOffset(dx, dy);
                args.Handled = true;
                return;
            }

            if (_touchDragging)
            {
                ClampScreenUv(mx, my, _renderer.BottomX, _renderer.BottomY, _renderer.ScreenW, _renderer.ScreenH, out float u, out float v);
                _core?.SetTouchState(true, u, v);
                args.Handled = true;
                return;
            }

            base.OnMouseMove(args);
        }

        public override void OnMouseUp(MouseEvent args)
        {
            if (_hudDragging)
            {
                _hudDragging = false;
                args.Handled = true;
            }

            if (_touchDragging)
            {
                _touchDragging = false;
                _core?.SetTouchState(false, 0.5f, 0.5f);
                args.Handled = true;
            }

            base.OnMouseUp(args);
        }

        private static bool IsInsideRect(double mx, double my, float rx, float ry, float rw, float rh)
        {
            return rw > 0f && rh > 0f && mx >= rx && mx <= rx + rw && my >= ry && my <= ry + rh;
        }

        private static bool TryGetScreenUv(double mx, double my, float rx, float ry, float rw, float rh, out float u, out float v)
        {
            if (rw <= 0f || rh <= 0f)
            {
                u = 0f;
                v = 0f;
                return false;
            }

            u = (float)((mx - rx) / rw);
            v = (float)((my - ry) / rh);
            return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
        }

        private static void ClampScreenUv(double mx, double my, float rx, float ry, float rw, float rh, out float u, out float v)
        {
            u = Math.Clamp((float)((mx - rx) / Math.Max(1f, rw)), 0f, 1f);
            v = Math.Clamp((float)((my - ry) / Math.Max(1f, rh)), 0f, 1f);
        }
    }
}