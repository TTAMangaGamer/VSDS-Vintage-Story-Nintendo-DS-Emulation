VSDS-Vintage-Story-Nintendo-DS-Emulation



Vintage Story mod embedding a Nintendo DS libretro core (melonDS / melonDS DS) with 2D HUD dual-screen rendering, draggable screen layout, button controls, audio, and persistent .sav save RAM storage.



MAY OR MAY NOT WORK ON LiNUX I HAVE NOT TESTED





!\[VSDS 2D HUD dual-screen emulator running in Vintage Story](screenshots/vsds-hud-working.png)



\---



\## Features



\* \*\*Dual-Screen 2D HUD Renderer (`DSScreenRenderer`):\*\* Renders 256×192 top and bottom DS screens as 2D HUD textures on `EnumRenderStage.Ortho`, positioned to avoid overlapping the top-right minimap and scaled cleanly when focused.

\* \*\*Draggable HUD Layout:\*\* Move the DS screens around the screen (`HudOffsetX` / `HudOffsetY`) and keep touch/overlay bounds synced with the rendered bottom screen.

\* \*\*Persistent `.sav` Save Data (`MelonDSCore`):\*\* Exports and imports DS save RAM (`RETRO\\\\\\\\\\\\\\\_MEMORY\\\\\\\\\\\\\\\_SAVE\\\\\\\\\\\\\\\_RAM`) to `<ModData>/vsds/saves/<ROMName>.sav` on ROM load, periodic auto-save ticks, ROM unload, and `Dispose()`, and also loads manually placed `<ROMName>.sav` files matching the ROM filename.

\* \*\*Cross-Platform Audio (`CrossPlatformDsAudioPlayer`):\*\* Streams 32,768 Hz stereo 16-bit PCM audio on Windows (`winmm.dll`) and Linux (`pw-cat` / `aplay`).

\* \*\*DS Button Keybinds (`VSDSModSystem`):\*\* Maps DS joypad inputs (`0`–`11`) to keyboard keys while the DS item (`ItemNintendoDS`) is held and focused.

\* \*\*Multiplayer Presence Channel (`vsds\\\\\\\\\\\\\\\_link`):\*\* Registers the `vsds\\\\\\\\\\\\\\\_link` network channel in `VSDSModSystem` for future world presence and trade/battle invite features.



\---



\## Project Structure



\* `VSDSModSystem.cs` — Main mod lifecycle, `ItemNintendoDS` registration, `/dsrom` command, `vsds\\\\\\\\\\\\\\\_link` network channel setup, input handling, and cleanup.

\* `MelonDSCore.cs` — Native `melonds\\\\\\\\\\\\\\\_libretro` P/Invoke wrapper, emulation loop thread, video/audio/input callbacks, and `.sav` save RAM export/import.

\* `DSScreenRenderer.cs` — SkiaSharp + `LoadedTexture` 2D HUD renderer with focus scaling and draggable offsets.

\* `GuiDialogDsTouchOverlay.cs` — Overlay dialog for pointer/drag interaction and screen positioning.



\---



\## Getting the Emulator Core (Required)



This repository does \*\*not\*\* redistribute compiled libretro core binaries. You must supply a compatible \*\*melonDS\*\* or \*\*melonDS DS\*\* libretro core yourself.



\### 1. Where to Get the Core

\* \*\*melonDS DS (recommended remake):\*\* Available from the official `JesseTG/melonds-ds` GitHub releases or Libretro core builds (`melondsds\\\\\\\\\\\\\\\_libretro`).

\* \*\*melonDS libretro / RetroArch builds:\*\* Available from Libretro's official buildbot/core distribution or an existing RetroArch `cores/` installation (`melonds\\\\\\\\\\\\\\\_libretro`).

\* \*\*Rename if needed:\*\* If your downloaded file is named `melondsds\\\\\\\\\\\\\\\_libretro` (or `libmelondsds\\\\\\\\\\\\\\\_libretro.so`), renaming it to `melonds\\\\\\\\\\\\\\\_libretro.dll` (Windows) or `melonds\\\\\\\\\\\\\\\_libretro.so` / `libmelonds\\\\\\\\\\\\\\\_libretro.so` (Linux) matches `MelonNativeResolver`'s primary lookup name directly.



\### 2. Where to Place the Core File

`MelonNativeResolver` checks the following folders automatically:



\* \*\*Next to the mod / assembly directory:\*\*

&#x20; \* `<ModAssemblyDir>/melonds\\\\\\\\\\\\\\\_libretro.dll` or `melonds\\\\\\\\\\\\\\\_libretro.so`

&#x20; \* `<ModAssemblyDir>/native/melonds\\\\\\\\\\\\\\\_libretro.dll` (or `.so`)

&#x20; \* `<ModAssemblyDir>/native/win-x64/` or `<ModAssemblyDir>/native/linux-x64/`

\* \*\*Inside your VSDS data folder (`<ModData>/vsds/`):\*\*

&#x20; \* `<ModData>/vsds/native/melonds\\\\\\\\\\\\\\\_libretro.dll` (or `.so`)

&#x20; \* `<ModData>/vsds/native/win-x64/` or `<ModData>/vsds/native/linux-x64/`



