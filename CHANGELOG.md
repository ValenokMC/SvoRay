# Changelog

All notable changes to SvoRay. Dates are the release dates of the installer published under
[Releases](../../releases). Detailed Russian notes live in [`docs/`](docs).

## 0.5.2 — 11 October 2026

Full notes: [docs/RELEASE_NOTES_0.5.2_RU.md](docs/RELEASE_NOTES_0.5.2_RU.md) (Russian).

### Security

- **Only local paths from launcher records and the registry are touched.** SvoRay runs elevated,
  and some launcher records sit in folders any user can write to. A planted `\\host\share` path
  would have made it open a network share and hand that host the user's NTLM credentials. Game
  discovery, final-path resolution and the server-routing geo files now accept only fully
  qualified paths on a local drive. Found by CodeQL.

### Added

- **A permanent download link**: every release also carries `SvoRay-setup.exe`, so
  <https://github.com/ValenokMC/SvoRay/releases/latest/download/SvoRay-setup.exe> always downloads
  the latest installer.

## 0.5.1 — 11 October 2026

Full notes: [docs/RELEASE_NOTES_0.5.1_RU.md](docs/RELEASE_NOTES_0.5.1_RU.md) (Russian).

The same program as 0.5.0 under a new version number, released so that 0.5.0 has something to
update to: it checks SvoRay's own update path end to end — the footer button, the confirmation,
the download, the SHA-256 check, the silent install and the restart.

## 0.5.0 — 10 October 2026

Full notes: [docs/RELEASE_NOTES_0.5.0_RU.md](docs/RELEASE_NOTES_0.5.0_RU.md) (Russian).

### Added

- **Game mode.** One switch on the main screen sends everything started from game folders past
  the tunnel while Discord, the browser and the rest stay on it. Steam libraries are found through
  the registry and `libraryfolders.vdf` on any drive; Epic, Battle.net (uninstall entries and
  `product.db`), EA app, Rockstar, GOG, Ubisoft, Riot on any drive, Lesta / Wargaming Game Center,
  HoYoPlay, Xbox game roots (`.GamingRoot`), Roblox, Easy Anti-Cheat and BattlEye are found from
  each launcher's own records, and folders can be added by hand in the routing window. Folders are
  matched by their final path, so junctions work, and anything as wide as a drive root or Program
  Files is refused from every source. It covers UDP game traffic too and can be switched
  mid-connection.
- **Rules from the server.** The routing profile a subscription sends in the Happ format — the
  same one Happ and INCY apply on the phone — now applies on Windows: block, proxy and direct
  groups in the server's order, after the user's own list. Its geo files are downloaded next to
  the stock ones and reached through `ext:`; a category a file lacks is left out instead of
  stopping Xray from starting. A switch in the routing window turns it off.
- **Several subscriptions.** The server row gains ↻ (refresh every subscription), Add (another
  subscription or a single server), Replace (swap the selected server's source) and … (remove a
  subscription or a single server, with a themed confirmation). The selector lists the servers of
  every subscription and names the subscription when there is more than one, from the
  `profile-title` header or the link's host. The same link added twice creates no duplicate, and
  servers added on their own no longer belong to whichever subscription was current.
- **Traffic left on the subscription** under the server row, from the selected server's
  `Subscription-Userinfo`, highlighted once 90 % of a quota is used.
- **SvoRay updates itself** from its GitHub releases: a daily check and one on demand, stable
  releases only, versions compared as numbers. The installer and `SHA256SUMS.txt` are taken only
  from that release's assets and the installer runs only when its SHA-256 matches; the system proxy
  is cleared and the core stopped first, the installer runs `/SILENT` and starts the new version.
  v2rayN's own self-update, which would have installed v2rayN, is disabled.

### Changed

- **A new look.** Graphite surfaces with one mint accent, a single connect button with distinct
  connecting and error states, a server row with the delay inside it, settings-style rows for
  game mode and routing, and a full-width Proxy | TUN switch whose halves each carry an "i" that
  explains that mode on hover or keyboard focus; confirmations and the tray menu use the same
  dark style instead of Windows' light dialogs. New application and tray icons: a ring, with tray states that differ by
  shape — off, connecting, on, game mode and error. The routing window and advanced mode's accent follow the same palette.
- **TUN is the default mode.** New installations start in TUN, and installations upgraded from
  0.4.x are switched to it once; a mode chosen after that is kept.
- **Smooth rendering.** SvoRay draws on the GPU by default (v2rayN's software-only default made
  the window stutter), existing installations are switched once, animations run at the
  monitor's refresh rate instead of WPF's 60 fps cap, and the large blurs and shadows are gone.

### Fixed

- **Nothing that goes direct worked in TUN mode** — Steam, local sites, games in game mode, the
  core's own DNS — wherever IPv4 forwarding was on for the physical adapter (software that shares
  the connection with a virtual network turns it on, e.g. the Codex sandbox's `vgate0`). Windows
  then ignores the interface binding the core relies on to send direct traffic past its own TUN.
  SvoRay now turns forwarding off on that adapter while TUN is up (active store only, re-checked
  every 20 s, recorded so a crash is undone at the next start) and back on afterwards. The same
  cause made the core hold ~16,000 UDP sockets, nearly every dynamic port, for up to ten minutes.
  As a safeguard, the core's own traffic coming back through its TUN is also dropped.
- Picking a server of another subscription mid-connection kept the previous subscription's rules
  and the game folders found at connect until the next reconnect.

