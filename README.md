# NuclearVOIP

`NuclearVOIP` adds in-game voice chat to `Nuclear Option`.

It is a client-side BepInEx mod with push-to-talk voice, team/global transmission modes, 10 radio channels, an in-game talking list, and Opus-based audio transport over the game's Steam networking.

## Features

- Push-to-talk team voice
- Push-to-talk global voice
- 10 selectable team radio channels
- In-game talking list UI
- Opus voice encoding with packet-loss handling
- Known-good bundled `opus.dll` in release packages

## Requirements

- `Nuclear Option`
- `BepInEx 5`

BepInEx install guide:

- https://docs.bepinex.dev/articles/user_guide/installation/index.html

## Multiplayer

- This is a client-side mod.
- Players who want to talk to each other need the mod installed.
- There is no separate dedicated voice server in this repo.

## Installation

1. Install BepInEx for `Nuclear Option`.
2. Download a release archive from GitHub releases.
3. Extract the `NuclearVOIP` folder from the archive into `BepInEx/plugins/`.

The final layout should look like this:

```text
BepInEx/
  plugins/
    NuclearVOIP/
      NuclearVOIP-Bep5.dll
      opus.dll
```

Use the `Bep5` DLL for BepInEx 5.

## Controls

- `V`: team push-to-talk
- `C`: global push-to-talk
- `/`: change radio channel

## Configuration

Config entries are created automatically after the mod loads successfully.

You can configure the mod through:

- `BepInEx/config`
- BepInEx Configuration Manager, if you have it installed

Current settings include:

- talk key
- all-talk key
- change channel key
- microphone gain
- output gain

## Local Install Script

For local development installs, this repo includes:

- `scripts/install-mod.sh`

Copy `.env.example` to `.env`, set `STEAMAPPS_DIR`, then run:

```bash
./scripts/install-mod.sh -v
```

## Creating A Release

Because this project builds against game assemblies from a local paid install, a hosted GitHub Actions build is not a great fit.

For local release publishing, use:

- `scripts/create-release.sh`

Example:

```bash
./scripts/create-release.sh v0.5.1
```

That script will:

- build `Release-Bep5`
- package the BepInEx 5 DLL with the vendored `opus.dll`
- create a GitHub release with `gh`

## Notes

- The release package includes a known-good `opus.dll` from the upstream NuclearVOIP release.
- This repo vendors the AtomicFramework networking/lifecycle subset directly into the plugin build.
