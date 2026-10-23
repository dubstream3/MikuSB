# MikuSB

<strong>MikuSB</strong> is a server emulator of a certain dungeon anime game.   
`SdkServer`, `GameServer`, and an optional local HTTP/HTTPS proxy are started from a single `net10.0` application.  

[Discord](https://discord.gg/6MvWY774hX)

[English](README.md) | [日本語](README_jp.md)

## Scam Warning

MikuSB is completely free and open source.
If anyone sold you this server or charged money to provide it, that was a scam.
Request a refund immediately and report the seller to us on Discord with any relevant proof or purchase details.

## Overview

- `SdkServer`
  - Serves HTTP APIs and dispatch responses
  - Returns server lists, version queries, and fallback responses
  - Reports version `3.6.0` for that channel
- `GameServer`
  - Accepts TCP-based game connections
  - Handles `ReqCallGS` and the game packets behind the supported systems
- `Proxy`
  - Listens on `127.0.0.1:18888` when enabled
  - Redirects some Snowbreak-related domains to the local `SdkServer`
- `Common` / `Proto` / `TcpSharp`
  - Shared data, protobuf definitions, and networking infrastructure

## Project Layout

- [MikuSB](MikuSB): entry point
- [SdkServer](SdkServer): HTTP server and dispatch
- [GameServer](GameServer): main game server
- [Proxy](Proxy): local proxy
- [Common](Common): config, database, and shared utilities
- [Proto](Proto): protobuf definitions

## Requirements

- [.NET SDK 10.0](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

## Running

1. Restore dependencies and build.
```powershell
dotnet build
```
2. Set `GamePath` in `Config.json` to the path of your game executable.
3. Start the server and run the `game` command.
4. Create an account in the server console.  
5. Enjoy.

### Local overrides

`Config/Config.json` is tracked by git, so keep machine-specific values (`GamePath`, `Arguments`, your LAN address) in `Config/Config.Local.json` instead. It is merged over `Config.json` at startup, and while it exists the server does not write back to `Config.json`.

## Building a release package

```powershell
pwsh -File build.ps1
```

Produces `build/MikuSB-win-x64/`, `build/MikuSB-win-x64.zip` and `build/MikuSB-win-x64.zip.sha256`. Debug symbols are dropped by default, because a locally built assembly records the absolute build path; pass `-WithSymbols` if you need them. On a machine without access to nuget.org, restore from a local cache with `-Source <folder>,<folder> -NoReadyToRun`.

## Running a specific channel

This fork follows a specific channel of the client and carries fixes on top of upstream:

- DLC activity and era progression, including the client-side Lua payloads the server has to push for activities the client time-gates on its own
- Level-run settlement and per-difficulty clear counts for Defend, Repression and Rogue3D
- Wider packet coverage across activity, quest and shop flows

LAN / direct-host co-op was dropped. It is not part of this branch.

That channel needs more than a normal login does:

- **Client build 3.6.x.** The SDK handshake answers on channel `pc_jinshan` with SDK version `5.3.6`; newer builds of that channel need packet definitions this fork does not have.
- **The `jinshan` channel at launch.** `Loader.Arguments` ships `-ChannelID=jinshan`; without it the client never asks for that channel's SDK endpoints.
- **Seven HTTPS ports.** Besides `HttpServer.Port` (21500) the server binds six more (443, 11443, 13443, 18443, 19443, 31443), because that channel's SDK talks to several fixed endpoints that each sit on their own port. Port 443 has to be free.
- **TLS.** Certificates are self-signed per host name and cached in `sdk-certs/`. If the profile refuses to import them, Kestrel logs `Failed to start the SDK/HTTP (dispatch) server` and login cannot finish.
- **Local proxy or `hosts`.** With `Proxy.Enabled` and `Loader.SetAllProxy` (both on by default) the client's SDK traffic goes to `127.0.0.1:18888` and is forwarded to those ports; with the proxy off, map those endpoints to `127.0.0.1` yourself.
- **Resources.** The first start downloads `Kei-Luna/MikuSB-Resource` into `Resources/`. Offline, put it there by hand: `item/templates/card.json` and `item/templates/weapon.json` must exist or data loading stops.
- **.NET 10 with the ASP.NET Core runtime.** The published zip is framework-dependent.

If you skip the proxy, `/game-cn` from the server console does the mapping: it writes a fenced block in `hosts`, flushes the resolver cache and starts the client **without** injecting `Patch/MikuSB-Patch.dll`. Editing `hosts` needs administrator rights — an unelevated server still runs, but the command then prints the block instead of writing it. `Loader.ManageCnHosts` (on by default) keeps that block for as long as the server runs and drops it again on a clean shutdown.

The game server itself listens on `127.0.0.1:21000` (TCP).

## Feature List

* [x] Login and basic account entry
* [x] Inventory
* [x] Combat
* [x] Character
* [x] GM menu
* [x] Weapons
* [x] Support systems
* [x] Apocalypse
* [x] Actus
* [x] Neuronics
* [x] Character skins
* [x] Weapon skins
* [x] Gacha
* [x] Main story chapters
* [x] Dorm / base
* [x] Mini-games
* [x] Underground cleanup
* [x] Neuron simulation
* [x] Tactical assessment
* [x] Episode stories
* [x] Routine operations
* [x] Paradox maze
* [x] Lobby editing
* [x] Planet exploration
* [x] Dream picture book
* [x] DLC activities and era progression
* [ ] Multiplayer (LAN / direct-host co-op) — abandoned
* [x] Shop
* [x] Honor dispute

## Contributors
- [Naruse](https://github.com/DevilProMT)
- [Kei-Luna](https://github.com/Kei-Luna)

## License
This repository is licensed under the [MIT License](LICENSE).

## Notes on use
This software is intended for research and testing purposes in a local environment.  
It is not intended for unauthorized access to, interference with, or commercial use of official services.

## Legal Disclaimer
MikuSB was developed for educational and research purposes.  
- All trademarks, copyrights, and other intellectual property related to the original game and its associated franchise belong to their respective owners.  
- This repository does not include any copyrighted game assets, binaries, or master data.  
- Use this software at your own risk. The authors assume no responsibility for any damages or legal consequences resulting from its use.  

If you are a rights holder and have any concerns regarding this software, please contact `devilpromt` or `kei_luna_re` on Discord.
