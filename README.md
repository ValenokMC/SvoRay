# SvoRay

A simplified Windows VPN client built on the open-source [v2rayN](https://github.com/2dust/v2rayN) 7.24.4 codebase.

You paste your own subscription link or a single profile, pick a server, and press one button. Everything else — TUN routing, DNS, adapter selection — is configured for you. The full v2rayN interface stays one click away for anything the simple screen does not cover.

**Русская версия этой страницы: [README_RU.md](README_RU.md).**

---

## What it does

- one screen: connection status, a power button, a profile selector
- imports an HTTPS subscription or an individual `vless://`, `hysteria2://` and other links v2rayN supports
- two connection modes: TUN for the whole system, or the Windows system proxy
- a domain list that decides what the VPN carries: everything except the list, or the list only
- picks the active Ethernet/Wi-Fi adapter and local IPv4 automatically
- excludes the selected server's IP from TUN so traffic cannot loop
- Cloudflare DNS: `1.1.1.1` and `https://1.1.1.1/dns-query`
- tray icon that shows state by shape, not only by colour
- latency check for the selected profile, measured through the proxy rather than by a bare TCP handshake
- the complete v2rayN interface for manual settings and diagnostics

## What it does not do

SvoRay routes your traffic through a server you supply. It does not provide anonymity, and it cannot guarantee the absence of leaks. It comes with no servers — you bring your own subscription.

## Install

1. Download `SvoRay-<version>-setup.exe` from [Releases](../../releases).
2. Optionally verify the download against `SHA256SUMS.txt` published with the release:

   ```powershell
   Get-FileHash .\SvoRay-0.3.0-setup.exe -Algorithm SHA256
   ```

   The build is not signed with a commercial certificate, so comparing the hash is the only way to confirm you got the right file.
3. Run the installer. **Windows SmartScreen may warn about an "Unknown publisher"** — same reason: no commercial signature. Choose *More info* → *Run anyway* if you decide to continue.
4. Confirm the UAC prompt. Administrator rights are required because TUN mode does not work without them.

A Start-menu shortcut is created. No desktop shortcut.

## First run

1. Launch SvoRay and confirm the UAC prompt.
2. Paste your subscription link or a single profile into the field and press **Импортировать**.
3. The form is replaced by a profile selector, and the first usable profile is selected automatically.
4. Pick a mode: **Прокси** covers applications that honour the Windows system proxy, **TUN** covers the whole system.
5. Press the power button. Press it again to disconnect.
6. **Проверить** measures latency for the selected profile.
7. **Маршрутизация** edits the domain list. Type `example.com`; subdomains are matched too, and DNS for a listed domain takes the same path as its traffic.
8. **Настройки** opens the full v2rayN interface.

## Update

Run the new installer over the current version. Settings, subscriptions and profiles are kept. The installer closes the running client and core before replacing files.

## Uninstall

*Settings* → *Apps* → *SvoRay* → *Uninstall*. The uninstaller asks separately whether to delete your settings, subscriptions and profiles; by default it keeps them.

## Where your data is stored

```
%LOCALAPPDATA%\SvoRay
```

**This folder contains your subscription URL and profile credentials in clear text.** Do not send it to anyone and do not attach it to bug reports. Attach only the relevant lines from `guiLogs`, after checking them.

Because the client runs elevated, `%LOCALAPPDATA%` belongs to the account used to elevate. With an ordinary UAC confirmation by the same user, that is their own profile.

## Build from source

Requires the .NET 10 SDK and Inno Setup 6 (`winget install --id JRSoftware.InnoSetup`).

```powershell
.\build\BuildInstaller.ps1
```

The script publishes a self-contained build, adds the v2rayN/Xray/sing-box/geodata components, and produces the installer, a portable ZIP, a source ZIP and `SHA256SUMS.txt` in `dist`.

Core binaries (`xray.exe`, `sing-box.exe`, `wintun.dll`) are copied from a local official v2rayN installation; the path is the `-CoreSource` parameter.

## Status

Version 0.3.0. What changed in each version: [CHANGELOG.md](CHANGELOG.md).

A pre-publication security audit has been completed — see [docs/SECURITY_AUDIT.md](docs/SECURITY_AUDIT.md) for scope, findings, fixes and remaining risks. Notable fixes it produced: the downloaded subscription payload no longer reaches the log files, and profile links are kept out of the on-screen notice stream.

Verified: clean install and first launch, install over a previous version, import and subscription update, latency check, tray behaviour, 80/80 unit tests, no threats found by Microsoft Defender in any release artifact.

Not verified yet: uninstall with both answers to the user-data prompt, Windows display scaling at 125 % and 150 %, and a live run of the 0.3.0 connection modes and domain routing.

Known open defect: a single unexplained `OutOfMemoryException` was observed once, raised while the thread pool was creating a worker thread. It has not been reproduced. If the client hangs or exits unexpectedly, please do not close the process — capture a dump first and open an issue.

## License

GPL-3.0. The complete corresponding source is published with every binary release as `SvoRay-<version>-source.zip`.

See [LICENSE](LICENSE), [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md), and the upstream project: <https://github.com/2dust/v2rayN>.

SvoRay is an independent fork. It is not affiliated with or endorsed by the v2rayN project.
