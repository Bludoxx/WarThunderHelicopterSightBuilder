# Changelog

## v1.5.0 - 2026-07-03

This release focuses on making the editor's preview and sizing match what War
Thunder actually displays.

### Added

- Adjustable line width for the editor, fullscreen preview, saved designs, and
  generated sight package.
- A real fullscreen preview with 720p, 1080p, 1440p, and 4K sizing.
- Predicted in-game screen coverage.
- Small, Medium, Large, and Extra Large size choices.
- Named design saves and dark mode.
- Multi-selection and group editing.
- Automatic War Thunder detection, one-click installation, backup, and restore.

### Fixed

- Normalized the size control so `100%` has a useful and consistent meaning
  across presets, imported art, and custom drawings.
- Migrated older saved designs to the normalized scale while preserving their
  previous output size.
- Corrected the horizontal squeeze caused by War Thunder's non-square rocket
  sight canvas.
- Fixed generated packages accidentally retaining the unchanged template HUD.
- Removed severe slowdown with very detailed SVG imports by caching the design
  scale and rendered geometry instead of recalculating the complete bounds for
  every segment.
- Kept the technical command panel responsive for huge designs without removing
  any commands from the exported package.
- Delayed autosave briefly after edits so large designs are not serialized on
  every intermediate input event.
- Fixed filled dots, outlined circles, grid snapping, CCIP origin placement,
  selection accuracy, canvas panning, decimal input, and very short lines.
- Prevented zero grid, nudge, and zoom values from producing non-finite
  coordinates or corrupting autosave.
- Removed the incorrect package-size restriction and its install warnings.
- Kept large designs and VROMFS entries buildable instead of clipping or
  rejecting them.

### Verification

- Core logic, SVG import, state migration, package rebuilding, install and
  restore behavior, stress cases, and malformed input are covered by automated
  checks.
- The published self-contained executable passed the packaged UI test suite.

`HeliSightBuilder.exe` SHA-256:

```text
A4DC47BFA74206F602AD8DA0C54712B6031A32973FDE879D8A140552A9E7B409
```
