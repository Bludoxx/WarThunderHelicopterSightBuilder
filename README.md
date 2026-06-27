# War Thunder Helicopter Sight Builder

A Windows editor for creating custom helicopter rocket CCIP reticles and
building the corresponding War Thunder `pkg_user` content package.

This project was created by [Bludoxx](https://www.youtube.com/@Bludoxx) after
players asked for an easier way to customize the helicopter sight shown in the
[original Reddit post](https://www.reddit.com/r/Warthunder/comments/1ubt3zu/you_can_change_the_helicopter_rocket_sight/).
Consider subscribing to the YouTube channel if the tool is useful to you.

## Download

1. Open the latest GitHub Release.
2. Download `HeliSightBuilder.exe`.
3. Run the application. The release is a single self-contained Windows file;
   Python and a separate .NET installation are not required.
4. Choose an output folder and select **Build install files**.

The single-file release is large because it contains the Microsoft .NET
desktop runtime, the application, and the required sight-package resources.

## Install a generated sight

The output contains:

```text
pkg_user/
  base.vromfs.bin
pkg_user.rq2
pkg_user.ver
```

Place those three items inside War Thunder's `content` directory. Removing
them restores the normal game sight.

## Editor controls

- **Select** selects an existing custom shape for precise editing.
- **Pan** moves the canvas with the left mouse button.
- **Line**, **Circle**, **Box**, and **Dot** draw custom geometry.
- The middle mouse button pans from any tool.
- The mouse wheel zooms around the cursor.
- **Custom scale %** changes the complete design without limiting package size.
- The yellow CCIP marker chooses which point becomes the in-game aiming center.
- Undo, redo, grid snapping, numeric editing, SVG import, colors, and autosave
  are included.

Autosave data is stored in:

```text
%LOCALAPPDATA%\HeliSightBuilder\autosave-native.json
```

## Supported SVG elements

The importer accepts line-oriented artwork using:

- `line`
- `rect`
- `circle`
- `ellipse`
- `polyline`
- `polygon`
- simple `M`, `L`, `H`, `V`, and `Z` path commands

Complex curves, text, masks, effects, and transforms should be converted to
simple line art before importing.

## Technical overview

The editor writes `VECTOR_LINE`, `VECTOR_ELLIPSE`, and
`VECTOR_RECTANGLE` commands into the helicopter rocket-sight function in
`reactivegui/airHudElems.nut`.

The selected CCIP origin is translated to the fixed position expected by each
HUD mode. The package writer then rebuilds the Zstandard-compressed VROMFS
container. If an edited entry is larger than its original slot, the writer
appends it and updates the package table instead of enforcing an artificial
byte limit.

The application does not request administrator access and contains no update
service, network client, startup entry, telemetry, obfuscator, or custom
executable packer.

## Source layout

- `src/HeliSightBuilder/MainForm.cs` - Windows UI, editor state, and canvas.
- `src/HeliSightBuilder/SightLogic.cs` - vector generation and SVG importing.
- `src/HeliSightBuilder/VromfsPackage.cs` - VROMFS reading and rebuilding.
- `src/HeliSightBuilder/Resources/` - editable HUD resources and package template.
- `tests/HeliSightBuilder.Tests/` - stress and package-compatibility checks.
- `build_release.ps1` - reproducible single-file release build.
- `run_tests.ps1` - automated quality-control suite.

## Build from source

Requirements:

- Windows 10 or 11
- .NET 8 SDK or newer

From PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run_tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\build_release.ps1
```

The release executable is written to:

```text
release\HeliSightBuilder.exe
```

The release uses Microsoft's standard self-contained single-file deployment
with compression disabled. Build outputs are deterministic at the managed
assembly level, but complete executable hashes can vary with SDK/runtime
updates.

## License

Original project code and documentation use
[CC BY-NC-SA 4.0](LICENSE). Third-party components and game-related property
remain under their respective terms; see [NOTICE.md](NOTICE.md) and
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
