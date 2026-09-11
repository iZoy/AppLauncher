# AppLauncher

**A lightweight, local-first application launcher for Windows.**

> 中文说明：[README.zh-CN.md](README.zh-CN.md)

AppLauncher gives you a fast keyboard- and mouse-driven way to find and start applications without maintaining a catalog or account. It scans local Windows application locations, keeps the index locally, and stays out of the way in the notification area.

## Download

The [GitHub Releases](https://github.com/iZoy/AppLauncher/releases) page is the download location for published builds. The initial stable build targets `v1.0.0` and provides self-contained portable ZIP packages:

| Package | Use it on |
| --- | --- |
| `AppLauncher-v1.0.0-win-x64.zip` | Most Intel/AMD 64-bit Windows PCs |
| `AppLauncher-v1.0.0-win-arm64.zip` | Windows on ARM64 devices |

The packages will include the .NET 10 runtime, so installing .NET separately will not be required. x86 (32-bit) Windows and an installer are not planned for the first release.

The initial public build is not code-signed. Windows SmartScreen may show a warning for an unsigned download. Verify the listed SHA-256 value and keep the extracted files together.

## Preview
![AppLauncher launcher view](docs/launcher.png)

![AppLauncher search results](docs/search.png)

## Requirements

- Windows 10 version 1809 or later (Windows 11 recommended).
- One of the matching 64-bit architectures: x64 or ARM64.
- The launcher is portable: extract the ZIP to a folder and run `AppLauncher.exe`.

On Windows 11 the launcher uses the native Acrylic surface when available. On Windows 10 or unsupported systems it uses a compatible translucent fallback.

## Build from source

Install the stable .NET 10 SDK `10.0.400` or a later .NET 10 feature band on Windows, then run:

```powershell
dotnet restore .\AppLauncher.sln
dotnet build .\AppLauncher.sln -c Release --no-restore
```

To create a self-contained x64 package locally:

```powershell
dotnet publish .\AppLauncher.csproj -c Release -r win-x64 --self-contained true
```

The ARM64 package uses the same command with `-r win-arm64`. Build outputs and local application data are intentionally excluded from Git.

## Everyday operation

### Search and launch

- Type anywhere when the launcher is focused to search by application display name or executable name.
- Press **Enter** to launch the first matching result.
- Press **Ctrl+Enter** or **Shift+Enter** to launch the first result as administrator.
- Press **Esc** once to clear a search; press it again with an empty search box to hide the launcher.
- A normal left click on an application tile launches it.

### Application sections

With an empty search box, pinned applications appear above regular applications. The divider below the pinned section scrolls together with the list. While searching, all applications—including pinned ones—are shown as one result list.

### Application context menu

Right-click an application tile to:

- Launch normally or as administrator.
- Open its file location.
- Pin or unpin it.
- Rename its display name without renaming the executable.
- Hide it from the launcher.

Pinning, names, hidden paths, and launch counts are stored locally and survive a restart.

### Empty-area context menu

Right-click an empty area to:

- Add one or more `.exe` files or Windows shortcuts (`.lnk`).
- Scan and add a folder containing portable applications.
- Refresh the application list with a full scan.

The launcher remains visible while a selection or rename dialog is open.

### Tray and window behavior

- Left-click the tray icon to show or bring the launcher to the foreground.
- The tray menu contains language selection, taskbar pinning, and Exit.
- Clicking outside the launcher hides it automatically; the process remains available in the tray.
- The launcher uses a fixed 900×600 window and cannot be resized.

## Local data and privacy

AppLauncher is local-first. It has no account, telemetry, automatic update service, or upload feature. The source contains no network client for sending your data anywhere. Application discovery reads local Windows locations only.

Runtime data is stored under `%APPDATA%\AppLauncher`:

- `config.json` — language, pinned paths, hidden paths, custom names, and other preferences.
- `app_cache.json` — the locally discovered application index and launch statistics.
- `icons_clean\` — extracted local application icons.
- `crash.log` and `diagnostic.log` — local troubleshooting records, automatically limited to 1 MiB each with one rotated copy.

These files are not part of the source repository or Release ZIP. To remove your settings, exit AppLauncher and delete that folder manually. To upgrade, replace the extracted program folder; your `%APPDATA%\AppLauncher` data is left in place.

## Release notes

This is a small, local-first utility shared for convenience. Windows 11 x64 is the manually verified platform for this release. Windows 10 and ARM64 are cross-built and package-checked, but have not been hardware-tested in this project. Taskbar pinning can also depend on Windows policy and shell support.

Please report reproducible problems through the repository's [Issues](https://github.com/iZoy/AppLauncher/issues) page. Before attaching logs, remove usernames, application paths, and other local details.

AppLauncher is inspired by desktop application launchers and is not affiliated with Apple Inc. The project does not distribute Apple fonts, icons, screenshots, or code.

## License

AppLauncher is released under the [MIT License](LICENSE). The project icon is original to this project. The portable packages also contain the applicable .NET runtime license and third-party notices.
