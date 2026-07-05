# Changelog

## v1.5.2 - 2026-07-05

This update adds an optional cockpit range label and fixes the editor scale
changing underneath a custom drawing.

### Added

- Optional live range text driven by the same `DistToTarget` state used by the
  Ka-52's `ils28K` rocket-mode sight.
- Three-decimal kilometer formatting, for example `6278 m` is displayed as
  `6.278 km`.
- Editable range-text X/Y position, canvas placement mode, text size, preview,
  autosave, and named-design persistence.

### Compatibility

- Cockpit sight and range support requires a non-HMD helicopter with rocket
  CCIP. This includes the Ka-50, Mi-24, and Mi-28 families.
- Helicopters without rocket CCIP can still show the custom sight in third
  person, but not in the cockpit.
- HMD-HUD helicopters can show the custom sight in third person. Their cockpit
  uses another HUD path, and the live range does not update in third person.

### Fixed

- The range label stays attached to the existing rocket canvas and moves with
  the live CCIP.
- Fixed the label being hidden in rocket mode by an unrelated laser-rangefinder
  state check.
- Fixed range-label placement and font scale disagreeing between the editor
  canvas and the full-screen preview.
- Calibrated range-text sizing so one editor unit equals four HUD pixels.
- Lowered the editable text-size minimum to `0.25` with quarter-unit steps.
- Calibrated the range-text previews for the cockpit collimator projection.
  Preview text no longer appears four times larger than its cockpit result.
- Centered range text through a zero-size HUD wrapper, matching War Thunder's
  native range component. X/Y placement now targets the label center.
- Custom geometry now uses a fixed design scale. Adding or removing a line no
  longer rescales every existing shape or shifts it away from the grid.
- Existing saved custom designs are migrated without changing their exported
  in-game size.
- Shape changes now participate in undo and autosave. Snap, grid, nudge, and
  canvas zoom settings are also preserved immediately.
- Tightened coordinate controls so the sidebar no longer needs horizontal
  scrolling for ordinary editing.
- Clarified in the controls and README that live distance updates come from
  War Thunder's cockpit ILS state.
- Fixed full-screen preview geometry using absolute design coordinates while
  the range label used CCIP-relative coordinates. Both now share the exact
  export transform.

`HeliSightBuilder.exe` SHA-256:

```text
16FF23188E2A25898F8A066625B4F30C3CDBDF3B43A23D74F743A6236710598C
```

## v1.5.1 - 2026-07-03

### Improved

- Rebuilt SVG path import around complete line-art geometry instead of reading
  only straight path commands.
- Added cubic and quadratic curves, smooth curves, elliptical arcs, relative
  coordinates, repeated path data, and closed subpaths.
- Added nested SVG matrix, translate, scale, rotate, and skew transforms.
- Curves are flattened adaptively so detailed converter output stays smooth
  without using a fixed and unnecessarily huge number of segments.
- Heavy imports are simplified below visible pixel detail and compiled into
  native polylines capped at the official HUD's seven-point maximum.
- Added **Mirror Selected (CCIP)** to duplicate selected geometry across the
  vertical axis through the current CCIP origin.
- Detailed imports retain their visible contours while the editor uses the
  dense-art rendering and autosave optimizations introduced in v1.5.0.

### Tested formats

- Photoshop SVG conversion.
- Potrace-style SVGs using relative curves and an inverted group transform.
- Converter output using a separate translation on every path.
- Mixed straight, cubic, quadratic, smooth, arc, and transformed paths.

`HeliSightBuilder.exe` SHA-256:

```text
E572A2ED9E474EF9384E19F7800CAA71C6255AC16CCADFF3C4D344ED6F470F1D
```

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
