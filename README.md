# Miseria

[![build](https://github.com/mel0k1/Miseria/actions/workflows/build.yml/badge.svg)](https://github.com/mel0k1/Miseria/actions/workflows/build.yml)
[![license](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com)

A minimal Steam client built on [SteamKit2](https://github.com/SteamRE/SteamKit) with an [Avalonia](https://github.com/AvaloniaUI/Avalonia) UI styled by [FluentAvalonia](https://github.com/amwx/FluentAvalonia). Cross-platform, no WebView, no Steam client installation required.

## Status

- Password logon with Steam Guard prompts, refresh token saved between launches for automatic re-logon
- Friends list with live presence, avatars and disk-cached images
- Friend chat: messages, typing indicator, unread badges and server-side history
- Profile page: avatar, persona name, status, country, summary, member since and Steam id
- Library of owned apps through PICS with names, types, icons, search, list and Steam-style capsule grid
- Downloads through SteamContent: parallel chunk download from the Steam CDN with decryption, verification, progress and cancel
- Branch picker for depot branches, password-protected betas unlock through the private beta PICS request
- Family View detection at logon and PIN unlock through Steam's Parental service
- Install tracking, Play button (Wine for Windows games on Linux) and manifest-diff updates that redownload only changed files
- Four themes: Light, Dark, Crimson, Kiwi, persisted to disk
- Home dashboard with an account card and counters

The refresh token and guard data are stored unencrypted in `~/.config/Miseria/session.json` (`%APPDATA%\Miseria` on Windows) with user-only file permissions on Linux and macOS. Deleting the file forces the password prompt again.

## Screenshots

| Friends (Dark) | Chat (Dark) |
| --- | --- |
| ![Friends list in the Dark theme](docs/screenshots/friends-dark.png) | ![Friend chat in the Dark theme](docs/screenshots/chat-dark.png) |

| Library grid (Dark) | Downloads (Dark) |
| --- | --- |
| ![Library capsule grid in the Dark theme](docs/screenshots/library-grid-dark.png) | ![Download progress and updates](docs/screenshots/downloads-dark.png) |

| Profile (Dark) | Home (Dark) |
| --- | --- |
| ![Profile page in the Dark theme](docs/screenshots/profile-dark.png) | ![Home dashboard](docs/screenshots/home-dark.png) |

## Build

Requires the .NET 10 SDK.

```
git clone https://github.com/mel0k1/Miseria.git
cd Miseria
dotnet run --project src/Miseria.App
```

The screenshots in `docs/screenshots` are regenerated headlessly with Xvfb:

```
xvfb-run -a dotnet run --project tools/Screenshots -- docs/screenshots
```

## Roadmap

The long-term goal is a central hub client like Steam itself. Planned next:

- Marketplace: listings, price history and orders through the Steam Web API (SteamKit2 has no market handler)
- Trading platform: trade offers with inventory browsing
- Community: profiles, groups and activity feed

## License

GPL-3.0. See [LICENSE](LICENSE). Not affiliated with Valve; Steam is a trademark of Valve Corporation. Runtime dependencies: [SteamKit2](https://www.nuget.org/packages/SteamKit2) (LGPL-2.1-only), [FluentAvaloniaUI](https://www.nuget.org/packages/FluentAvaloniaUI) and [Avalonia](https://www.nuget.org/packages/Avalonia) (MIT).
