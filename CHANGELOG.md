# Changelog

All notable changes to RdpSwitcher are documented here. The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

## [0.1.0] - 2026-11-14

First public release.

### Added

- Several simultaneous RDP sessions hosted in the Windows RDP ActiveX control (`mstscax.dll`), with instant switching between them.
- Full-screen borderless mode drawn by the application, per-monitor DPI aware.
- Optional secondary window for a single session.
- Auto-hiding panel at a configurable screen edge (left, right, top or bottom).
- Global hotkeys through a low-level keyboard hook, working while a session has focus.
- JSON configuration with connections, groups, colors, panel and hotkey settings (see `config.json.example`), plus dialogs to edit connections and settings.
- Credentials are never stored: the password is entered in the standard Windows CredSSP dialog.
- Unit tests for the logic that does not need COM (configuration, session manager, panel geometry, hotkey parser).

### Requirements

- Windows 10 or 11 and .NET Framework 4.8.
- To run the portable build: extract the archive and start `RdpSwitcher.exe`.
- To build from source: see the README (COM interop is generated with `tools\gen-interop.cmd`).
