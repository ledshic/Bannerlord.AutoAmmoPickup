# Bannerlord.AutoAmmoPickup

Automatically picks up nearby usable ammunition during Mount & Blade II: Bannerlord combat missions.

## Features

- Picks up arrows, bolts, throwing weapons, and other usable ammunition near the player.
- Refills equipped ammo stacks or equips a new compatible stack when a weapon slot is available.
- Uses Bannerlord's normal item interaction path for pickup animation and equipment handling.
- Requires no runtime dependencies such as Harmony or MCM.

## Development

Source and module packaging files follow the same layout as `Bannerlord.gimeallperks`:

```text
dev/
├── build.ps1
├── module/SubModule.xml
└── src/Bannerlord.AutoAmmoPickup/
    ├── Bannerlord.AutoAmmoPickup.csproj
    └── *.cs
```

The project uses the `Bannerlord.ReferenceAssemblies` NuGet package, so a local Bannerlord installation is not required to compile it.

Run from the repository root:

```powershell
./dev/build.ps1 -Version v1.0.0
```

Outputs:

- `out/AutoAmmoPickup/` - ready-to-copy Bannerlord module folder
- `out/AutoAmmoPickup-v1.0.0.zip` - release package

## Installation

Copy the built `AutoAmmoPickup` folder into Bannerlord's `Modules` directory, enable **Auto Ammo Pickup** in the launcher, and start a battle.

## GitHub Release

Run the `Build And Release Mod` workflow manually, specify a version, and choose whether the release is a prerelease. The workflow builds the project, creates the module archive, uploads it as an artifact, and publishes a GitHub release.

## License

Free to use, modify, and redistribute.
