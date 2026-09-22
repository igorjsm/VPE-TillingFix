# VPE - Tilling Uses Plant Work Speed

A RimWorld 1.6 mod. In [Vanilla Plants Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=2134308522),
tilling soil is a Growing job that uses the Plants skill, but its work speed still comes from the colonist's
**Construction Speed**. This mod makes tilling use **Plant Work Speed** instead.

- Only tilling is affected. Walls, floors and every other construction job are unchanged.
- No construction stuff factor is applied to tilling.
- Compatible with Vanilla Skills Expanded: its Floor Speed stat no longer applies to tilling.
- Low-skill growers will till slower and skilled growers faster than before.

Requires [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) and Vanilla Plants Expanded.
Load after Vanilla Plants Expanded. Safe to add to or remove from existing saves.

## How it works

Tilled soil (`VCE_TilledSoil`) is a buildable terrain, so the work runs through vanilla's
`JobDriver_ConstructFinishFrame`. Vanilla Expanded Framework already moves its work type, skill and XP to
Plants, but the per-tick work still reads `StatDefOf.ConstructionSpeed`. A Harmony transpiler on that tick
action swaps the stat for `PlantWorkSpeed` (and drops the stuff factor), only when the frame being built is
tilled soil. It runs with `Priority.First` so it still works when Vanilla Skills Expanded replaces the same
stat read.

## Building

Place the repository in `RimWorld/Mods/VPE-TillingFix` (the project references the game's DLLs through a
relative path) and run:

```bash
cd Source
dotnet build -c Release
```

The DLL is written to `1.6/Assemblies/VPETillingFix.dll`.

## License

[MIT](LICENSE)
