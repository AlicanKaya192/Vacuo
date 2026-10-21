# Vacuo

Pulls in the clutter, leaves what matters.

Vacuo is a small Windows 11 maintenance tool that lives in the system tray. Click its icon in the bottom-right corner and a panel shows your disk, memory and messy folders in one place:

- **Disk:** where the used space goes, developer and AI caches (pip, uv, conda, Hugging Face, PyTorch, npm, Rust `target/`, old `.venv` and `node_modules`) with what it costs to download them again, your own large files, and what should stay.
- **Windows leftovers:** your temp folder, and with administrator approval, Windows Temp, update downloads and the DISM component cleanup.
- **Memory:** memory pressure instead of a "% used" number, apps that have been idle for hours, and the real difference after you close them.
- **Organize:** rules for Desktop, Downloads or any folder, with a preview before anything moves and one-click undo.
- **Duplicate files:** files with identical content; you choose which copy stays.
- **Uninstall:** each program's own uninstaller, then a list of leftovers.
- **Battery:** battery health from the battery driver, on laptops.
- **Automatic maintenance:** off by default; when on, at most once a day while the computer is idle and plugged in.
- **Freed this week** and the **Journal:** what was freed, every action, and how to undo it.

[Türkçe](README.tr.md)

## Download

Get `Vacuo-X.Y.Z-setup.exe` from [Releases](https://github.com/AlicanKaya192/vacuo/releases/latest) and run it. It installs for your user only and does not ask for administrator rights. Vacuo updates itself from the same page.

The setup is not code-signed, so Windows SmartScreen may warn you: choose "More info", then "Run anyway". Smart App Control may block it.

## Safe by design

- Nothing happens without your choice. Automatic maintenance is off by default and only moves things that can be undone.
- Files you remove go to the Recycle Bin, and Vacuo never empties it. The only exception is the administrator clean-up on the Windows screen (Windows Temp, update downloads, DISM): it asks for administrator approval, says it cannot be undone, and is logged in the Journal.
- No "RAM cleaning" and no force-closing apps. Closing sends a normal close request, so an app with unsaved work can ask you first.
- No registry cleaning, no services, drivers or system settings. Vacuo itself never runs as administrator; a separate short-lived helper asks only for the Windows screen's clean-up.
- No account and no telemetry. The only network connection is the update check against this repository's releases, and it can be turned off in Settings.

Built with C#, WinUI 3, Windows.UI.Composition and Win2D.
