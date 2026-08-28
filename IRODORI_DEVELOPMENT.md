# Irodori branch development notes

This document records the development baseline and repository policy for the Irodori integration fork.

## Baseline

- Upstream repository: `whatismyname0/RimTalkTTSModule`
- Development branch: `Irodori`
- RimTalk TTS upstream base commit: `37ac73b50459acacefee9d875d02d284d208e1d1`
- Irodori v5.9 functional baseline commit: `42b9e70da7aa5c42c50246edec68601b9525867c`

The v5.9 baseline contains the Irodori provider, Fast Path, BIO voice preview, editable preview sample text, Voice Lab, voice display-name editing, server/profile deletion, and repair of stale per-save pawn voice assignments.

Commits after the v5.9 baseline should modify the source directly. The legacy `apply_v5_x` patch-script workflow is no longer the canonical development workflow.

## Branch policy

- `main`: keep suitable for following the upstream fork.
- `Irodori`: active Irodori integration development.
- Do not commit locally generated `RimTalk.TTS.dll` or `RimTalk.TTS.pdb` files.

## Build

The project no longer stores a developer-specific absolute path to `RimTalk.dll`.

From the repository root:

```powershell
.\build.cmd -RimTalkDir "F:\SteamLibrary\steamapps\common\RimWorld\Mods\3551203752"
```

`-RimTalkDir` should normally point to the RimTalk mod root. The build helper also accepts a path to the `1.6` directory or directly to a directory containing `RimTalk.dll`.

Alternatively, define `RIMTALK_DIR` and run:

```powershell
$env:RIMTALK_DIR = "F:\SteamLibrary\steamapps\common\RimWorld\Mods\3551203752"
.\build.cmd
```

The build script resolves the exact `RimTalk.dll` path and passes it to MSBuild as `RimTalkAssemblyPath`; it does **not** rewrite `Source/RimTalk.TTS.csproj`.

Generated output:

```text
1.6/Assemblies/RimTalk.TTS.dll
```

## Runtime installation

Building this repository does not automatically replace the DLL in another installed RimTalk TTS addon folder. If the development repository is not itself the enabled RimWorld mod folder, copy the generated DLL to the enabled addon's `1.6/Assemblies` directory before testing.
