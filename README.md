# Vacuo

Make room on Windows, honestly.

Vacuo is a small Windows 11 maintenance tool that lives in the system tray. Click its icon in the bottom-right corner and a panel shows your disk, memory and messy folders in one place:

- **Disk:** developer and AI caches (pip, uv, conda, Hugging Face, PyTorch, npm, Rust `target/`, old `.venv` and `node_modules`) with what it costs to download them again, your own large files, duplicate files, and what should stay.
- **Memory:** memory pressure instead of a "% used" number, apps that have been idle for hours, and the real difference after you close them.
- **Organize:** rules for Desktop, Downloads or any folder, with a preview before anything moves and one-click undo.
- **Uninstall:** each program's own uninstaller, then a list of leftovers.
- **Log:** every action, and how to undo it.

[Türkçe](README.tr.md)

## Safe by design

- Nothing happens without your choice. Auto Care is off by default.
- Nothing is deleted permanently: files go to the Recycle Bin, and Vacuo never empties it.
- No "RAM cleaning" and no force-closing apps. Closing sends a normal close request, so an app with unsaved work can ask you first.
- No registry cleaning, no services, drivers or system settings. Vacuo does not ask for administrator rights.
- No network access and no telemetry.

## Status

Early development. Not ready to use yet.

Built with C#, WinUI 3, Windows.UI.Composition and Win2D.
