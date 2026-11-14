# Sensitivity Converter

Work out what to set a game's mouse sensitivity to, so that every game feels the same.

You tell it your mouse DPI and how far you want to sweep for a full 360. Then you measure each
game once, by spinning in a circle while the program counts your mouse movement. From then on it
tells you the exact sensitivity to type into that game.

The point of measuring it yourself: it works for *any* game, including the small and obscure ones
that the online converters have never heard of.

Nothing is injected into any game. The program only listens to your mouse, so there is nothing for
an anti-cheat to object to.

## How it works

Any game that scales mouse input in a straight line does this internally:

```
degrees turned = mouse counts x in-game sens x C
```

`C` is a constant baked into that game. Everything else follows from it:

```
counts per 360 = 360 / (sens x C)
cm/360         = counts per 360 / DPI x 2.54
```

**Worked example.** You want 30 cm/360 at 1600 DPI, which is `30 / 2.54 x 1600 = 18,898` counts
per 360. In some game you set sens to `1.0` and your measured 360 took 9,449 counts, so
`C = 360 / 9449 = 0.0381`. The sens you need is `360 / (0.0381 x 18,898) = 0.5`.

Because `C` belongs to the game alone, changing your DPI or your target distance never means
measuring a game again.

## Using it

**First run.** Enter your mouse DPI and the distance you want a full 360 to take. 30 cm is a
common starting point; lower is more sensitive.

Turn mouse acceleration off first - in Windows (Mouse settings, untick *Enhance pointer
precision*) and in every game. With acceleration on, the same sweep turns a different amount
every time and no converter can be accurate.

**Adding a game.**

1. Click *Add a game* and point at the game's exe.
2. In the game, set the mouse sensitivity to the value shown (1.0 by default) and stand still.
3. Line up a landmark dead centre on screen.
4. Press the hotkey (F8), sweep exactly one full circle back to that landmark, press F8 again.
   You will hear a tone at each end, so you do not need to see the window.
5. Do that five times, then press *Save*.

Overshooting and correcting is fine - movement left and right cancels out, so only the net turn
counts.

**Reading the result.** Select a game and it shows the sensitivity to set. *Copy number* puts it
on the clipboard. Change your DPI or your target later and every game's number updates at once.

### When a measurement looks wrong

- A spin more than 5% from the middle of the pack is flagged in red. Untick it and record another.
- If the kept spins still vary by more than 10%, the program says so. Usually that means either
  sloppy spins, or acceleration still switched on somewhere.
- A spin with a lot of back-and-forth, or a very tilted sweep, gets a note of its own.

### What it does not handle

- **Separate scoped or ADS sensitivity.** A game gets one measurement, for hipfire. If you want
  a scoped number too, add the game twice under different names.
- **Non-linear sensitivity.** A handful of games do not scale in a straight line. They show up as
  spins that refuse to agree with each other, and they cannot be converted this way.
- **Writing the number into the game for you.** That is planned, not built - see below.

## Where your data lives

Two plain json files, next to the exe (or in `%LOCALAPPDATA%\SensConverter` if that folder is
read-only). *Open data folder* takes you there.

`settings.json` is your profile:

```json
{
  "schemaVersion": 1,
  "dpi": 1600,
  "targetCm360": 30,
  "calibrationHotkey": "F8",
  "spinsPerCalibration": 5,
  "configured": true
}
```

`games.json` is one entry per measured game:

```json
{
  "schemaVersion": 1,
  "games": [
    {
      "id": "6f1c...",
      "name": "Niche Shooter",
      "exePath": "C:\\Games\\niche\\niche.exe",
      "exeHash": "9b74c9897bac770ffc029102a200c5de...",
      "sensUsedDuringCalibration": 1,
      "counts360": 9449,
      "coefficient": 0.038099,
      "spins": [9400, 9450, 9497],
      "calibratedOn": "2026-11-14T15:39:00+00:00",
      "notes": "hipfire only"
    }
  ]
}
```

Both are safe to read, edit or back up. Anything derivable is recalculated on load, so a
hand-written entry with nothing but `spins` and `sensUsedDuringCalibration` still works. A file
that cannot be parsed is renamed to `games.bad-<date>.json` rather than thrown away, and the
program tells you it did that.

Games are remembered by the exe's SHA-256 as well as its path, so a game that moves is recognised
as the same game. If an exe changes because the game updated, you are asked whether to keep the
existing measurement or redo it - updates almost never change how a game scales mouse input.

## Running it

Needs the .NET 10 runtime (or SDK) on Windows.

```
dotnet build                                      # build everything
dotnet run --project src/SensConverter.App        # run it
dotnet run --project tests/SensConverter.Tests    # run the tests
```

To make a single self-contained exe:

```
dotnet publish src/SensConverter.App -c Release
```

Command line: `SensConverter.exe [--data <folder>]` keeps the json files somewhere else - handy
for testing without touching your real settings.

### Layout

- `src/SensConverter.Core` - the maths, the saved-file handling and the spin logic. No UI, so it
  can be tested on its own.
- `src/SensConverter.App` - the windows. `Input/GlobalInput.cs` is the only part that talks to
  Windows directly: a hidden window that receives raw mouse movement and one global hotkey.
- `tests/SensConverter.Tests` - a plain console test suite, no packages to download. It prints
  each test and exits non-zero if any fail.

## Planned

- **Writing the sensitivity into the game's own config file**, so you do not have to type it.
- **Automatic measurement** for single-player games: the program moves the mouse itself and finds
  the point where the view has come full circle. Deliberately left until last, because synthetic
  mouse movement is exactly what anti-cheats are built to notice.

Currently version 0.1.0.

## Licence

GPLv3 - see [LICENSE](LICENSE). Use it, change it and share it freely; anything you share onward
has to stay under the same licence.
