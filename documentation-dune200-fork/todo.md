# OpenRA Dune 2000 Android Port — TODO Roadmap

This roadmap tracks planned enhancements, polish items, and architectural tasks for the OpenRA Dune 2000 native Android port.

---

## 1. Input & Controls
- [ ] **Touchscreen Fine-Tuning**:
  - Tune 1-finger tap sensitivity and drag deadzones so quick taps don't accidentally register as micro-drags.
  - Refine 2-finger box-selection threshold and rubber-band cancellation.
  - Add optional floating virtual buttons for Stop / Attack-Move for players who prefer not using the command bar.
  - Smooth out pinch-to-zoom step interpolation.
  - **Menu & Scrollpanel Touch Dragging**: Fix touchscreen drag scrolling in UI menus (mission lists, skirmish map chooser, replay list, settings). Currently dragging a finger does not scroll lists without dragging the tiny scrollbar slider or clicking scroll arrows.
- [ ] **Bluetooth Mouse & Keyboard Polish**:
  - Add configurable mouse sensitivity and cursor size slider in Settings.
  - Support middle-click drag pan natively with mouse wheel button.

---

## 2. Stability & Lifecycle
- [ ] **Main Menu Exit Button Freeze**:
  - Fix crash/freeze when tapping the "Quit" button in the main menu.
  - Implement clean Android Activity termination (`FinishAffinity()`, graceful thread join, or `Process.Kill()`).
- [ ] **Low Memory (LOM) Handling**:
  - Implement `OnTrimMemory` and `OnLowMemory` callbacks in `MainActivity` to purge texture caches when system memory is constrained.

---

## 3. Multiplayer & Networking
- [ ] **Version Tag Compatibility for Public Servers**:
  - Provide an option or version string override matching official OpenRA stable release tags (e.g., `release-20231010`) so players can connect to public internet servers without "Version Mismatch" blocks.
  - Add a dedicated mobile server tag in master server broadcasts to identify mobile-friendly lobbies.
- [ ] **In-Game Chat Soft Keyboard Integration**:
  - Improve virtual keyboard IME return key behavior and auto-dismiss on message submit.
  - Auto-scroll chat history when the virtual keyboard is expanded.

---

## 4. UI & Ergonomics
- [ ] **Command Bar Scaling without Texture Tiling**:
  - Allow seamless scaling of the bottom-left command bar and stance bar without repeated/tiled button borders or misaligned background dividers.
- [ ] **Asset Downloader & Content Manager**:
  - Add in-engine download progress bar when downloading free game content directly from official OpenRA web mirrors on first launch.
- [ ] **HUD Customization**:
  - Allow toggling right-hand vs left-hand sidebar layout for left-handed tablet players.

---

## 5. Performance & Power Management
- [ ] **Configurable Frame Rate Cap**:
  - Add 30 FPS / 60 FPS / 120 FPS frame rate limit options in Display Settings.
  - Implement Battery Saver mode (reduced particle effects and 30 FPS cap) for extended on-the-go play.
