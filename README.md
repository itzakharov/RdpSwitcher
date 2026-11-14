# RdpSwitcher

A Windows desktop app (C#, WinForms, .NET Framework 4.8) for keeping several parallel RDP sessions and switching between them instantly in a full-screen borderless window. A panel at a configurable screen edge slides out on demand.

The RDP engine is the built-in Windows COM component `mstscax.dll` (`AxMsRdpClient9NotSafeForScripting`); the protocol is not reimplemented.

## Features

- Multiple simultaneous RDP sessions with instant switching
- Full-screen borderless mode, drawn by the app itself
- Auto-hiding panel at the left, right, top or bottom screen edge
- Global hotkeys that keep working while an RDP session has focus
- No stored passwords: credentials are entered in the standard Windows CredSSP dialog

Non-goals: multi-monitor, password storage, cross-platform support, RD Gateway.

## Download

Grab the latest portable build from the [Releases](../../releases) page, extract the archive and run `RdpSwitcher.exe`. Windows 10/11 with .NET Framework 4.8 is required.

The executable is not code-signed, so Windows SmartScreen may show "Windows protected your PC". Click **More info**, then **Run anyway**. Alternatively, unblock the archive before extracting it: right-click the zip, **Properties**, tick **Unblock**, **OK**.

## Build

Requirements:

- Windows 10/11 (`mstscax.dll` is Windows-only)
- .NET SDK 6 or newer (the projects target `net48`)
- Windows SDK with .NET Framework tools (`aximp.exe`), installed with Visual Studio 2022 or its Build Tools

From a **Developer Command Prompt for VS**, in the repository root:

```cmd
:: 1. Generate COM interop (once, and after an RDP client/Windows version change)
tools\gen-interop.cmd

:: 2. Build
dotnet build RdpSwitcher.sln -c Debug

:: 3. Test (logic only, no RDP connection needed)
dotnet test RdpSwitcher.sln -c Debug
```

Run `RdpSwitcher\bin\Debug\net48\RdpSwitcher.exe`. The configuration is created at `%APPDATA%\RdpSwitcher\config.json`; see `config.json.example` for the format.

## License

[MIT](LICENSE)
