# War Thunder Helicopter Sight Builder

A Windows editor for making custom helicopter rocket CCIP sights and installing
them as a War Thunder `pkg_user` content package.

I made this after players asked for a practical way to build their own sights
following my [original Reddit post](https://www.reddit.com/r/Warthunder/comments/1ubt3zu/you_can_change_the_helicopter_rocket_sight/).
Updates and demonstrations are posted on
[my YouTube channel](https://www.youtube.com/@Bludoxx).

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)
![Release](https://img.shields.io/badge/release-v1.5.2-2E7D32)
![License](https://img.shields.io/badge/license-CC%20BY--NC--SA%204.0-555555)

## Download

Open the [latest release](https://github.com/Bludoxx/WarThunderHelicopterSightBuilder/releases/latest)
and download `HeliSightBuilder.exe`.

The download is a self-contained Windows application. It does not require a
separate .NET installation. The large file size comes from the included
Microsoft .NET desktop runtime and package-building resources.

The SHA-256 for each build is listed on its GitHub release page.

To verify a download in PowerShell:

```powershell
Get-FileHash .\HeliSightBuilder.exe -Algorithm SHA256
```

## Compatibility

Cockpit support depends on the helicopter HUD:

| Helicopter type | Cockpit custom sight | Third-person custom sight | Live range |
| --- | --- | --- | --- |
| Standard HUD with rocket CCIP | Yes | Yes | Cockpit only |
| No rocket CCIP | No | Yes | No |
| HMD HUD | No | Yes | No |

The complete cockpit setup works on non-HMD helicopters with rocket CCIP, such
as the Ka-50, Mi-24, and Mi-28 families. Similar helicopters using the standard
rocket CCIP HUD should work as well.

On a helicopter without rocket CCIP, the game does not draw this sight in the
cockpit. The custom symbol can still appear in third person.

HMD-equipped helicopters use a different cockpit HUD path, so neither the
custom symbol nor its range label is drawn there. The custom symbol still works
in third person, but the distance value is refreshed by the cockpit ILS and is
not useful in third person.

## Quick start

1. Run `HeliSightBuilder.exe`.
2. Pick a preset or select **Custom** and draw a sight.
3. Adjust its relative size, line width, color, and CCIP origin.
4. Use **Full Screen** to check its predicted in-game size.
5. Select **Install Sight**.
6. Restart War Thunder if it was already running.

The program normally finds a Steam installation automatically. If it does not,
use **Find / Choose** and select War Thunder's `content` folder.

## Installing and restoring

- **Install Sight** builds the current design and installs it into the selected
  War Thunder installation.
- **Restore Original** removes the installed override and restores the package
  that existed before the builder replaced it.
- **Build install files** creates a portable package without touching the game
  directory.

An existing `pkg_user` package is backed up before installation.

Manual builds contain:

```text
pkg_user/
  base.vromfs.bin
pkg_user.rq2
pkg_user.ver
```

Place all three items inside War Thunder's `content` directory. Remove those
same items to return to the normal sight when no earlier `pkg_user` package
needs to be restored.

## Editing a sight

### Drawing and navigation

- **Select** chooses existing shapes. Drag over empty canvas space for a box
  selection, or use Ctrl-click and Shift-click to change a multi-selection.
- **Pan** moves the canvas with the left mouse button.
- The right or middle mouse button pans regardless of the active drawing tool.
- The mouse wheel zooms around the cursor.
- **Line**, **Circle**, **Box**, and **Dot** create new geometry.
- **Snap to grid** displays the grid and locks new points to its intersections.
- The nudge controls move every selected shape by the chosen nudge distance.
- **Mirror Selected (CCIP)** duplicates the selected shapes across the vertical
  axis through the current CCIP origin and selects the new mirrored copies.
- Undo, redo, delete, numeric coordinate editing, and SVG import are available.

### Size and appearance

- **Design scale %** is a fixed multiplier for custom drawings. Adding,
  deleting, or moving a shape does not resize the rest of the sight.
- **In-game size** fits the complete design to Small, Medium, Large, or Extra
  Large. These choices deliberately recalculate the scale; choose Custom for
  direct percentage control.
- **Line width** changes stroke thickness in the editor, fullscreen preview,
  saved design, and generated game package.
- **Sight color** sets the generated HUD color.
- **Full Screen** previews the predicted screen coverage at 720p, 1080p, 1440p,
  or 4K using the standard helicopter HUD canvas scale.
- The yellow CCIP marker sets the point that will be placed on the game's
  calculated rocket impact position.
- **Show live range (cockpit)** adds the game's existing rocket CCIP distance
  value to the sight as kilometers with three decimals. The game refreshes this
  value through the cockpit ILS path; third-person display is not guaranteed to
  update. Enter its X/Y position or use
  **Pick range position on canvas**. The position is stored relative to the
  selected CCIP origin when the package is built.

Sight size is unrestricted. Build and Install do not reject a design because
of its dimensions.

### Saved designs

Named designs can be saved, loaded, and deleted inside the program. Autosave
also preserves current work.

```text
Saved designs: %LOCALAPPDATA%\HeliSightBuilder\designs
Autosave:      %LOCALAPPDATA%\HeliSightBuilder\autosave-native.json
```

Older saves are migrated to the fixed custom scale without intentionally
changing their rendered size. Invalid or non-finite coordinates are rejected
before they can enter the package.

## SVG import

The importer supports line-oriented SVG artwork made from:

- `line`
- `rect`
- `circle`
- `ellipse`
- `polyline`
- `polygon`
- straight, cubic, quadratic, smooth, and elliptical-arc path commands:
  `M`, `L`, `H`, `V`, `C`, `S`, `Q`, `T`, `A`, and `Z`
- absolute and relative path coordinates
- nested `matrix`, `translate`, `scale`, `rotate`, `skewX`, and `skewY`
  transforms

Text, masks, and effects should still be converted to paths before importing.

## How the package works

War Thunder calculates the rocket CCIP position. This project only replaces the
HUD resources used to draw the symbol at that position.

The editor converts the design to `VECTOR_LINE`, `VECTOR_ELLIPSE`, and
`VECTOR_RECTANGLE` commands inside `reactivegui/airHudElems.nut`. Filled dots
use a dedicated filled-ellipse editor command and are converted to a form the
game's working vector canvas accepts.

The generated HUD applies horizontal aspect correction from the actual canvas
width and height. This compensates for the game's non-square rocket-sight
canvas so artwork does not become narrower in game.

The package writer rebuilds the Zstandard-compressed VROMFS container and
updates its entry table. Edited resources are not restricted to the original
entry size.

Heavy SVG imports are simplified only below visible pixel detail, then
connected segments are packaged in polylines of no more than seven points. That
matches the largest `VECTOR_LINE` form used by the official HUD resources and
reduces third-person renderer load without using unsupported oversized commands.


## Source layout

```text
src/HeliSightBuilder/
  MainForm.cs              Windows UI, canvas, preview, and editor state
  SightLogic.cs            Vector generation and SVG import
  VromfsPackage.cs         VROMFS reading and rebuilding
  GameInstallService.cs    Detection, backup, install, and restore
  EditorStateRules.cs      Numeric and save-state validation
  Resources/               HUD resources and package template
tests/HeliSightBuilder.Tests/
  Program.cs               Package, stress, migration, and workflow tests
```

## Build from source

Requirements:

- Windows 10 or 11
- .NET 8 SDK or newer

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run_tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\build_release.ps1
```

The self-contained executable is written to:

```text
release\HeliSightBuilder.exe
```

The hash can change when the source, .NET SDK, or bundled runtime changes.

## Project status

The builder is an independent community project and is not affiliated with or
endorsed by Gaijin Entertainment. Installation of game modifications remains
the player's responsibility.

For release history, see [CHANGELOG.md](CHANGELOG.md). Bug reports should
include the builder version, the steps that caused the problem, and a screenshot
or saved design when possible. See [SUPPORT.md](SUPPORT.md) for compatibility
questions and [CONTRIBUTING.md](CONTRIBUTING.md) before submitting code.

## License

Original project code and documentation are available under
[CC BY-NC-SA 4.0](LICENSE). Third-party components and material remain under
their own terms; see [NOTICE.md](NOTICE.md) and
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
