# FlatRat-Scamper

Play the Windows Steam version of Scamper with a keyboard and mouse. FlatRat uses the game's original player camera, body, hands, and interactions.

**Donations are optional.** If you enjoy FlatRat and can contribute, I’d really appreciate your support. [Support J_axon on Ko-fi](https://ko-fi.com/j_axon)

## Requirements

- Windows x64 and the Steam version of Scamper. FlatRat 0.1.4 targets Steam build `26710795`, which uses Unity `6000.6.0f1`.
- FlatRat currently requires the MoonSweepers BepInEx Pack 0.1.0 fork. Its BepInEx 6 IL2CPP loader includes the Unity 6000.6 and metadata v108 support needed to generate the game bindings FlatRat uses. [MoonSweepers-BepInExPack](https://github.com/Andrew1431/MoonSweepers-BepInExPack)

## Install

Close Scamper and copy `FlatRat-Scamper.dll` into `Scamper/BepInEx/plugins`. Replace the previous copy when updating, then launch Scamper. [Download FlatRat-Scamper.dll](dist/FlatRat-Scamper.dll?raw=true)

To return to VR, set `Enabled = false` in `BepInEx/config/jaxon.flatrat.scamper.cfg` and restart. To uninstall the mod, remove its DLL from the plugins folder while Scamper is closed.

## Movement and camera

| Control | Action |
| --- | --- |
| Mouse | Look freely from the original player eye position. Turning left or right also turns the body and its colliders. |
| F1 | Select keyboard movement, the default at startup. Your hands follow your look direction. |
| WASD, Shift, Space | Walk, sprint, and jump in keyboard movement mode. |
| F2 | Select physical hand movement. Your hands stay aligned with your body when you look up or down. |
| Hold left or right mouse button in F2 | Move the left or right hand with the mouse. Scroll while holding the button to adjust reach. Release the button to resume mouse look. |
| Esc during gameplay | Show the cursor inside the game window. Left-click to resume mouse look. Switching to another app releases the cursor. |

## Interactions

- **TVs.** Aim at a lobby, gameplay, or podium TV and press **E** when prompted. FlatRat centers the view, holds your body still, hides your local rat model, and gives you a cursor within the game window. Left-click buttons, drag sliders, and scroll where the TV supports it. Type letters and numbers or use **Backspace** on the code keypad or in a focused text field. **Space** is ignored while typing. Press **Esc** to leave the TV. **E** also exits when you’re outside a typing screen or field.
- **Physical switches.** Aim at a supported button and press **E** when prompted. FlatRat moves the switch through its normal press and release cycle so its assigned actions run, such as opening doors or starting a round.
- **Ladders.** Aim at a ladder and hold **E** for three seconds. A ring shows your progress. Keep looking at the ladder and holding **E** until you reach a clear landing at the top.

## Multiplayer

FlatRat feeds mouse and keyboard input into Scamper’s existing player rig. The game handles normal synchronization of your body and hand poses. Multiplayer compatibility with players who use the stock game is still being tested.

## Configuration

After the first launch, settings are available in `BepInEx/config/jaxon.flatrat.scamper.cfg`. Close Scamper before editing the file.

| Setting | Default | Purpose |
| --- | --- | --- |
| `Enabled` | `true` | Enable desktop controls at startup. |
| `MouseSensitivity` | `0.13` | Degrees of view rotation per mouse pixel. |
| `EyeHeight` | `1.58` | Standing eye height in metres when the game has no headset pose. |
| `WalkSpeed` | `2.1` | Walk speed in metres per second at normal player scale. |
| `SprintSpeed` | `3.6` | Sprint speed in metres per second at normal player scale. |
| `JumpHeight` | `0.55` | Grounded jump height in metres. |

If desktop controls fail to start, check `BepInEx/LogOutput.log` for the FlatRat version and any loading errors. Include that log, your Scamper build, and the steps that reproduced the issue when reporting a bug.

## Build from source

```powershell
dotnet build .\FlatRat-Scamper.csproj -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Scamper"
```

The project builds with the .NET SDK against the BepInEx core and generated IL2CPP interop assemblies in your local Scamper installation. Build output is in `bin/Release/net6.0`.

## Credits

**Contributors** - J_axon

Scamper was developed and published by **Bezbro Games**. [Scamper on Steam](https://store.steampowered.com/app/2481930/Scamper/)

Thanks to the **BepInEx**, **Il2CppInterop**, and **Cpp2IL** teams and **Andrew1431** for the compatibility pack. The Vertigo 2 Flatscreen Test Mode project informed the desktop camera and hand-control approach. [MoonSweepers-BepInExPack](https://github.com/Andrew1431/MoonSweepers-BepInExPack)

## License

The original FlatRat-Scamper work is licensed under Creative Commons Attribution-NonCommercial 4.0 International. [Official license text](LICENSE)

## AI disclosure

AI assisted with coding and documentation. J_axon provided the concept, feature designs, and creative direction.
