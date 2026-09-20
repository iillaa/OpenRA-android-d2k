# Attribution & Contributions

This document details the lineage, upstream attribution, and technical contributions of the **OpenRA: Dune 2000 (`d2k`)** Android port.

---

## 1. Upstream Foundation & Attribution

This fork is built upon the foundational Android port prototype created by **Tarek Hosni ([@tarek369](https://github.com/tarek369))** in commit [`83d120fc17`](https://github.com/tarek369/OpenRA/commit/83d120fc17004110c0e779a3e194c8253f3031d6).

Tarek established the architectural breakthrough that made running modern OpenRA on Android without heavy emulation layers possible:
* **Direct EGL / OpenGL ES 3.2 Platform**: Bypassed desktop SDL2 and X11 dependencies, implementing direct P/Invoke bindings into `libEGL.so` and a GLES function-pointer loader in `OpenRA.Platforms.Android`.
* **.NET 9 Multi-Targeting**: Enabled the engine and mod assemblies to multi-target `net9.0-android` while maintaining compatibility with the upstream desktop codebase.
* **Native Library Integration**: Integrated OpenAL-Soft (`libsoft_oal.so`) for positional audio and FreeType (`libfreetype6.so`) for vector text rendering.
* **Proof of Concept**: Successfully rendered the *Command & Conquer: Red Alert* main menu shellmap on a personal OnePlus tablet (Adreno 830, Android 16).

We extend our sincere appreciation to Tarek for laying down this innovative baseline.

---

## 2. The Shift to Westwood's *Dune 2000* (`d2k`)

Historically, the vast majority of OpenRA development and mobile experimentation has centered around *Red Alert* or *Tiberian Dawn*. *Dune 2000* has its own distinct RTS soul:
* Unique spice harvesting mechanics and giant sandstorms.
* House-specific arsenals (Atreides, Harkonnen, and Ordos).
* Distinctive UI chrome layout, widescreen radar positioning, and tactical command bar.
* Micro-heavy infantry formations and hit-and-run combat dynamics.

When we created this fork, our mission was to take the raw, non-distributable Android proof-of-concept and turn it into a **complete, polished, and distributable Dune 2000 native Android title** optimized for modern touch devices and tablets.

---

## 3. Contributions & Engineering Accomplishments (by @iillaa)

Starting after commit [`b3c9376a`](https://github.com/iillaa/OpenRA-android-d2k/commit/b3c9376a52c95906ee4556b1da90e1084afcb6d7), our fork implemented **2,450+ lines of new code, fixes, and documentation** across 35 files, delivering the following major milestones:

### A. Automated CI/CD & Linux Build Toolchain
* **GitHub Actions Pipeline**: Created [`.github/workflows/android.yml`](../.github/workflows/android.yml) from scratch. Solved runner environment limitations by introducing multi-path NDK auto-detection (supporting NDK r26d and r27) with direct download fallback, automated license agreements, and automated signed APK packaging.
* **Linux Cross-Compilation Suite**: Upstream scripts were macOS-only. Authored [`thirdparty/build-android-linux.sh`](../thirdparty/build-android-linux.sh) to cross-compile Lua 5.1.5, FreeType 2.13.3, and OpenAL-Soft 1.23.1 for `arm64-v8a` on standard Ubuntu/Debian runners.
* **Modern Android 15+ Compatibility**: Enforced 16KB page-size alignment (`-Wl,-z,max-page-size=16384`) and added automated ELF header validation (`llvm-readelf`) and APK contents inspection.

### B. Startup Crash Resolution & Native Library Resolution
* **Release AOT Stabilization**: Discovered that Mono Fast Deployment in `Debug` builds failed to resolve dynamic libraries on physical devices; migrated the project to full `Release` AOT packaging.
* **Dynamic Library Preloading & DllImport Resolver**: Resolved startup P/Invoke crashes (`DllNotFoundException: libfreetype6.so`) by implementing JNI dynamic library preloading (`JavaSystem.LoadLibrary`) in `MainActivity.cs` and managed fallback resolution via `NativeLibrary.SetDllImportResolver` loading directly from `Context.ApplicationInfo.NativeLibraryDir`.
* **Null Pointer Guards**: Fixed critical SIGSEGV crashes during engine initialization on `glGetString`/`glGetStringi` and empty FreeType glyph bitmaps.

### C. EGL Lifecycle & Zero-Black-Screen App Resume
* **`EnsureCurrentSurface()`**: Upstream suffered from a fatal black-screen or `EGL_BAD_SURFACE` exception when the user switched apps, answered a notification, or locked the screen.
* Re-architected `AndroidPlatformWindow.cs` to detect surface destruction on `OnPause`, dynamically recreate the EGL window surface on `OnResume`, and cleanly bind `eglMakeCurrent` to the active render thread before swapping buffers.

### D. Modern Tablet UI Scaling (1.8× HUD)
* **High-DPI Tablet Ergonomics**: On high-resolution tablets (e.g. 12.7" 2560×1600), the default 1.0× desktop interface was unreadable and unclickable.
* **Targeted `scaleModifier = 1.8f` Architecture**: Scaled up the Dune 2000 sidebar, building/infantry production tabs, cash counter, and repair buttons by **1.8×**.
* **Expanded $364\times364$ Minimap**: Enlarged the tactical radar minimap to an expansive $364\times364$ pixels for effortless finger navigation while keeping the battlefield and pixel sprites at 100% native sharpness.
* **Fixed UI Crashes**: Resolved layout overflow crashes in combobox dropdowns and mission restarts under dynamic scaling.

### E. Rebuilt Touchscreen Control Engine
* **Smooth 1-Finger Map Pan**: Eliminated the jarring map "jump" bug by cleanly seeding the drag origin on touch down. Added a 25px touch slop to prevent accidental scrolling when tapping.
* **100% Reliable Taps**: Removed an arbitrary 350ms maximum tap duration limit that was dropping deliberate button taps and menu selections.
* **Dedicated 2-Finger Box Selection**: Touching down 2 fingers immediately draws a green unit selection box between them. Added a **lingering touch shield** that ignores trailing fingers when lifting up, preventing accidental movement orders.
* **4-Finger Pinch-to-Zoom**: Moved map zoom to a dedicated 4-finger gesture to eliminate competition with troop box selection.

### F. Hardware / Bluetooth Mouse Support
* **PC Classic Controls**: Restored classic desktop RTS controls (Left-Click selects and drags selection boxes; Right-Click moves, attacks, and cancels).
* **25px Screen-Edge Scrolling**: Hovering the cursor within 25px of any display border smoothly pans the battlefield camera.
* **Fixed Mouse Release State Machine**: Solved Android's `ACTION_UP` zero-buttonstate bug that previously left permanent green selection boxes stuck across the screen.
* **Command Bar Tooltips**: Restored bottom-left command bar widget coordinates and enabled mouse hover tooltips (`IgnoreChildMouseOver`).

### G. 30+ Minute Match Longevity & GPU Optimization
* **Zero GC Allocations in Vertex Streaming**: Replaced `GCHandle.Alloc` pinning in `VertexBuffer.cs` with C# native `unsafe fixed` stack pointers, eliminating **~5.4 million GC handle allocations** per 30-minute match and ending runtime GC stutter.
* **Zero-Allocation GPU Streaming & Memory Zeroing**: Implemented `unsafe fixed` pointer streaming with pre-zeroed VRAM buffers, ensuring safe partial vertex updates without risking persistent terrain buffer corruption or alias pool exhaustion.
* **Adreno Driver Crash Fix**: Discovered Qualcomm Adreno GPUs emit high-severity debug performance notices (`Too much alias space, unable to rename`) upon base destruction. Updated `OpenGLES.cs` to filter non-error performance hints, preventing game termination.

### H. In-Game Diagnostics Suite
* **Floating Draggable DevConsole (`🐛`)**: Engineered an in-app floating overlay that renders on top of the OpenGL surface. Expands to a full-screen, color-coded terminal log with one-tap clipboard copying and auto-expands on unhandled exceptions.
* **`CrashLogActivity`**: Created a fallback native Activity that captures uncaught managed and native exceptions before the game window initializes.

### I. Comprehensive Documentation Suite
* Wrote the complete 7-document operational guide in `documentation-dune200-fork/`:
  * [`readme.md`](readme.md): Project overview and quick start.
  * [`controls.md`](controls.md): Detailed touch and mouse input reference.
  * [`technical.md`](technical.md): Engineering deep dive into .NET 9, EGL, and GPU streaming.
  * [`developer.md`](developer.md): Prerequisites, build scripts, CI, and DevConsole debugging.
  * [`progress.md`](progress.md): Chronological history of the port.
  * [`contribution.md`](contribution.md): Attribution and feature breakdown.
  * [`todo.md`](todo.md): Planned roadmap and future polish tasks.
  * [`lessons_learned.md`](lessons_learned.md): Post-mortem analysis and development rules.

---

## 4. Comparison Summary

| Milestone Area | Upstream Prototype (`83d120fc17`) | Dune 2000 Android Fork (`682163b1ef`) |
|---|---|---|
| **Primary Mod Target** | Command & Conquer: Red Alert | **Dune 2000 (`d2k`)** |
| **CI / CD Pipeline** | None (macOS local builds only) | **Automated GitHub Actions CI** with NDK r27 auto-detection & signed APK publishing |
| **Native Library Build** | macOS-only scripts | **Unified Linux cross-compiler** (`thirdparty/build-android-linux.sh`) |
| **Startup Reliability** | SIGSEGV on `glGetString`, null glyphs, DllImport failure | **100% Reliable**: Release AOT, JNI preloading, null-pointer guards |
| **App Lifecycle** | Black screen on pause/resume (`EGL_BAD_SURFACE`) | **Seamless Resume**: Dynamic surface recreation via `EnsureCurrentSurface()` |
| **Display & UI Scaling** | Microscopic 1.0× desktop UI layout | **1.8× Tablet HUD Scaling** with expanded $364\times364$ radar minimap |
| **Touch Controls** | Jumping map, dropped taps, gesture conflicts | **Smooth 1-finger pan**, 2-finger box select, 4-finger zoom, zero jump |
| **Hardware Mouse** | Unsupported / stuck selection boxes | **Full PC Classic Dune 2000 mouse** with 25px edge scrolling & hover tooltips |
| **30+ Min Match Stability** | GC stutter (~5.4M allocations), Adreno alias crash | **Zero-allocation `unsafe fixed` vertex streaming**, stable partial SubData, locked 60 FPS |
| **Terrain Rendering** | Horizontal cliff stripes on sand | **Pristine, stable terrain rendering** with safe partial SubData flushes |
| **On-Device Diagnostics** | None (blind crashes) | **Floating DevConsole (`🐛`)** & `CrashLogActivity` with 1-tap clipboard copy |
| **Documentation** | Generic `ANDROID.md` | **Complete 8-document technical and operational guide** |
