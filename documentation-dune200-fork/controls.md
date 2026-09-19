# Controls & Input Guide

This document provides a comprehensive overview of the input system for the **OpenRA: Dune 2000 (`d2k`)** Android port.

The input architecture is bifurcated at the Android View boundary into two independent pipelines:
1. **Touchscreen Pipeline**: Optimized for natural tablet and phone ergonomics.
2. **Hardware / Bluetooth Mouse Pipeline**: Replicates the native PC real-time strategy experience.

Both pipelines run concurrently—you can pan the map with your left finger while aiming or selecting units with your mouse, or switch between them dynamically.

---

## 1. Touchscreen Controls

| Gesture | Action | Description |
|---|---|---|
| **1-Finger Tap** | Select / Order / UI Click | Taps on friendly units to select; taps on enemies/ground to move or attack in Classic mode; taps HUD buttons and radar. |
| **1-Finger Drag / Swipe** | Smooth Map Pan | Dragging across the battlefield scrolls the camera smoothly with 1:1 direct response (starts with zero jump). |
| **2-Finger Drag / Frame** | Unit Box Selection | Touching down 2 fingers immediately creates a green selection box between them. Drag or expand the fingers across your units and lift to select all troops inside. |
| **4-Finger Pinch / Spread** | Zoom In / Out | Using two hands (4 fingers), pinch inward to zoom out for a tactical overview, or spread outward to zoom in. |
| **Radar Tap** | Order / Move | In Classic mode with a unit selected, tapping the radar orders the unit to that location. |

### Misclick Prevention & Viewport Safety
* **Touch Slop ($25\text{ px}$)**: Small natural finger flatting on high-DPI screens ($>230\text{ PPI}$) will never trigger accidental map panning.
* **Instant Button Response**: Releasing a finger from a menu item or production button fires instantly on the first tap without dropping clicks.
* **Lingering Touch Shield**: When lifting fingers from a 2-finger box selection, any trailing finger touching the glass for a split-second is automatically ignored until all fingers lift, preventing accidental move orders.
* **Viewport Edge Clamping**: The camera is clamped so the desert map always fills **88%–90% of the screen**, eliminating the old issue where panning to the border resulted in 50% of the display being empty black void.

---

## 2. Bluetooth / Hardware Mouse Controls

The mouse pipeline connects directly to OpenRA's desktop event loop without touch translation delays:

| Mouse Action | In-Game Function |
|---|---|
| **Left Click** | Select unit / Click UI buttons & production tabs / Issue orders on radar when unit selected. |
| **Left Click + Drag** | Draw green unit box selection around multiple troops. |
| **Right Click** | Issue Move, Attack, or Harvest orders / Cancel current placement or target in Classic mode. |
| **Right Click on Radar** | **Instant Camera Jump**: Instantly centers the viewport on that minimap position. |
| **Middle Click + Drag** | Tiberian Sun / Standard camera drag. |
| **Scroll Wheel** | Smooth zoom in / zoom out. |
| **Screen Edge Hover (25px)** | **Edge Scrolling**: Moving the mouse cursor within 25px of any display border smoothly pans the camera across the desert map. |

> [!NOTE]
> * **Hot-Plug Resilience**: Connecting or disconnecting a Bluetooth mouse while in-game does not restart the Activity or crash the engine.
> * **No Stuck Selection Boxes**: Mouse clicks and hover motions are cleanly tracked with deterministic `Down` and `Up` state machines, eliminating the legacy bug where dragging a mouse left a permanent green box across the screen.

---

## 3. Unit Stances & Combat Behaviors

On the bottom stance bar (or using hotkeys), you can configure the automated engagement behavior of any selected units:

| Stance Icon | Name | Tactical Behavior | Best Used For |
|:---:|---|---|---|
| 🎯 | **Attack Anything** | **Aggressive Engagement**: Units actively scan their sight radius. When an enemy appears, they immediately break formation, pursue the enemy, and destroy them. | **Base Defense & Patrols**: Essential for perimeter tanks and defensive infantry so they don't sit idle when enemies approach. |
| 🛡️ | **Defend** | **Stand Ground / Do Not Chase**: Units will only fire at enemies that step inside their exact firing range. They will **never** leave their position to chase. *(Warning: Enemies with longer range like Rocket Infantry or Siege Tanks can fire at them without retaliation).* | Holding choke points, defending spice refineries, or guarding buildings. |
| 🔄 | **Return Fire** | **Retaliation Only**: Units will hold fire and will only shoot back if they are directly attacked first. | Guarding convoys or harvesters without picking fights. |
| 🛑 | **Hold Fire** | **Strict Stealth**: Units will **never** shoot under any circumstances, even when being attacked and destroyed. | Infiltrators, saboteurs, or covert scouts. |

---

## 4. Command Bar Reference

Located at the bottom-left corner of the screen:

* ⚔️ **Attack Move (`A`)**: Troops march toward the destination and immediately halt to attack any enemy encountered along the path.
* 🚀 **Force Move**: Forces units to move to a location, even running over enemy infantry with heavy tanks.
* 🎯 **Force Attack (`Ctrl`)**: Orders units to fire at neutral terrain or friendly targets (useful for uncovering worms or firing into shroud).
* 🛡️👤 **Guard (`G`)**: Escorts and protects another friendly unit (e.g. assigning combat tanks to guard a Harvester).
* 🏗️ **Deploy (`Enter`)**: Deploys MCVs into Construction Yards or unpacks mobile artillery.
* 💥 **Scatter (`X`)**: Orders infantry and vehicles to scatter in all directions (critical for dodging Sandworms or missile strikes).
* 🛑 **Stop (`S`)**: Immediately cancels current movement and firing orders.
* 📋 **Queue Orders (`Shift`)**: Allows chaining multiple waypoints or sequential production commands.
