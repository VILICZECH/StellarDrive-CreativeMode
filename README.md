# Creative mode

![Creative mode](Mods/thumbnail.jpg)

Need creative mode? Build anything you can imagine without grinding for a single material.

Pick Creative when you create a new world and you get:
- Free building: no materials needed, research and the journal unlocked, and parts from other mods in a Mods tab
- No health or oxygen to worry about
- Tools ready in the quick bar
- Middle-click picks the part you are looking at, even without the build tool
- Shift-click stacks hull blocks in a line. Scroll sets the length up to 16, then click places it
- Ctrl+Z undoes your own last placement, removal or cable. Ctrl+Y restores it. Each player has a separate history
- Refill wood thrusters, generators and distillers, or give them infinite fuel

Flight, double-tap Space or press F1:
- The mode you are in stays, walking or flying. Held WASD stays held when you switch
- Hold Space to ascend and Ctrl to descend
- Speed eases in when you move and eases out when you let go. Flying through a ship does not drag you with it
- Shift+scroll sets speed from ¼× to 128× walking speed. A short label shows the step
- Hold Shift while moving to fly at double that speed
- F uses whatever you look at: levers, chests, seats and other devices
- When a device has taken WASD, Space, Ctrl and Shift, those keys run the device and movement pauses. In flight you stay aloft
- Hold Left Alt to move again. The device stays open and takes the keys back when you let go

MelonLoader mod for Stellar Drive.

## Installation

Install it from the Stellar Mod Manager, or copy `CreativeMod.dll` from the latest release into the game's `Mods` folder. The game needs MelonLoader.

A creative world is marked by a `creative.mode` file in the save folder. A world without that file plays as survival.

## Building from source

You need the .NET SDK (any version that can build `net472` projects) and Stellar Drive with MelonLoader installed. The project references the game's own assemblies, so nothing from the game is included in this repository.

```
dotnet build src/CreativeMode.csproj -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\StellarDrive Demo"
```

The DLL ends up in `src/bin/Release`. Add `-p:CopyToMods=true` to copy it into the game's `Mods` folder.

## How the code is organised

| File | What it does |
| --- | --- |
| `CreativeModeMod.cs` | Mod entry point: scene load, research, journal and the vital-stat passes |
| `CreativeSession.cs` | Writes and reads the `creative.mode` file so a world stays creative after a save |
| `ModePicker.cs` | The Creative / Survival dialog before a new world is created |
| `GameText.cs` | Localized strings taken from the game |
| `CreativePatches.cs` | Tools, free build, health, oxygen, collision damage and the tutorial |
| `CreativeFlight.cs` | Double-tap Space to fly, eased speed, Shift+scroll, and device controls that keep walk or fly |
| `CreativeCatalog.cs` | Unlocks every part and feature and ignores build costs |
| `CreativeResearch.cs` | Unlocks research |
| `CreativeJournal.cs` | Marks journal entries as read |
| `PickLookedAtPart.cs` | Middle-click picks the part you are looking at |
| `BuildLine.cs` | Shift-click stacks hull blocks in a line |
| `BuildUndo.cs` | Ctrl+Z and Ctrl+Y for each player's own placements, removals and cables |
| `BuildUndoNet.cs` | Asks the host to undo that player's own cables |
| `FuelCreative.cs` | Refill or infinite fuel on wood thrusters, generators and distillers |
| `Shared/GameKeys.cs` | Keeps the mod's keys away from the chat, text fields and game menus |

## License

MIT, see [LICENSE](LICENSE).
