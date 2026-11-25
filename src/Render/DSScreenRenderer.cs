using System;
using System.Runtime.InteropServices;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VSDS.Item;

namespace VSDS.Render
{
    internal sealed class SkiaScreenBitmap : IBitmap
    {
        private readonly SKBitmap _bitmap;
        private readonly int[] _packedPixels;

        public int Width => _bitmap.Width;
        public int Height => _bitmap.Height;
        public int[] Pixels => _packedPixels;

        public SkiaScreenBitmap(int width, int height, int[] bgraPixels)
        {
            _bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            _packedPixels = new int[width * height];

            if (bgraPixels != null && bgraPixels.Length == _packedPixels.Length)
            {
                Marshal.Copy(bgraPixels, 0, _bitmap.GetPixels(), bgraPixels.Length);
            }

            SKColor[] skColors = _bitmap.Pixels;
            for (int i = 0; i < skColors.Length; i++)
            {
                SKColor c = skColors[i];
                uint opaque = 0xFF000000u | ((uint)c.Red << 16) | ((uint)c.Green << 8) | c.Blue;
                _packedPixels[i] = unchecked((int)opaque);
            }
        }

        public SKColor GetPixel(int x, int y)
        {
            int cx = Math.Clamp(x, 0, Width - 1);
            int cy = Math.Clamp(y, 0, Height - 1);
            return _bitmap.GetPixel(cx, cy);
        }

        public SKColor GetPixelRel(float x, float y)
        {
            int px = Math.Clamp((int)(x * Width), 0, Width - 1);
            int py = Math.Clamp((int)(y * Height), 0, Height - 1);
            return _bitmap.GetPixel(px, py);
        }

        public int[] GetPixelsTransformed(int rot = 0, int alpha = 255)
        {
            return _packedPixels;
        }
    }

    public class DSScreenRenderer : IRenderer
    {
        private readonly ICoreClientAPI _capi;
        private readonly int[] _topPixels = new int[256 * 192];
        private readonly int[] _bottomPixels = new int[256 * 192];

        private LoadedTexture _topTexture;
        private LoadedTexture _bottomTexture;

        public bool IsFocused { get; private set; }
        public int TopScreenTextureId => _topTexture?.TextureId ?? 0;
        public int BottomScreenTextureId => _bottomTexture?.TextureId ?? 0;

        // Movable HUD offset (persists across renders until reset)
        public float HudOffsetX { get; set; }
        public float HudOffsetY { get; set; }

        // Latest rendered screen rectangles (in screen pixels)
        public float TopX { get; private set; }
        public float TopY { get; private set; }
        public float BottomX { get; private set; }
        public float BottomY { get; private set; }
        public float ScreenW { get; private set; }
        public float ScreenH { get; private set; }

        public double RenderOrder => 0.9;
        public int RenderRange => 24;

        public DSScreenRenderer(ICoreClientAPI capi)
        {
            _capi = capi;
            _topTexture = new LoadedTexture(capi);
            _bottomTexture = new LoadedTexture(capi);

            UploadPixels(_topPixels, ref _topTexture);
            UploadPixels(_bottomPixels, ref _bottomTexture);
        }

        public void SetFocused(bool focused)
        {
            ItemSlot activeSlot = _capi.World?.Player?.InventoryManager?.ActiveHotbarSlot;
            bool holdingDs = activeSlot?.Itemstack?.Item is ItemNintendoDS;

            IsFocused = focused && holdingDs;
        }

        public bool ToggleFocus()
        {
            ItemSlot activeSlot = _capi.World?.Player?.InventoryManager?.ActiveHotbarSlot;
            if (activeSlot?.Itemstack?.Item is not ItemNintendoDS)
            {
                IsFocused = false;
                return false;
            }

            IsFocused = !IsFocused;
            _capi?.ShowChatMessage(IsFocused ? "[VSDS] DS Focused" : "[VSDS] DS Unfocused");
            return IsFocused;
        }

        public void NudgeHudOffset(float dx, float dy)
        {
            HudOffsetX += dx;
            HudOffsetY += dy;
        }

        public void ResetHudOffset()
        {
            HudOffsetX = 0f;
            HudOffsetY = 0f;
        }

        private void UploadPixels(int[] pixels, ref LoadedTexture target)
        {
            IBitmap bmp = new SkiaScreenBitmap(256, 192, pixels);
            _capi.Render.LoadTexture(bmp, ref target, false, 0);
        }

        public void UpdateScreenPixels(int[] topBgra, int[] bottomBgra)
        {
            if (topBgra != null && topBgra.Length == _topPixels.Length)
            {
                Array.Copy(topBgra, _topPixels, _topPixels.Length);
                UploadPixels(_topPixels, ref _topTexture);
            }

            if (bottomBgra != null && bottomBgra.Length == _bottomPixels.Length)
            {
                Array.Copy(bottomBgra, _bottomPixels, _bottomPixels.Length);
                UploadPixels(_bottomPixels, ref _bottomTexture);
            }
        }

        public void RenderScreenHud()
        {
            if (_capi?.Render == null) return;

            float frameW = _capi.Render.FrameWidth;
            float frameH = _capi.Render.FrameHeight;

            float scale = IsFocused ? 2.0f : 1.0f;
            float w = 256f * scale;
            float h = 192f * scale;

            // Default left side when unfocused avoids the top-right minimap; centered when focused
            float baseX = IsFocused ? (frameW - w) * 0.5f : 16f;
            float baseTopY = IsFocused ? 24f : 16f;
            float gap = IsFocused ? 12f : 8f;
            float baseBotY = baseTopY + h + gap;

            if (baseBotY + h > frameH - 16f)
            {
                float maxHeight = Math.Max(64f, (frameH - 48f) * 0.5f);
                float fitScale = maxHeight / 192f;
                w = 256f * fitScale;
                h = 192f * fitScale;
                baseX = IsFocused ? (frameW - w) * 0.5f : 16f;
                baseTopY = 16f;
                baseBotY = baseTopY + h + 8f;
            }

            float minX = 8f;
            float maxX = Math.Max(minX, frameW - w - 8f);
            float minTopY = 8f;
            float maxBotY = Math.Max(minTopY + h + 4f, frameH - h - 8f);

            float x = Math.Clamp(baseX + HudOffsetX, minX, maxX);
            float topY = Math.Clamp(baseTopY + HudOffsetY, minTopY, maxBotY - h - 4f);
            float botY = Math.Clamp(topY + h + gap, minTopY + h + 4f, maxBotY);

            TopX = x;
            TopY = topY;
            BottomX = x;
            BottomY = botY;
            ScreenW = w;
            ScreenH = h;

            if (TopScreenTextureId > 0)
            {
                _capi.Render.Render2DTexture(TopScreenTextureId, x, topY, w, h, 50f);
            }

            if (BottomScreenTextureId > 0)
            {
                _capi.Render.Render2DTexture(BottomScreenTextureId, x, botY, w, h, 50f);
            }
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            ItemSlot activeSlot = _capi.World?.Player?.InventoryManager?.ActiveHotbarSlot;
            bool holdingDs = activeSlot?.Itemstack?.Item is ItemNintendoDS;

            if (!holdingDs)
            {
                IsFocused = false;
                return;
            }

            if (stage == EnumRenderStage.Ortho)
            {
                RenderScreenHud();
            }
        }

        public void Dispose()
        {
            IsFocused = false;

            _topTexture?.Dispose();
            _bottomTexture?.Dispose();
            _topTexture = null;
            _bottomTexture = null;
        }
    }
}