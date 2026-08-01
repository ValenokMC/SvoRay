# SvoRay

SvoRay is a simplified Windows VPN client based on the open-source v2rayN 7.24.4 codebase.

The project is intentionally kept separate from the VPS_SETUP server repository. The application accepts a user's own subscription or individual VLESS/Hysteria2 links and applies privacy-oriented Windows TUN defaults.

## Status

Version 0.2.0. A release consists of `SvoRay-0.2.0-setup.exe`, the portable Windows ZIP, and the matching source ZIP, with `SHA256SUMS.txt` covering all three.

See `README_RU.md` for installation, usage, and safety notes.

### What changed in 0.2.0

- One main card: status, power button, and a dynamic import/profile block. The separate import card and the "protection is automatic" panel are gone.
- Explicit connection states (off / connecting / on / error) reported from whether the core process actually started, not from the requested TUN flag.
- Profile drop-down matches the field width, flips above the field when there is no room below, shows ~5 rows, and never truncates the list for large subscriptions.
- Tray icons distinguish the four states by shape as well as colour, with a compact context menu. The full v2rayN commands stay in advanced mode.
- Labelled "Обновить подписку" button with waiting, success, and error states.
- User data moved to `%LOCALAPPDATA%\SvoRay`, with a one-time migration from 0.1.x portable layout.
- Single installer with in-place upgrade, Start-menu-only shortcut, and opt-in removal of user data on uninstall.

### Not done yet

The pre-publication security audit is complete — see [docs/SECURITY_AUDIT.md](docs/SECURITY_AUDIT.md), verdict: ready to publish.

A clean install and the first launch after it are verified. Four checks are still open and are not covered by the audit verdict: upgrading in place over a previous version, uninstalling with the user-data prompt, the Windows 125 % / 150 % display-scaling check, and a live connection and TUN test.

## Building

Requires the .NET 10 SDK and Inno Setup 6 (`winget install --id JRSoftware.InnoSetup`).

```powershell
.\build\BuildInstaller.ps1
```

## License

GPL-3.0. See `LICENSE`, `THIRD_PARTY_NOTICES.md`, and the upstream project at https://github.com/2dust/v2rayN.
