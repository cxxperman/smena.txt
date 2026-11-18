# Working rules for this repo

This is an app of the dev.txt™ ecosystem (Windows, C#/WPF), made from the newapp.txt template. The launcher core.txt installs it, shows its settings and hosts the local hub. Shared design and the hub protocol live in the kit.txt repo.

## Ecosystem rules

1. Colours, radii, font sizes and durations come from `Tokens.cs`. Never write them as numbers in code.
2. The app works without the hub and without the other apps. The hub being absent is normal, not an error.
3. Everything the app tells the outside world goes through the hub (`HubLink`), never through a port of its own.
4. Buttons, switches, blocks, rows, status dots and the brand name come from `Ui.cs`. Do not draw your own.
5. Everyday settings are described in `AppHub.Settings()` and published as `<app>.settings`; core.txt draws them. State is always sent whole. After applying `settings.set`, publish the full state again, even if nothing changed.
6. Each product has its own pixel sprite: rows of `.`, `#`, `E`; one flat orange, dark upright eye, transparent background.

## Shared files: copied, never edited

`src/Tokens.cs`, `src/Ui.cs` and `src/HubLink.cs` are copies from kit.txt. Do not edit them here, not even to silence a warning. If one needs a change, say so: it is made in kit.txt and brought back with `tools/sync.ps1 <path to kit.txt>`.

## Language level: C# 5

The app is built by the compiler that ships with Windows (`csc.exe` from .NET Framework 4). It understands C# 5 only:

- no `?.`, no `$"..."`, no `nameof`, no expression-bodied members (`=>` after a member), no `out var`, no pattern matching, no tuples;
- use `delegate { }` or plain lambdas, `string.Format` or `+`, explicit `out` variables;
- WPF is written in code, no XAML; no NuGet packages and no other dependencies.

## Naming

- In the interface and in text the product is lowercase with the signature: `smena.txt™`. Use `Ui.Brand(...)` for it.
- Files and processes carry the plain name: `Smena.exe`. Never `Smena.txt.exe`.
- Hub app id, topics and commands: lowercase Latin with dots, e.g. `smena.settings`, `settings.set`.

## Text

All interface text is in Russian, plain and short, addressed as «вы». Code comments are in Russian too, like the rest of the code.

## How to verify

Build, then run both checks, every time:

```
powershell -ExecutionPolicy Bypass -File build.ps1
./Smena.exe --selftest                 # must exit with 0
./Smena.exe --demo --shot shot.png     # then open shot.png and look at it
```

- Zero compiler errors is not enough: look at the picture. Compare it with core.txt's `docs/screenshot.png`: same backdrop, glass, type and spacing.
- New behaviour gets a check in `SelfTest.Run()`.
- Hub behaviour is tested against the reference hub on a spare port: `node <kit.txt>/hub/hub.js <port>`, then the app with `SMENA_HUB_PORT=<port>`, `SMENA_DATA=<temp folder>` and `--headless`. Never test against port 42080: that is the user's real core.txt.
- The build copies sources to a short temp folder because `csc.exe` fails on long paths. Keep it that way.

## The user's machine

- Never kill, restart or reconfigure apps that are already running (core.txt, Kapsula, Aura, Discord, Steam). Stop only processes you started, by their PID; no `taskkill /IM`.
- Do not leave windows on the screen: use `--shot` and `--headless` for checks.
- `--demo` and `--selftest` must not touch the real settings folder, the registry or the network.
- No features, numbers or claims in README or interface text that the code does not back.
