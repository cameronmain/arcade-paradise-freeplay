# Arcade Paradise Freeplay Mod

A MelonLoader mod that adds a libretro-powered arcade cabinet to [Arcade Paradise](https://store.steampowered.com/app/1388870/Arcade_Paradise/), letting you play compatible classic arcade ROMs from inside the game.

> **Note:** This mod changes arcade-machine and save-data behaviour. Back up your Arcade Paradise save files before installing or testing it. Use the mod at your own risk, particularly when adding it to an existing save or removing it from one.

## Requirements

- [Arcade Paradise](https://store.steampowered.com/app/1388870/Arcade_Paradise/)
- [MelonLoader](https://github.com/LavaGang/MelonLoader) installed for Arcade Paradise
- The .NET 6 runtime/dependencies required by your MelonLoader installation
- A legally obtained arcade ROM in a format supported by the bundled libretro core

### ROM-set compatibility

The **FB Alpha 2012** libretro core is the arcade emulation engine used by this mod. The ROMs currently being used to test this project are from an **FB Alpha v0.2.97.39** set, and the games tried so far have worked successfully. Compatibility can vary between individual titles because ROM contents, parent/clone relationships, BIOS requirements and filenames can change between set versions.

For the most reliable results, use ZIP archives from the FB Alpha v0.2.97.39 set used for testing, preferably full non-merged sets when using individual games. The definitive version is the one reported by the core in the MelonLoader log when it starts. The mod does not validate ROM checksums itself; the core decides whether an archive can be loaded.

The scanner currently exposes only direct `.zip` files in `FreePlay/roms/`. It does not expose `.7z` files, and there is no special CHD or multi-disc handling. Arcade ROMs must keep the filename expected by the core; renaming an archive can prevent it from being recognised.

## Installation

1. Install MelonLoader for Arcade Paradise and launch the game once so that MelonLoader can initialise its folders.
2. Download the latest release ZIP from this repository's Releases page.
3. Extract the release into the Arcade Paradise installation directory. The layout should look like this:

```text
Arcade/
└── Mods/
    ├── ArcadeParadiseFreePlayMod.dll
    └── FreePlay/
        ├── cabinet.png
        ├── fbalpha2012_libretro.dll
        └── roms/
```

`fbalpha2012_libretro.dll` is the emulator core that actually loads and runs the arcade ROMs. The mod selects it automatically.

`cabinet.png` is optional. If it is present, the mod uses it for the Freeplay cabinet artwork; otherwise the cabinet keeps its fallback appearance.

4. Copy your legally obtained ROM ZIP files into `Arcade/Mods/FreePlay/roms/`:

```text
Arcade/
└── Mods/
    ├── ArcadeParadiseFreePlayMod.dll
    └── FreePlay/
        ├── cabinet.png
        ├── fbalpha2012_libretro.dll
        └── roms/
            ├── your-game.zip
            └── another-game.zip
```

Do not include ROMs in public releases. Users must provide their own legally obtained game data.

5. Start Arcade Paradise, open the Arcade Mania shop and purchase the **Freeplay** cabinet. It follows the game's normal delivery process.
6. Once the cabinet is available, interact with it to start the selected ROM.

The mod scans the `roms/` directory for `.zip` files and creates the folder automatically if it does not exist. Known BIOS archives are excluded from the in-cabinet game list, but may still be required by some games and should remain in the same `roms/` directory when needed by the core.

## Using the cabinet

- Interact with the cabinet to start the currently selected ROM.
- While playing, press **F2** to open the ROM browser when more than one ROM is available.
- Use **Up/Down** or **W/S** to select a ROM.
- Press **Enter** or **Space** to launch the selected ROM.
- Press **Escape** or **Backspace** to leave the ROM browser.
- The last successfully selected ROM is remembered in `FreePlay/lastrom.txt`.

### Recommended input

Controller input uses a standard RetroPad mapping, so games do not need separate bindings. Sticks, face buttons, shoulders and triggers are supported automatically where available.

- D-pad or left stick: movement
- Menu/Start: start
- View/Back: coin
- Face buttons: arcade actions
- Triggers: occassionally supported, ROM dependent

Keyboard input is available as a fallback. The current keyboard mapping passed to the libretro core is:

| Arcade input | Keyboard |
| --- | --- |
| Up / Down / Left / Right | Arrow keys or W/S/A/D |
| Start | Enter or 1 |
| Coin | 5 |
| Action button 1 | Left Alt or X |
| Action button 2 | Left Ctrl or Z |

The exact controls may vary between games and cores.

## Troubleshooting

### The cabinet does not appear

- Confirm that `ArcadeParadiseFreePlayMod.dll` is directly inside the game's `Mods` folder.
- Confirm that MelonLoader is installed for the same Arcade Paradise installation you are launching.
- Check the MelonLoader log for errors loading the mod.

### The cabinet reports `NO ROMS FOUND`

- Confirm that the files are `.zip` archives, not folders or another archive format.
- Confirm that they are directly inside `Arcade/Mods/FreePlay/roms/`.
- Make sure the files are not only BIOS archives, which are intentionally hidden from the game list.

### The cabinet reports `CORE NOT FOUND` or `CORE LOAD FAILED`

- Confirm that `fbalpha2012_libretro.dll` is directly inside `Arcade/Mods/FreePlay/`.
- Ensure that the core matches the architecture and runtime used by your MelonLoader installation.

### A ROM is rejected

The ROM must be compatible with the selected libretro core and may require a matching BIOS archive. Check the core's supported systems and the ROM's legal source. The mod does not provide or convert ROM files.

## Release contents

A release ZIP should contain at least:

- `ArcadeParadiseFreePlayMod.dll`
- `FreePlay/fbalpha2012_libretro.dll`
- `FreePlay/cabinet.png` (optional cabinet artwork)
- Any applicable third-party licence and attribution files

Core DLLs are intentionally ignored by this source repository's `.gitignore`, so `fbalpha2012_libretro.dll` is not stored in the tracked project files. A release ZIP must add the core separately under `FreePlay/`; the normal build only produces the mod DLL and does not copy the core automatically. If a release does not include the core, users must provide a compatible copy themselves and comply with its licence.

Do **not** include copyrighted ROMs or BIOS files unless you have the legal right to redistribute them.

## Changing the emulator core

The default core is `fbalpha2012_libretro.dll`, but advanced users can test another compatible libretro core without rebuilding the mod:

1. Copy the core DLL into `Arcade/Mods/FreePlay/`.
2. Create or edit `Arcade/Mods/FreePlay/cabinet.json` and set the `core` filename:

```json
{
  "core": "another_core_libretro.dll"
}
```

3. Restart Arcade Paradise completely so that the new core is loaded.

The replacement core must be a Windows x64 libretro core supported by the mod's host. Its ROM set, BIOS files and dependencies must match the core; changing the DLL does not make an incompatible ROM set work. For example, the `fbalpha2012_neogeo_libretro.dll` core is for Neo Geo games only, while MAME cores expect their own MAME ROM-set versions.

To return to the default core, set the value to `auto` or remove `cabinet.json`:

```json
{
  "core": "auto"
}
```

## Building

The project targets .NET 6 and references the local Arcade Paradise and MelonLoader assemblies. Those assemblies are not included in the repository.

1. Install the .NET 6 SDK and a compatible C# development environment.
2. Create or edit the untracked `Directory.Build.props` file in the project root and set `GamePath` to your Arcade Paradise installation:

```xml
<Project>
  <PropertyGroup>
    <GamePath>C:\Games\Arcade Paradise</GamePath>
  </PropertyGroup>
</Project>
```

3. Build `ArcadeParadiseFreePlayMod.sln` or `ArcadeParadiseFreePlayMod.csproj`.

The post-build target copies the resulting mod DLL to `$(GamePath)/Mods`; it does not download or copy the separately licensed emulator core. A clone and build therefore produces the mod itself, but you must provide the runtime files manually before launching the game:

```text
$(GamePath)/Mods/
├── ArcadeParadiseFreePlayMod.dll
└── FreePlay/
    ├── fbalpha2012_libretro.dll
    ├── cabinet.png       (optional)
    └── roms/             (your legally obtained ROMs)
```

Obtain `fbalpha2012_libretro.dll` from a legitimate RetroArch/Libretro distribution or build it from the relevant upstream source. Make sure it is a Windows x64 build and retain its applicable licence and attribution. Do not commit `Directory.Build.props`, game assemblies, ROMs, core DLLs or runtime state files.

## Credits and licences

The original mod code is licensed under the [MIT License](LICENSE.txt). The same file also contains the applicable licence text and attribution for the bundled FB Alpha 2012 libretro core and its components.

The core's licence prohibits distributing FB Alpha with ROM images unless you have the legal right to distribute them. Arcade-game ROMs and BIOS files remain the property of their respective copyright holders. This project does not redistribute game ROMs.
