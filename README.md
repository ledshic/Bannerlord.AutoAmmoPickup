# Bannerlord.AutoAmmoPickup

Automatically picks up nearby usable ammunition during Mount & Blade II: Bannerlord combat missions.

## Features

- Picks up arrows, bolts, throwing weapons, and other usable ammunition near the player.
- Refills equipped ammo stacks or equips a new compatible stack when a weapon slot is available.
- Uses Bannerlord's normal item interaction path for pickup animation and equipment handling.
- Fully configurable via **MCM (Mod Configuration Menu)**.

## Configuration (MCM)

After loading a mission or campaign, open **Mod Options** (ESC → Mod Options).

Under **Auto Ammo Pickup** you can configure:

- **Enable Mod** — master toggle
- **Pickup Mode**
  - *Default* — pick any usable ammo your currently equipped weapons can benefit from (original smart behavior)
  - *Only ammo for the currently equipped ranged weapon (in-hand)* — stricter mode: only auto-pick ammo that matches the weapon you are currently holding/wielding
  - *Disabled* — completely turn auto pickup off
- **Disable while crouching** (default: **checked**) — suspends automatic pickup while your character is in the crouched stance. In Bannerlord the default crouch key is **Z** and it is a toggle (press once to crouch, press again to stand). Great for stealth or precise aiming.
- **Show Pickup Messages** — toggle the yellow "Picking up X Y" notifications.
- **Auto Pickup Distance** — slider to tune how close you need to be (1–6 meters).

All settings are global (saved as JSON). 

The mod now depends on the standard MCM stack (Bannerlord.Harmony + ButterLib + UIExtenderEx + MBOptionScreen). Load order is handled in `SubModule.xml`.

## Installation

1. Download the latest `Bannerlord.AutoAmmoPickup-vX.Y.Z.zip` from Releases.
2. Extract and copy the `Bannerlord.AutoAmmoPickup` folder into your game's `Modules/` directory.
3. Launch the game, go to the Launcher → Mods, and enable **Bannerlord Auto Ammo Pickup**.
4. Start any battle, siege, arena, or custom battle. Ammo near the player will be collected automatically (subject to your MCM settings).

**Required dependencies** (must be enabled and loaded before this mod):
- Bannerlord.Harmony
- Bannerlord.ButterLib
- Bannerlord.UIExtenderEx
- Bannerlord.MBOptionScreen (MCM)

## Localization (l10n)

The mod supports the game's built-in localization system.

- English strings are provided.
- Additional languages can be added by contributing `ModuleData/Languages/<culture>/sta_strings.xml` files (community translations welcome).

All in-game messages (load notification and pickup feedback) are localized.

## Building from Source

The repository follows a unified layout shared with other Bannerlord.XXX mods in this collection:

```
dev/
├── build.ps1
├── module/
│   ├── SubModule.xml
│   └── ModuleData/
│       └── Languages/
└── src/
    └── Bannerlord.AutoAmmoPickup/
        ├── Bannerlord.AutoAmmoPickup.csproj
        └── *.cs
```

Requirements: .NET SDK (dotnet).

From the repository root:

```powershell
./dev/build.ps1 -Version v1.0.0
```

Outputs:

- `out/Bannerlord.AutoAmmoPickup/` — ready-to-copy module folder (for testing / manual install)
- `out/Bannerlord.AutoAmmoPickup-v1.0.0.zip` — release package

The project uses `Bannerlord.ReferenceAssemblies` + the MCM package. A local Bannerlord installation is **not** required to compile (the final module still requires the MCM modules at runtime).

## GitHub Release / CI

Use the "Build And Release Mod" workflow (workflow_dispatch):

- Provide the `version` (e.g. `v1.2.3`)
- Choose prerelease true/false

The workflow builds via the script above, packages the module, uploads artifacts, and creates the GitHub Release.

## Load Order

No special requirements (loads anywhere after Native/SandBoxCore/Sandbox if you list dependencies explicitly). Standalone friendly.

## License

Free to use, modify, and redistribute.

## Credits

- TaleWorlds for Bannerlord's excellent modding APIs.
- Community modders who pioneered easy-pickup patterns.
