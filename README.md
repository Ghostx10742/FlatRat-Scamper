# FlatRat-Scamper

Play Scamper on Windows with a keyboard and mouse using the game's original camera, body, hands, and interactions.

**Donations are optional.** If you enjoy FlatRat and can contribute, I’d really appreciate your support. [Support J_axon on Ko-fi](https://ko-fi.com/j_axon)

## Requirements

- Windows x64 and the Steam version of Scamper.
- FlatRat currently requires the MoonSweepers BepInEx Pack 0.1.0 fork. It includes the IL2CPP support needed to generate Scamper's game bindings. [MoonSweepers-BepInExPack](https://github.com/Andrew1431/MoonSweepers-BepInExPack)

## Install

Close Scamper and copy `FlatRat-Scamper.dll` into `Scamper/BepInEx/plugins`. Replace the previous copy when updating, then launch Scamper. [Download FlatRat-Scamper.dll](dist/FlatRat-Scamper.dll?raw=true)

## Movement and camera

| Control | Action |
| --- | --- |
| Mouse | Look around. Turning left or right turns your body and colliders. |
| F1 | Select keyboard movement. Your hands follow your view. |
| WASD, Shift, Space | Walk, sprint, and jump in keyboard movement mode. |
| F2 | Select physical hand movement. Your hands follow your body's turn and stay level when you look up or down. |
| Hold left or right mouse button in F2 | Move that hand sideways or forward and back. Hold Shift while dragging to raise or lower it, and scroll to adjust reach. Reach down to crouch and plant a hand on the floor. Release the mouse button to look around again. |
| Esc during gameplay | Show the cursor within the game window. Left-click to resume mouse look. |

In F1, the bar under the reticle shows sprint stamina. A full bar gives you 10 seconds. After 2 seconds without Shift, it refills over 3 seconds. You can sprint again with the stamina you've recovered.

## Interactions

- **TVs.** Step close to a lobby, gameplay, or podium TV and press **E** to focus it. Use the mouse to click, drag, or scroll. Text fields and the code keypad accept letters, numbers, and **Backspace**. **Space** is ignored while typing. Press **Esc** to exit.
- **Switches.** Aim at a supported button and press **E** to run its normal action, such as opening a door or starting a round.
- **Ladders.** Aim at a ladder and hold **E** until the three-second progress ring fills to climb to a clear landing at the top.

## Multiplayer

FlatRat uses Scamper's normal body and hand pose synchronization. Multiplayer with stock clients is still being tested.

## Configuration

Adjust mouse sensitivity, eye height, walk and sprint speed, or jump height in `BepInEx/config/jaxon.flatrat.scamper.cfg`. Set `Enabled = false` and restart to return to VR. For loading errors, check `BepInEx/LogOutput.log`.

## Build from source

```powershell
dotnet build .\FlatRat-Scamper.csproj -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Scamper"
```

Build output goes to `bin/Release/net6.0`. The project uses the BepInEx and IL2CPP assemblies from your local game installation.

## Credits

**Contributors** - J_axon

Scamper was developed and published by **Bezbro Games**. [Scamper on Steam](https://store.steampowered.com/app/2481930/Scamper/)

Thanks to the **BepInEx**, **Il2CppInterop**, and **Cpp2IL** teams and **Andrew1431** for the compatibility pack. [MoonSweepers-BepInExPack](https://github.com/Andrew1431/MoonSweepers-BepInExPack)

## License

The original FlatRat-Scamper work is licensed under Creative Commons Attribution-NonCommercial 4.0 International. [Official license text](LICENSE)

## AI disclosure

AI assisted with coding and documentation. I, J_axon, wrote code and led the project's core development, design, and creative direction.