\### 3. DS BIOS / Firmware (If Required by Your Core)

If your core or specific ROMs require DS BIOS/firmware files, place them in:

\* `<ModData>/vsds/system/`



\---



\## File Locations



\* \*\*ROMs:\*\* `<VintageStoryData>/ModData/vsds/roms/` (place `.nds` files here and open with `/dsrom`)

\* \*\*Save Files (`.sav`):\*\* `<VintageStoryData>/ModData/vsds/saves/<ROMName>.sav`

\* \*\*Core / Native Library:\*\* Loaded via `MelonNativeResolver` from the mod/native directories (`melonds\\\\\\\\\\\\\\\_libretro.dll` on Windows or `melonds\\\\\\\\\\\\\\\_libretro.so` / `libmelonds\\\\\\\\\\\\\\\_libretro.so` on Linux).



\---



\## Controls



| DS Button | Joypad ID | Default Key |

| :--- | :---: | :--- |

| \*\*B\*\* | `0` | `Z` |

| \*\*Y\*\* | `1` | `A` |

| \*\*Select\*\* | `2` | `BackSpace` |

| \*\*Start\*\* | `3` | `Enter` |

| \*\*D-Pad Up\*\* | `4` | `Up` |

| \*\*D-Pad Down\*\* | `5` | `Down` |

| \*\*D-Pad Left\*\* | `6` | `Left` |

| \*\*D-Pad Right\*\* | `7` | `Right` |

| \*\*A\*\* | `8` | `X` |

| \*\*X\*\* | `9` | `S` |

| \*\*L\*\* | `10` | `Q` |

| \*\*R\*\* | `11` | `E` |



\---



\## Current Status \& Known Limitations



\* \*\*Working:\*\* ROM booting, 2D HUD dual-screen rendering, draggable screen offsets, button controls, audio playback, and `.sav` save file loading/saving.

\* \*\*Known Issue (Touchscreen Taps):\*\* Pointer coordinates/dot render on the bottom screen, but menu tap activation in the `melonDS DS` (`melonds\\\\\\\\\\\\\\\_libretro`) core is still unreliable/incomplete—best suited for games navigable via DS buttons.

\* \*\*Not Yet Implemented (Local Wireless / WFC):\*\* Direct DS local wireless and Nintendo WFC trading/battling over Vintage Story network packets are parked for now; only the `vsds\\\\\\\\\\\\\\\_link` channel skeleton is registered.



\---



\## Survival Crafting Recipe

clearquartz x1
copperplate x1
any wood plank x1
temporal gear x1
resin x1

Players can craft the \*\*Nintendo DS\*\* (`ItemNintendoDS`) in survival mode once they have gathered the required materials. Because `"shapeless": true` is set in the recipe, \*\*ingredient positions in the 3×3 grid do not matter\*\*—only the required items and quantities are checked.



\---



\## TODO



\* \*\*Touchscreen Menu Taps:\*\* Diagnose why `melonDS DS` (`melonds\\\\\\\\\\\\\\\_libretro`) displays the pointer/dot on the bottom screen without activating menu taps (verify `RETRO\\\\\\\\\\\\\\\_DEVICE\\\\\\\\\\\\\\\_POINTER` / `RETRO\\\\\\\\\\\\\\\_DEVICE\\\\\\\\\\\\\\\_MOUSE` press state and core options).

\* \*\*HUD Position Persistence:\*\* Save and restore `HudOffsetX` and `HudOffsetY` between sessions so players don't have to re-drag the screens every time they load in.

\* \*\*Keybind Remapping \& Collision Avoidance:\*\* Add configurable keybinds or prevent default `A` (`Y`) and `S` (`X`) DS bindings from conflicting with movement when the DS is focused.

\* \*\*Local Wireless \& WFC / Link Features:\*\* Explore `vsds\\\\\\\\\\\\\\\_link` presence, trade/battle invites, or external/LAN DS multiplayer workflows when returning to multiplayer features.



\## Changelog



\* \*\*Current Snapshot (Parked):\*\*

&#x20; \* Added `.sav` save RAM export and import (`RETRO\\\\\\\\\\\\\\\_MEMORY\\\\\\\\\\\\\\\_SAVE\\\\\\\\\\\\\\\_RAM`) to `<ModData>/vsds/saves/<ROMName>.sav` on ROM load, periodic auto-save ticks, ROM unload, and `Dispose()`, supporting existing `.sav` files matching the ROM filename.

&#x20; \* Updated `GuiDialogDsTouchOverlay` with overlay composition, pointer coordinate mapping, and screen dragging.

&#x20; \* Added `HudOffsetX` / `HudOffsetY` dragging and minimap-safe placement in `DSScreenRenderer`.

&#x20; \* Registered `vsds\\\\\\\\\\\\\\\_link` channel in `VSDSModSystem` and parked direct local wireless / WFC trading and battling for a later pass.

\* \*\*Earlier Milestone:\*\*

&#x20; \* Migrated DS screen rendering from 3D quad to 2D HUD (`EnumRenderStage.Ortho`) with `MelonDSCore` emulation loop, cross-platform audio output, and DS joypad button mapping.

