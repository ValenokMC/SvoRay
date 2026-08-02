# Changelog

All notable changes to SvoRay. Dates are the release dates of the installer published under
[Releases](../../releases). Detailed Russian notes live in [`docs/`](docs).

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
