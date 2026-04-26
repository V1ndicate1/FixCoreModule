# FixCoreModule

Fixes the `Duplicate type '<>O'` crash caused by [MelonLoader 0.7.2](https://github.com/LavaGang/MelonLoader/issues/1142) on fresh installs.

This affects **all Unity games** using MelonLoader with Il2Cpp — not just one specific game. If MelonLoader crashes on first launch with a duplicate type error, this tool fixes it.

## How to use

1. Close all affected games
2. Download `FixCoreModule-v*.zip` from the [latest release](https://github.com/V1ndicate1/FixCoreModule/releases/latest) and extract it
3. Run `FixCoreModule.exe`
4. Launch your game

The tool automatically scans your Steam libraries and finds **all** affected games. It asks for confirmation before fixing each one. If auto-detection fails, a folder picker opens so you can browse to your game folder.

A backup of each original DLL is saved as `.bak`. If a game updates and the error returns, run it again.

## What it does

MelonLoader's Il2CppInterop unstripper sometimes writes duplicate `<>O` type definitions into `UnityEngine.CoreModule.dll` during first-time assembly generation. This causes a crash before any mod can load.

FixCoreModule uses [Mono.Cecil](https://github.com/jbevain/cecil) to read the DLL, remove duplicate type definitions, and rewrite it cleanly. No network calls are made — it only touches files in your local game folders.

## Auto-detection

The tool searches for affected games automatically:
1. Checks the current directory (if you placed it in a game folder)
2. Scans all Steam library locations (reads `libraryfolders.vdf` and the registry)
3. Lists **all** games with a corrupted `UnityEngine.CoreModule.dll`
4. Asks `Fix this game? [Y/n]` before touching each one

If auto-detection finds nothing, a Windows folder picker dialog opens so you can browse to your game folder. If that's cancelled, you can type the path manually.

## Build from source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet build -c Release
```

For a self-contained exe (no .NET runtime required on target machine):

```
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Credits

Developed with AI assistance (Claude)

## License

MIT License. See [LICENSE](LICENSE).

## Third-party

| Library | License | Author |
|---------|---------|--------|
| [Mono.Cecil](https://github.com/jbevain/cecil) | MIT | Jb Evain |

See [THIRD-PARTY-LICENSES.txt](THIRD-PARTY-LICENSES.txt) for full license texts.
