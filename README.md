# 🚀 NavShelf

A lightweight, high-performance, native Bookmark Bar for **Windows 11 & Windows 10 File Explorer**, bringing a Google Chrome/Edge-style bookmarking experience directly into Windows File Explorer.

![NavShelf](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue?logo=windows)
![Framework-.NET Framework 4.8](https://img.shields.io/badge/.NET-4.8-512BD4?logo=dotnet)
![License-MIT](https://img.shields.io/badge/License-MIT-green)

---

## ✨ Key Features

- 📌 **Native File Explorer Integration**: Embeds seamlessly into Windows 11 & 10 File Explorer (`CabinetWClass`) with zero-gap docking.
- 🗂️ **Multi-Tab Support (Windows 11)**: Fully compatible with Windows 11 tabbed File Explorer. Seamlessly switch tabs without losing the bookmark bar.
- 🖱️ **Mouse Middle-Click Navigation**: Middle-click (scroll wheel click) any bookmark to open that folder in a **New Tab** instantly, just like in Google Chrome!
- 🌓 **Dynamic Real-time Dark & Light Theme**: Automatically detects and instantly transitions between Windows Dark Mode and Light Mode with native Fluent design colors.
- 📂 **Common File Dialog Support**: Automatically attaches to `#32770` Open/Save file dialogs in third-party apps (WhatsApp, Chrome, Notepad, Photoshop, etc.) without altering dialog controls or covering Open/Cancel buttons.
- 🖱️ **Drag & Drop**: Simply drag and drop any folder onto the bookmark bar to add it.
- ⚙️ **Customizable**:
  - Custom bookmark names, icons (Folder, Drive, Download, Code, Image, Music, Star, etc.), and custom accent colors.
  - Adjustable bar height, item spacing (margin), and button padding.
- ⌨️ **Global Hotkey & Quick Exit**: Press `Ctrl + Shift + B` anywhere to toggle visibility, or click the quick Exit (`✕`) button right on the bar to cleanly close the application.
- 💻 **System Tray & Auto Startup**: Runs silently in the system tray with an optional "Launch on Windows Startup" feature.
- 🪶 **100% Lightweight & Portable**: Zero third-party dependencies, purely built using WPF and Win32 APIs (~108 KB single binary).

---

## 🛠️ Requirements & Tech Stack

- **OS**: Windows 10 (1809+) or Windows 11 (all versions, including 22H2/23H2/24H2)
- **Framework**: .NET Framework 4.8 (pre-installed on modern Windows)
- **Language**: C# / WPF / Win32 Interop

---

## 🏗️ Building from Source

You can build the project using Visual Studio 2019/2022 or directly via MSBuild from the command line:

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe NavShelf.csproj /p:Configuration=Release /p:Platform=AnyCPU
```

The compiled standalone executable will be generated at:
```
bin\Release\NavShelf.exe
```

---

## 📦 Project Structure

```
NavShelf/
├── .gitignore
├── LICENSE
├── README.md
├── app.ico                   # Application icon
├── app.manifest              # Windows DPI awareness & OS compatibility manifest
├── App.xaml                  # Application entry point & tray management
├── App.xaml.cs               # App lifecycle, tray context menu, global hotkeys
├── NavShelf.csproj           # MSBuild project file
│
├── Models/
│   ├── AppSettings.cs        # Settings data contract (height, margin, padding, etc.)
│   └── BookmarkItem.cs       # Bookmark data contract (name, path, icon, color)
│
├── Services/
│   ├── BookmarkManager.cs         # Bookmark storage & JSON persistence
│   ├── ExplorerChildBarManager.cs # Native Win32 WS_CHILD window attachment & layout engine
│   ├── ExplorerComHelper.cs       # Shell.Application COM navigation (tabs, windows, dialogs)
│   ├── ExplorerHookEngine.cs      # Window tracking & shell hooks
│   ├── ThemeManager.cs            # Live Windows Dark/Light theme synchronization
│   └── Win32Helper.cs             # Win32 API declarations & window coordinate utilities
│
└── Views/
    ├── BookmarkBarControl.xaml    # The main bookmark bar UI & items control
    ├── BookmarkBarControl.xaml.cs # User interactions, drag-drop, click, middle-click
    ├── BookmarkEditDialog.xaml    # Dialog for adding/editing bookmarks & choosing icons
    ├── BookmarkEditDialog.xaml.cs
    ├── SettingsWindow.xaml        # Fluent Settings UI (height, spacing, padding, startup)
    └── SettingsWindow.xaml.cs
```

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
