using System;
using System.IO;
using Vintagestory.API.Client;
using VSDS.Emu;

namespace VSDS.Gui
{
    public class GuiDialogRomSelector : GuiDialog
    {
        private readonly string _romsFolder;
        private readonly MelonDSCore _emuCore;
        private string[] _romPaths = Array.Empty<string>();
        private int _selectedIndex = -1;

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogRomSelector(ICoreClientAPI capi, string romsFolder, MelonDSCore emuCore) : base(capi)
        {
            _romsFolder = romsFolder;
            _emuCore = emuCore;
        }

        public override bool TryOpen()
        {
            RefreshRomList();
            ComposeDialog();
            return base.TryOpen();
        }

        private void RefreshRomList()
        {
            if (!string.IsNullOrWhiteSpace(_romsFolder) && Directory.Exists(_romsFolder))
            {
                _romPaths = Directory.GetFiles(_romsFolder, "*.nds", SearchOption.TopDirectoryOnly);
                Array.Sort(_romPaths, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                _romPaths = Array.Empty<string>();
            }

            _selectedIndex = _romPaths.Length > 0 ? 0 : -1;
        }

        private void ComposeDialog()
        {
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            var composer = capi.Gui
                .CreateCompo("vsds-rom-selector", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar("Nintendo DS - Select ROM", OnTitleBarClose)
                .BeginChildElements(bgBounds);

            double y = 38;
            const double rowHeight = 32;
            const double width = 360;

            if (_romPaths.Length == 0)
            {
                ElementBounds emptyBounds = ElementBounds.Fixed(0, y, width, 60);
                composer.AddStaticText(
                    $"No .nds ROMs found in:\n{_romsFolder}",
                    CairoFont.WhiteSmallText(),
                    emptyBounds
                );
                y += 70;
            }
            else
            {
                int maxVisible = Math.Min(_romPaths.Length, 8);
                for (int i = 0; i < maxVisible; i++)
                {
                    int idx = i;
                    string fileName = Path.GetFileName(_romPaths[idx]);
                    string label = idx == _selectedIndex ? $"> {fileName}" : fileName;

                    ElementBounds btnBounds = ElementBounds.Fixed(0, y, width, rowHeight);
                    composer.AddSmallButton(label, () =>
                    {
                        _selectedIndex = idx;
                        ComposeDialog();
                        return true;
                    }, btnBounds);

                    y += rowHeight + 6;
                }
            }

            y += 8;
            ElementBounds refreshBounds = ElementBounds.Fixed(0, y, 170, 34);
            ElementBounds bootBounds = ElementBounds.Fixed(190, y, 170, 34);

            composer
                .AddSmallButton("Refresh", () =>
                {
                    RefreshRomList();
                    ComposeDialog();
                    return true;
                }, refreshBounds)
                .AddSmallButton("Boot ROM", OnBootClicked, bootBounds)
                .EndChildElements();

            SingleComposer = composer.Compose();
        }

        private bool OnBootClicked()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _romPaths.Length)
            {
                return true;
            }

            string chosenPath = _romPaths[_selectedIndex];
            if (_emuCore != null && _emuCore.LoadRom(chosenPath))
            {
                TryClose();
            }

            return true;
        }

        private void OnTitleBarClose()
        {
            TryClose();
        }
    }
}