# Contributing

AppLauncher is a small Windows-first utility. Bug reports, usability feedback, and focused pull requests are welcome.

## Development setup

- Windows 10 version 1809 or later.
- Stable .NET 10 SDK `10.0.400` or a later .NET 10 feature band.

```powershell
dotnet restore .\AppLauncher.sln
dotnet build .\AppLauncher.sln -c Release --no-restore
```

Please test changes on Windows 11 x64 when possible. ARM64 is cross-built in CI but is not treated as hardware-tested without a physical ARM64 device.

## Pull requests

- Explain the user-visible behavior that changed.
- Keep changes focused and preserve the existing `%APPDATA%\AppLauncher` data format.
- Do not commit `bin/`, `obj/`, `publish/`, `release/`, logs, dumps, screenshots containing private data, or personal AppLauncher data.
- Before sharing diagnostics, remove usernames, application paths, and other local details.

## License

By contributing, you agree that your contribution may be distributed under the repository's MIT License.
