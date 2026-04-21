# FixCoreModule

Fixes the `Duplicate type '<>O'` crash caused by [MelonLoader 0.7.2](https://github.com/LavaGang/MelonLoader/issues/1142) on fresh installs.

This affects **all Unity games** using MelonLoader with Il2Cpp — not just one specific game. If MelonLoader crashes on first launch with a duplicate type error, this tool fixes it.

## How to use

1. Close the game
2. Download `FixCoreModule.exe` from the [latest release](https://github.com/V1ndicate1/FixCoreModule/releases/latest)
3. Place it in your game folder and run it
4. Launch the game

The tool automatically finds and patches `UnityEngine.CoreModule.dll` in your MelonLoader assemblies folder. A backup is saved as `.bak`. If the game updates and the error returns, run it again.

## What it does

MelonLoader's Il2CppInterop unstripper sometimes writes duplicate `<>O` type definitions into `UnityEngine.CoreModule.dll` during first-time assembly generation. This causes a crash before any mod can load.

FixCoreModule uses [Mono.Cecil](https://github.com/jbevain/cecil) to read the DLL, remove duplicate type definitions, and rewrite it cleanly. No network calls are made — it only touches the one file in your local game folder.

## Auto-detection

The tool searches for affected games automatically:
1. Checks the current directory (if you placed it in a game folder)
2. Scans all Steam library locations (reads `libraryfolders.vdf` and the registry)
3. Finds the first game with a corrupted `UnityEngine.CoreModule.dll`

If auto-detection fails, it prompts you to enter the game folder path manually.

## Build from source

Requires [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0).

```
dotnet build -c Release
```

For a self-contained exe (no .NET runtime required on target machine):

```
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## License

MIT License. See [LICENSE](LICENSE).

## Third-party

| Library | License | Author |
|---------|---------|--------|
| [Mono.Cecil](https://github.com/jbevain/cecil) | MIT | Jb Evain |
