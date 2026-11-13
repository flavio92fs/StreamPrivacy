# Changelog

## 0.2.4 — 2026-11-13

- Add a dark interface, a light-theme option and saved theme preferences.
- Include Signal and more known chat clients in the convenience filter; show all detected apps by default for new profiles.
- Add true portable profiles using `portable.flag` and adjacent `Data/settings.xml`.
- Provide an offline, per-user installer with uninstallation support.
- Publish English and Italian documentation, an explicit privacy statement, MIT license and release checksums.
- Avoid reading window titles during normal operation. Explicit development tests can inspect titles to locate demonstration windows.

This is an experimental compatibility release. App detection and a successful Windows API response do not guarantee exclusion from every capture method.

## Earlier development versions

- 0.2.3: restore the main window after minimizing, hiding or moving it off-screen; add tray recovery.
- 0.2.2: make the public capture-test window protect itself automatically.
- 0.2.1: add multiple app checkboxes and stable, flicker-free refresh.
- 0.2.0: remember selected apps, watch for new windows and add startup/tray support.
