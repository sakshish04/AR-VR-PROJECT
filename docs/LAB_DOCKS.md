# Lab docks — a designated place for every item

**Status: built and logic-tested in Edit Mode (2026-10-23). Not yet played through in the Meta XR Simulator or on a headset.**

Every one of the nine items has one dock, in a row on the research platform (z = 4.9, x = -3.6 ... 3.6 in 0.9 m steps, left to right as you face the room):

| # | Dock | Item | Colour |
|---|---|---|---|
| 1 | RED BIN | red cube | red |
| 2 | BLUE BIN | blue sphere | blue |
| 3 | GREEN BIN | green capsule | green |
| 4 | REACTOR DOCK | energy core | cyan |
| 5 | SERVER RACK | data module | violet |
| 6 | CHARGING STATION | power cell | yellow |
| 7 | ASSEMBLY BAY | robot component | orange |
| 8 | SPECIMEN STORAGE | research canister | mint |
| 9 | ANALYSIS POD | crystal specimen | magenta |

## How it plays

1. Pick an item up any way you like: tracked hand, controller, distance grab, or say **"grab this object"** (it comes into your hands and follows you).
2. Take it to its dock and put it down: release the grab, or say **"drop this object"** over the pad. Or select it, look at its pad and say **"move there"**.
3. When the item is over its own pad, not held, and still for 0.35 s, it **snaps onto the pad** and the dock turns **green** ("DOCKED").
4. On the **wrong** dock the dock flashes **red** and the HUD says where the item belongs.
5. A dock **pulses** while its item is looked at, selected or held ("this is where it goes"). The HUD line says "<item> goes to the <dock> (dock n of 9)".
6. Pick a docked item up again and its dock is free again. **"reset"** sends everything back. When all nine are docked: **LAB COMPLETE** with the time taken.

Progress is shown top-centre in the desktop view and in the last row of the headset panel.

## Files

- `Assets/Scripts/Lab/LabStation.cs` — one dock (data, colours, label, visuals).
- `Assets/Scripts/Lab/LabTaskManager.cs` — the rules, snapping, messages and progress.
- `Assets/Scripts/Lab/LabPlacementRules.cs` — the Unity-free placement rules (unit tested).
- `Assets/Editor/VoiceVRLabStationsBuilder.cs` — **Group05 > VoiceVR > Build Lab Docks** rebuilds the docks (safe to re-run). Materials are in `Assets/Materials/Lab`.

The lab task does not write any telemetry and never executes a voice command.

## Verified so far / not yet

Verified (Edit Mode, positions restored afterwards): an item above its own dock docks and snaps to the pad; an item on a wrong dock is refused with a message; an item on its own dock after that docks; carrying a docked item away frees the dock; all nine docked gives LAB COMPLETE. Standalone tests 384 passed; compile check 35 files, 3 of 3 configurations.

Not yet verified: playing it with hands, controllers or the microphone in the simulator; the timing of the snap; the XR panel row on screen.
