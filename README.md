<div align="center">

# NavShelf — Bookmark Bar for Windows File Explorer

**The fastest way to access your favourite folders in Windows 11 & 10 File Explorer.**  
Pin folders to a Chrome-style bookmark bar, open them in new tabs with middle-click, and never dig through the sidebar again.

[![Download](https://img.shields.io/badge/⬇️%20Free%20Download-NavShelf%20v1.0.1-0066ff?style=for-the-badge)](https://navshelf.nexonic.dev/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D4?style=for-the-badge&logo=windows)](https://navshelf.nexonic.dev/)
[![License: MIT](https://img.shields.io/badge/License-MIT-22c55e?style=for-the-badge)](LICENSE)
[![.NET Framework](https://img.shields.io/badge/.NET_Framework-4.8-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![Size](https://img.shields.io/badge/Size-~108%20KB-f59e0b?style=for-the-badge)](https://navshelf.nexonic.dev/)

🌐 **Website:** [navshelf.nexonic.dev](https://navshelf.nexonic.dev/) &nbsp;|&nbsp; ⭐ **Star this repo if you find it useful!**

![NavShelf — Bookmark Bar for Windows File Explorer](screenshot.jpg)

</div>

---

## What is NavShelf?

NavShelf is a free, open-source Windows utility that adds a **horizontal bookmark bar** directly beneath the tabs of Windows 11 and Windows 10 File Explorer — the same concept as the bookmarks bar in Google Chrome or Microsoft Edge, but for your local **folders**.

> **Search terms:** Windows File Explorer bookmark bar · Windows Explorer favorites bar · pin folders Windows 11 · open folder in new tab Windows · Windows 11 file explorer extension · Windows folder shortcuts

---

## ✨ Key Features

| Feature | Description |
|---|---|
| 📌 **Native Explorer Embed** | Docks seamlessly under Explorer tabs with zero gap. No overlay, no separate window. |
| 🖱️ **Middle-Click → New Tab** | Scroll-wheel click any bookmark to open the folder in a new Explorer tab, Chrome-style. |
| 🌓 **Live Dark / Light Mode Sync** | Automatically follows your Windows theme in real time with native Fluent colors. |
| 📂 **Open & Save Dialog Support** | Works inside file dialogs in VS Code, Photoshop, Chrome, and every other Windows app. |
| 😀 **Emoji & Custom Icons** | Give each bookmark a custom icon (folder, code, music, star…) and emoji prefix. |
| ⌨️ **Global Hotkey** | Press `Ctrl + Shift + B` from anywhere to toggle the bar. |
| 🪶 **Ultra Lightweight & Portable** | Single ~108 KB `.exe`. No installer, no admin rights, no registry edits needed. |
| 🛡️ **100% Safe** | Uses the official Win32 `SetParent` API. Zero DLL injection, zero system file patches. |
| 💻 **System Tray & Auto-Startup** | Runs quietly in the tray with an optional "Launch on Windows Startup" setting. |
| 🗂️ **Bookmark Folders** | Group bookmarks into Chrome-style folders that open as a dropdown menu. |
| 🔽 **Subfolder Browsing** | Hover a bookmark and click its **▾** to jump straight into any subfolder (or any drive from *This PC*). |
| 📥 **Drop Files to Copy / Move** | Drag files onto a bookmark to copy or move them into that folder, with Windows' own progress and undo. |
| 🔢 **Alt + 1…9 Shortcuts** | Open your first nine bookmarks from the keyboard in Explorer and file dialogs. |
| 💾 **Export / Import** | Back up your bookmarks to a file, or move them to another PC (merge or replace). |
| 👻 **Missing Folder Detection** | Bookmarks whose folder is gone (deleted, unplugged USB, offline network drive) are dimmed automatically. |
| 🛟 **Crash-Safe Storage** | Bookmarks are saved atomically with an automatic backup, so a crash or power cut can't wipe them. |

---

## 🚀 Quick Start

1. **[Download NavShelf.exe](https://navshelf.nexonic.dev/)** (~108 KB)
2. **Double-click** `NavShelf.exe` — no install, no UAC prompt.
3. **Open Windows File Explorer** — the bookmark bar appears automatically beneath the tabs.
4. **Drag any folder** onto the bar to pin it, or right-click a bookmark to edit, rename, or remove it.
5. **Middle-click** a bookmark to open it in a new Explorer tab.

---

## ⌨️ Keyboard & Mouse Shortcuts

| Action | Shortcut |
|---|---|
| Open folder in current tab | Left Click |
| Open folder in **New Tab** | Middle Click (Scroll Wheel) |
| Open folder in New Window | Ctrl + Left Click |
| Open the 1st … 9th bookmark | `Alt + 1` … `Alt + 9` (in Explorer & file dialogs) |
| Browse subfolders of a bookmark | Hover the bookmark → click the small **▾** on its right edge |
| Open a bookmark folder (group) | Left Click the folder → dropdown |
| Rename / change icon / delete | Right Click → Context Menu |
| Move a bookmark into a folder | Drag it onto the **middle** of the folder, or Right Click → *Move to Folder* |
| Create a bookmark folder | Right Click empty bar space → *New Bookmark Folder…* |
| Reorder bookmarks | Drag a bookmark to the **left/right edge** of another |
| Copy / move files into a bookmarked folder | Drag files onto the **middle** of the bookmark (`Ctrl` = copy, `Shift` = move) |
| Add a new bookmark | `+ Add` button, or drop a folder on the bar |
| Toggle bookmark bar visibility | `Ctrl + Shift + B` |
| Close / exit NavShelf | `✕` button on the bar |

---

## 📖 How to Use

### Adding bookmarks
- Click **`+ Add`** on the bar to bookmark the folder you are currently viewing. You can change its name, path and icon before saving.
- Or drag any folder from Explorer and drop it on an empty part of the bar (or on the **left/right edge** of an existing bookmark to place it exactly there).
- Right-click empty space on the bar for *Add Current Folder…*, *New Bookmark Folder…* and *Settings…*.

### Opening folders
- **Left click** opens the folder in the current tab. **Middle click** opens it in a new tab. **Ctrl + click** opens a new window.
- **`Alt + 1` … `Alt + 9`** opens the bookmark at that position on the bar. Hover any bookmark to see its number in the tooltip. If that position holds a bookmark folder, its dropdown opens instead. The shortcut only works while File Explorer or an Open/Save dialog is the active window. Everywhere else the keys reach your other apps as normal. You can turn it off in **Settings**.
- In **Open / Save dialogs** (WhatsApp, Chrome, Office, VS Code…), clicking a bookmark jumps the dialog straight to that folder.

### Bookmark folders (groups)
Keep the bar tidy by grouping related bookmarks, the same way as Chrome's bookmark folders.

1. Right-click empty space on the bar → **New Bookmark Folder…**, type a name and press **Save**.
2. Drag a bookmark onto the **middle** of the folder (it gets a blue outline) to move it inside. You can also right-click a bookmark → **Move to Folder ▸** and pick a folder, or choose *New Folder…* there.
3. Click the folder to open its dropdown. Each entry opens like a normal bookmark (click / middle-click / Ctrl+click), and hovering an entry shows its subfolders.
4. **Right-click an entry in the dropdown** to edit it, open it in a new tab or window, move it to another folder, **Move Out to Bar**, or delete it.
5. Right-click the folder itself to **Rename**, **Add Current Folder Here**, or **Delete Folder**. Deleting a folder never deletes its bookmarks: they move back onto the bar.

Dropping folders from Explorer onto the middle of a bookmark folder adds them to it.

### Browsing subfolders (▾)
Hover a bookmark and a small **▾** appears on its right edge. Click it to see every subfolder, then hover to go deeper level by level. Click any entry to open it, or middle-click to open it in a new tab. On the **This PC** bookmark the ▾ lists all of your drives. Folders are sorted the same way as in Explorer, and hidden or system folders are left out. A slow or offline network drive shows *"Folder is not responding"* instead of freezing the bar.

### Copying or moving files with a drop
Drag files or folders from Explorer onto the **middle** of a bookmark. The bookmark gets a blue outline, and when you release, the files are put **inside** that folder:

| Situation | Result |
|---|---|
| Same drive | **Move** |
| Different drive | **Copy** |
| Hold `Ctrl` while dropping | Always **Copy** |
| Hold `Shift` while dropping | Always **Move** |

The transfer uses the Windows shell, so you get the normal progress window and *Replace / Skip* prompts, and you can undo it with `Ctrl + Z` in Explorer. NavShelf won't let you drop a folder into itself or into the folder it is already in.

Drop on the **left or right edge** of a bookmark instead to create a new bookmark at that spot.

### Missing folders
A bookmark whose folder no longer exists is shown **dimmed**, and its tooltip says *"Folder not found"*. This covers a deleted folder, an unplugged USB drive or a disconnected network drive. NavShelf re-checks in the background every few seconds, so the bookmark returns to normal as soon as the drive is connected again.

### Backing up & moving to another PC
Open **Settings** (⚙ on the bar, or double-click the tray icon):
- **Export…** saves all bookmarks, including folders, to a `.json` file.
- **Import…** loads such a file. Choose **Merge** to keep your current bookmarks and add only the new ones (duplicates are skipped), or **Replace** to start fresh. An invalid file is rejected without changing anything.

NavShelf also protects your bookmarks on its own. Every save goes to a temporary file first and keeps the previous version as `bookmarks.json.bak`. If the bookmarks file is ever damaged, NavShelf restores it from that backup automatically, and keeps the damaged copy aside instead of overwriting it.

---

## 🆕 What's New

### Unreleased
**New features**
- 🗂️ **Bookmark folders**: group bookmarks into dropdown folders. Drag bookmarks in, use *Move to Folder*, rename, and delete (bookmarks are kept).
- 🔽 **Subfolder browsing**: hover a bookmark and click its ▾ to navigate subfolders level by level. *This PC* lists your drives.
- 📥 **Drop files onto a bookmark** to copy or move them into that folder (Explorer-style rules, progress dialog and undo).
- 🔢 **`Alt + 1…9`** opens the bar's first nine bookmarks in Explorer and file dialogs. Can be turned off in Settings.
- 💾 **Export / Import** bookmarks from Settings (merge or replace).
- 👻 **Missing folders are dimmed** and re-checked automatically.

**Fixes & improvements**
- In Open/Save dialogs (e.g. WhatsApp), clicking a bookmark sometimes only put the path into the *File name* box instead of opening the folder. Fixed.
- After maximizing or restoring an Explorer window, the bar could shift upward, sometimes covering the minimize / maximize / close buttons, or look broken. It now stays exactly under the header at any DPI.
- Bookmarks and settings are saved crash-safely with an automatic backup and recovery. Previously a damaged file was replaced by the defaults.
- Fixed a memory leak where bars from closed Explorer windows and file dialogs were never released.

---

## 🛠️ Tech Stack & Requirements

- **OS:** Windows 10 (1809+) or Windows 11 (22H2 / 23H2 / 24H2+)
- **Runtime:** .NET Framework 4.8 *(pre-installed on all modern Windows)*
- **Language:** C# + WPF + Win32 Interop
- **Size:** ~108 KB (single portable `.exe`)

---

## 📦 Project Structure

```
NavShelf/
├── App.xaml / App.xaml.cs          # Entry point, system tray, global hotkeys
│
├── Models/
│   ├── AppSettings.cs              # Settings schema (height, spacing, padding, shortcuts)
│   ├── BookmarkDisplayItem.cs      # Per-chip UI state (missing, drop target, ▾)
│   └── BookmarkItem.cs             # Bookmark schema (name, path, icon, color, folder children)
│
├── Services/
│   ├── BookmarkManager.cs          # Bookmarks, folders, export/import, crash-safe JSON storage
│   ├── ExplorerChildBarManager.cs  # Win32 WS_CHILD window attachment & layout
│   ├── ExplorerComHelper.cs        # Shell COM navigation (tabs, windows, dialogs)
│   ├── ExplorerHookEngine.cs       # Window tracking & shell hooks
│   ├── KeyboardShortcutService.cs  # Alt+1..9 shortcuts (active only in Explorer / file dialogs)
│   ├── ThemeManager.cs             # Live Windows Dark/Light theme sync
│   └── Win32Helper.cs              # Win32 API declarations, shell copy/move & utilities
│
└── Views/
    ├── BookmarkBarControl.xaml     # Main bookmark bar UI
    ├── BookmarkEditDialog.xaml     # Add / Edit bookmark dialog
    └── SettingsWindow.xaml         # Settings UI (sizing, startup, shortcuts, export/import)
```

---

## 🤔 Frequently Asked Questions

**Q: Does NavShelf work with Windows 10?**  
A: Yes. NavShelf supports Windows 10 (1809 and later) and all versions of Windows 11.

**Q: Is NavShelf safe? Does it modify system files?**  
A: NavShelf is 100% safe. It uses the official Win32 `SetParent` API — no DLL injection, no patching of `explorer.exe`, no registry modifications. The full source code is available here on GitHub.

**Q: Where are my bookmarks stored?**  
A: Bookmarks are saved in `%AppData%\ExplorerBookmarkBar\bookmarks.json`, with an automatic backup next to it (`bookmarks.json.bak`). The easiest way to back them up or move them to another PC is **Settings → Export… / Import…**.

**Q: I dropped files on a bookmark by mistake. Can I undo it?**  
A: Yes. NavShelf copies and moves files through the Windows shell, so press `Ctrl + Z` in File Explorer to undo, just like a normal drag-and-drop.

**Q: Will `Alt + 1…9` interfere with my other apps?**  
A: No. The shortcut only works while File Explorer or an Open/Save dialog with the bookmark bar is the active window. Everywhere else the keys pass through untouched. You can also switch it off in Settings.

**Q: Does it work in Open / Save file dialogs?**  
A: Yes. NavShelf attaches to the common file dialog window (`#32770`) used by virtually every Windows application — VS Code, Photoshop, Chrome, Notepad, and more.

**Q: How do I uninstall it?**  
A: Simply close NavShelf from the system tray and delete the `.exe`. There is nothing else to remove.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE) — free for personal and commercial use.

---

<div align="center">

Made with ❤️ for Windows power users &nbsp;|&nbsp; [navshelf.nexonic.dev](https://navshelf.nexonic.dev/)

</div>
