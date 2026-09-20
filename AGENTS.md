# Agent Guidelines & Investigation Rules for OpenRA Dune 2000 Android Port

## 1. Core Codebase Trust & Investigation Hierarchy (Token & Time Efficiency)

Upstream OpenRA is an established, battle-tested, 15-year RTS engine with hundreds of contributors. Its core simulation, game logic, maps, and asset pipeline are mature, well-structured, and stable.

**DO NOT deep-dive into upstream OpenRA core engine code, YAML rules, or raw map/asset binaries first.** Doing so burns excessive tokens, wastes developer time, and pursues visual red herrings.

Whenever a bug, visual glitch, performance drop, or crash is reported, you **MUST** audit strictly in this gradual order:

---

### Step 1: Recent Working Branch Commits (Immediate Culprits)
- Inspect the most recent commits and uncommitted diffs:
  ```bash
  git status
  git log -p -n 5
  ```
- **Rationale**: 90% of regressions (such as the recent buffer orphaning terrain glitch) were introduced in the latest commits attempting "optimizations", UI tweaks, or platform adjustments. Always check what changed recently before suspecting older code.

---

### Step 2: Fork Modifications (User Changes & Features)
- Inspect custom features added specifically for this Dune 2000 Android fork:
  - HUD / Command Bar scaling (`WidgetUtils.DrawPanel`, `ingame-player.yaml`)
  - Touch input & gesture recognition (`OpenRA.Platforms.Android/AndroidInputHandler.cs`)
  - Mouse edge scrolling and button release state machine
  - Asset auto-importer (`AndroidAssetImporter.cs`)
  - Viewport margin and boundary clamping (`Viewport.cs`)

---

### Step 3: Android Platform Layer (Tarek's Android Native Port)
- If the issue is low-level (rendering, audio, freeze on resume, native crash):
  - Inspect `OpenRA.Platforms.Android/` (`VertexBuffer.cs`, `OpenGLES.cs`, `AndroidGraphicsContext.cs`, `AndroidPlatform.cs`)
  - Inspect `OpenRA.Android/` (`MainActivity.cs`, Activity lifecycle, permissions)
  - Inspect cross-compiled native libraries (`thirdparty/`)
  - Compare directly with `OpenRA.Platforms.Default/` to see how upstream desktop implements the same platform contract.

---

### Step 4: Upstream OpenRA Engine Core & Assets (Strictly Last Resort)
- **Only** inspect `OpenRA.Game/`, `OpenRA.Mods.*`, map binaries (`map.bin`), or raw game assets (`BLOXBASE.R16`, etc.) if:
  1. An explicit comparative audit against desktop platform behavior is needed.
  2. All layers in Steps 1 through 3 have been thoroughly audited and proven innocent.
- **Never** assume official campaign maps, tilesets, or audio/video assets are corrupt without first ruling out the platform rendering and input layers.

---

## 2. Commit & Repository Hygiene
- Maintain a clean and focused commit history.
- Ensure changes compile cleanly and pass GitHub Actions CI for both desktop and Android targets.
- Always consult and update [`documentation-dune200-fork/lessons_learned.md`](documentation-dune200-fork/lessons_learned.md) whenever an architectural pitfall or regression is resolved.