- A failure while the main window initialises is now logged with its cause. Before, it vanished
  and left the server list empty; it also left the certificate policy without its config, so
  every subscription download fell back to a path that drops response headers. The certificate
  policy now reads the app config on its own, and exception logs keep the full inner-exception
  chain.

## 0.4.0 — 3 August 2026

Full notes: [docs/RELEASE_NOTES_0.4.0_RU.md](docs/RELEASE_NOTES_0.4.0_RU.md) (Russian).

### Added

- **Russian and English simple interfaces.** Every label, status, error, tray caption and domain
  routing message specific to SvoRay now comes from paired resources. A compact RU/EN selector in
  the header remembers the choice and says explicitly that the app must be restarted before it
  takes effect. A new installation starts in the Windows UI language when it is supported, with
  English as the fallback.
- **A support action that follows the subscription.** The simple profile card always has a
  **Support** button. When the subscription server supplies `Support-Url`, SvoRay remembers it;
  otherwise the button opens the project's GitHub Issues page.

### Changed

- Subscription downloads can retain response headers without changing the existing body-only API
  used by geo files and update checks. The support link is stored as a nullable column on the
  subscription record; sqlite-net adds it to existing databases without rebuilding them.
- Provider support links are treated as untrusted input. Only absolute HTTP, HTTPS and Telegram
  links up to 2,048 characters are accepted, and a link is opened only after an explicit click.

### Fixed

- The simple screen no longer stays partly Russian when the application culture is English. The
  routing window, dynamic connection messages, error paths, title and tray now use the same
  culture as the rest of the application.

## 0.3.1 — 3 August 2026

Full notes: [docs/RELEASE_NOTES_0.3.1_RU.md](docs/RELEASE_NOTES_0.3.1_RU.md) (Russian).

### Fixed

- **Off now means off on the server.** With the switch off, the core kept running: in proxy mode
  only the Windows proxy setting was removed, and merely launching the app started the core and
  probed the tunnel. Connections opened before the switch went off stayed open through it, and the
  app kept using its proxy port for subscription and geo updates, so the account stayed listed as
  online on the server panel — and traffic was still counted — while the app showed a disconnected
  VPN. Disconnecting now stops the core, and nothing that asks for a reload — startup, a different
  profile, a routing or DNS change, an imported subscription — starts it again while the switch is
  off.

### Changed

- In advanced mode, clearing the system proxy with the tunnel down is now the disconnected state
  and stops the core, matching what the tray on/off already switched between. **Do not change** and
  **PAC** are unaffected: both keep the core running for use through the local proxy port by hand.

## 0.3.0 — 2 August 2026

Full notes: [docs/RELEASE_NOTES_0.3.0_RU.md](docs/RELEASE_NOTES_0.3.0_RU.md) (Russian).

### Added

- **Connection mode selector on the simple screen: proxy or TUN.** TUN behaves as before; proxy
  mode sets the Windows system proxy instead, so applications that honour it go through the VPN
  and the rest stay direct. The choice is remembered between runs, and switching it on a live
  connection takes the old mode down through its own switch before bringing the new one up.
- **Domain routing.** A **Маршрутизация** window holds a list of domains and decides how it is
  applied: everything through the VPN except the list, or only the list through the VPN.
  `example.com` covers its subdomains, and pasted URLs, ports and `*.` prefixes are reduced to
  the same host. A whole list can be pasted at once in any separator shape, and a **Набор для
  РФ** button fills in the sites that commonly refuse a foreign address - state services, banks,
  Yandex, VK, marketplaces, operators - together with the static hosts they load their images
  from. The button also pulls the community-maintained list from
  [itdoginfo/allow-domains](https://github.com/itdoginfo/allow-domains) and merges it on top, so
  it works offline and gets fresher entries when it can reach the network. SvoRay generates its
  own routing profile and rebuilds it on every connect, which also makes DNS follow the traffic:
  a domain routed direct is resolved by the direct DNS server rather than through the tunnel.
- **A connection check that works while connected.** The check button used to be available only
  while disconnected. With the VPN up it now probes the running tunnel through the core's own
  proxy port and reports the exit country and the delay - the question an IP-check site is
  usually opened for, answered without leaving the app and without trusting the browser's proxy
  settings.

### Changed

- **The simple screen is a phone-shaped window** — 420×780 by default, 360×560 minimum. Advanced
  mode keeps its desktop size, and each mode remembers its own size for the session.
- The card scrolls instead of clipping, the header is compact, and the advanced-mode button is
  now an icon.

### Fixed

- At the default window size the bottom of the card could be cut off with no way to scroll to it.
- After a subscription update the refresh button relabelled itself with a longer caption that no
  longer fit.

## 0.2.0 — 1 August 2026

Full notes: [docs/RELEASE_NOTES_0.2.0_RU.md](docs/RELEASE_NOTES_0.2.0_RU.md) (Russian).

### Added

- One dynamic block on the main screen: the import form is replaced by a profile selector once a
  profile exists.
- Latency check measured through the proxy itself rather than by a bare TCP handshake.
- Tray icons that differ by shape, not only by colour, and a compact tray menu.
- A single downloadable installer built with Inno Setup 6, plus `SHA256SUMS.txt`.

### Changed

- Explicit connection-state model (off / connecting / on / error). `On` is reported only when the
  core process actually came up.
- Settings, subscriptions, profiles and logs moved to `%LOCALAPPDATA%\SvoRay`; data from 0.1.x
  next to the executable is migrated once on first launch.
- The profile selector shows the profile name only — never the server address or port.

### Fixed

- With more profiles than the tray menu limit the selector stayed empty.
- Subscription payloads no longer reach the log files, and profile links stay out of the
  on-screen notice stream.
