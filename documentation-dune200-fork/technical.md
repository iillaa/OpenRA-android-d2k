# Technical Architecture & Engineering Deep-Dive

This document details the internal architecture, graphics pipeline, memory management, and platform integration of the **OpenRA: Dune 2000 (`d2k`)** native Android port.

---

## 1. High-Level Architecture

OpenRA on Android executes as a native Android application powered by **.NET 9 Android Workload (`net9.0-android`)**. The architecture completely bypasses SDL2 and desktop emulation wrappers:

```
┌────────────────────────────────────────────────────────────────────────┐
│                   OpenRA.Android (net9.0-android)                      │
│        MainActivity.cs  •  OpenRASurfaceView  •  DevConsole            │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Direct P/Invoke & Delegates
┌───────────────────────────────────┴────────────────────────────────────┐
│                    OpenRA.Platforms.Android                            │
│  Egl.cs • OpenGLES.cs • VertexBuffer.cs • AndroidInput.cs • FreeType   │
└───────────────────┬───────────────────────────────┬────────────────────┘
                    │                               │
┌───────────────────┴──────────┐   ┌────────────────┴────────────────────┐
│       Core Game Logic        │   │        Native C/C++ Libraries       │
│  OpenRA.Game • OpenRA.Mods   │   │  libfreetype6.so • libsoft_oal.so   │
│  (Compiled-in Assemblies)    │   │  liblua51.so     (arm64-v8a)        │
└──────────────────────────────┘   └─────────────────────────────────────┘
```

### Key Assembly Responsibilities:
1. **`OpenRA.Android`**: Host Android Activity, lifecycle manager, `OpenRASurfaceView` capturing hardware touch and mouse inputs, and the in-game debug overlay (`DevConsole`).
2. **`OpenRA.Platforms.Android`**: Platform implementation satisfying `IPlatform`, `IPlatformWindow`, and `IGraphicsContext`. Interacts with `libEGL.so`, `libGLESv3.so`, and native audio/font libraries.
3. **`OpenRA.Game` & `OpenRA.Mods.*`**: Core game logic and mod rules. Multi-targeted and compiled directly into the application package without dynamic reflection loading.

---

## 2. Graphics Pipeline: Raw EGL & OpenGL ES 3.2

Unlike desktop OpenRA which relies on SDL2, the Android platform binds directly to `libEGL.so` and `libGLESv3.so`.

### EGL Context & Surface Lifecycle
* **EGL Display & Context**: Initialized with `EGL_RENDERABLE_TYPE = EGL_OPENGL_ES3_BIT_KHR`, configuring an RGBA8888 24-bit depth/stencil buffer.
* **SurfaceView Integration**: When `SurfaceCreated` or `SurfaceChanged` fires, `ANativeWindow` is wrapped in an EGL window surface via `eglCreateWindowSurface`.
* **Zero-Black-Screen Resume**: When the game is sent to the background (`OnPause`), the EGL surface is torn down. On `OnResume`, `AndroidPlatformWindow.EnsureCurrentSurface()` dynamically recreates the surface and calls `eglMakeCurrent` on the game loop thread before issuing `eglSwapBuffers`, preventing `EGL_BAD_SURFACE` exceptions.

### OpenGL Debug Message Handling
On desktop drivers, `GL_DEBUG_SEVERITY_HIGH` messages are often fatal. However, mobile drivers (such as **Qualcomm Adreno 650**) utilize high-severity debug callbacks to emit internal driver notifications (e.g. `Performance - Too much alias space, unable to rename`).
* In [`OpenGLES.cs`](file:///data/data/com.termux/files/home/chat/dune/OpenRA.Platforms.Android/OpenGLES.cs), `DebugMessageHandler` explicitly checks `type == GL_DEBUG_TYPE_ERROR`.
* Non-error driver performance hints are logged to the console without throwing fatal exceptions, preventing random crashes during heavy combat.

---

## 3. GPU Vertex Streaming & Memory Management

In a typical 30+ minute match, OpenRA submits tens of thousands of quad batches to the GPU. Two critical bottlenecks were addressed:

### A. Zero-Allocation Streaming via `unsafe fixed`
* **Legacy Problem**: [`VertexBuffer.cs`](file:///data/data/com.termux/files/home/chat/dune/OpenRA.Platforms.Android/VertexBuffer.cs) called `GCHandle.Alloc(data, GCHandleType.Pinned)` and `ptr.Free()` on every single quad batch. At 60 FPS with 50 batches per frame, this resulted in **3,000 GC handle allocations/sec** (~5.4 million per match), inducing heavy SGen GC pauses and memory fragmentation.
* **Solution**: Switched to C# native `unsafe fixed (T* ptr = &data[offset])` stack pinning. Stack pinning incurs **zero GC allocations**, eliminating runtime handle table churn completely.

### B. Vertex Buffer Stability & Memory Zeroing
* **Architecture**: OpenRA uses `VertexBuffer` for two very different workloads:
  1. High-frequency transient batching (`tempVertexBuffer` in `Renderer.cs`), flushed multiple times per frame.
  2. Persistent world-space layers (`TerrainSpriteLayer` in `OpenRA.Game/Graphics/TerrainSpriteLayer.cs`), created once per map and updated incrementally row-by-row (`start = vertexRowStride * row`).
* **The Pitfall of Buffer Orphaning**: Attempting OpenGL buffer orphaning (`glBufferData(..., IntPtr.Zero)`) in `SetData` when `start == 0` causes catastrophic map corruption: flushing row 0 wipes the entire map geometry from VRAM, leaving non-dirty rows populated with recycled GPU memory garbage.
* **Solution**: `SetData` uses direct `glBufferSubData` to safely patch only the affected rows without altering persistent buffer storage, while `VertexBuffer(int size)` pre-zeroes VRAM at allocation using zero-overhead unsafe stack pointers.

---

## 4. Native C/C++ Libraries & 16KB Page Alignment

Android 15+ mandates that native libraries support **16KB ELF page sizes**. All native shared libraries are built with NDK r27 and `-Wl,-z,max-page-size=16384`:

| Library | Function | Interop Mechanism |
|---|---|---|
| `libfreetype6.so` | Font rasterization & vector glyphs | `NativeLibrary.SetDllImportResolver` via `Java.Lang.JavaSystem.LoadLibrary("freetype6")` |
| `libsoft_oal.so` | 3D positional audio & ambient playback | OpenAL-CS P/Invoke with OpenSL ES audio backend |
| `liblua51.so` | Campaign script execution | KeraLua / C# P/Invoke bindings |

### FreeType Runtime Resolver
On Android, `NativeLibrary.TryLoad("freetype6")` fails if the OS loader expects `libfreetype6.so` inside the app's native library directory. In `AndroidPlatform.cs`:
1. `Java.Lang.JavaSystem.LoadLibrary("freetype6")` pre-loads the library into the process address space.
2. An explicit `DllImportResolver` intercepts P/Invoke calls to `freetype6` and redirects them to the absolute path in `ApplicationInfo.NativeLibraryDir`.

---

## 5. Dual Input Engine Architecture

In [`MainActivity.cs`](file:///data/data/com.termux/files/home/chat/dune/OpenRA.Android/MainActivity.cs), incoming events are categorized before entering the lock-free input queue:

```
[Android View MotionEvent]
          │
          ▼
   IsMouseEvent(e)? ────────► YES ──► EnqueueMouseMotion() ──► HandleMouse()
          │
          ▼ NO
   EnqueueMotion()  ──────────────────────────────────────────► HandleTouch()
```

### State Tracking Highlights:
* **Persistent Mouse Tracking (`lastEventWasMouse`)**: Android dispatches mouse `ACTION_UP` with `ButtonState == 0`. By maintaining mouse tracking across events, mouse releases are never misidentified as touch gestures.
* **Viewport LastMousePos Synchronization**: Before 1-finger panning begins, `AndroidInput` injects a neutral cursor move to `primaryDownPos`. This aligns `Viewport.LastMousePos` with the touch location, completely eliminating the legacy 1000px camera jump on swipe start.
* **Multi-Touch Segmentation**:
  * **PointerCount == 1**: Single-finger tap vs. 1:1 pan.
  * **PointerCount == 2**: 100% dedicated to unit box selection.
  * **PointerCount >= 4**: Centroid-based pinch-to-zoom.

---

## 6. Dynamic Tablet HUD & Resolution Engine

On a 12.7" 2560×1600 tablet display, rendering the UI at standard scale makes production buttons and radar thumbnails too small to touch comfortably, while setting global `UIScale = 2` blurs the battlefield map.

* **Split Scaling Architecture**:
  * Battlefield map renders at **100% native 2560×1600 resolution** for pixel-sharp terrain and unit sprites.
  * Interactive panels (sidebar, production palette, radar, command bar, and stance selector) are uniformly scaled up by **1.8×** in `mods/d2k/chrome/ingame-player.yaml`.
* **Dynamic Grid Recalculation**:
  * `ProductionPaletteWidget.cs` scales `IconSize` ($104\times86$) and `IconMargin` ($4\times0$).
  * `SupportPowersWidget.cs` scales support power icons ($108\times86$).
  * `COMMAND_BAR_BACKGROUND` ($812\times77$), `COMMAND_BAR` ($480\times70$, buttons $58\times70$, icons $44\times44$), and `STANCE_BAR` ($220\times44$) are scaled $1.8\times$ with `IgnoreMouseOver: true` for hover tooltips.
  * `RadarWidget.cs` renders at $364\times364$ pixels with full touch and mouse navigation.
* **Standardized 100% UI Scale Default**:
  * `MainActivity.ComputeDefaultUIScale()` returns `1.0f` (100%), avoiding double-scaling while automatically migrating any legacy `UIScale: 2.x` settings.

---

## 7. Viewport Edge Clamping & Scroll Bounds

In desktop OpenRA, `CenterLocation` is clamped directly to `mapBounds`. Because the *center* of the screen can reach the map boundary, exactly **50% of the screen** hangs over into the empty black void when scrolling to any border.

In [`Viewport.cs`](file:///data/data/com.termux/files/home/chat/dune/OpenRA.Game/Graphics/Viewport.cs), `GetEffectiveScrollBounds()` dynamically computes scroll limits based on current viewport dimensions and asymmetric HUD layout:
* **Asymmetric Margins**:
  * **Top, Bottom, and Left Margins ($12\%$)**: Keeps the desert battlefield filling $\ge 88\%$ of the screen without unnecessary black void.
  * **Right Margin ($35\%$)**: Accommodates the right sidebar (radar minimap and production palette, which covers the rightmost $25\%–30\%$ of the screen). This ensures troops, structures, and spice fields on the eastern edge of the map never get obscured or trapped underneath the sidebar.
* **Result**: Complete visibility of all map edges while keeping void borders minimal. Small maps automatically center without wandering into the void.

---

## 8. Bluetooth Mouse Hot-Plug & Android Activity Lifecycle

Connecting or disconnecting a Bluetooth mouse dispatches Android configuration changes (`Keyboard | Navigation | UiMode | Density | FontScale`).

* **Manifest Configuration**: In [`MainActivity.cs`](file:///data/data/com.termux/files/home/chat/dune/OpenRA.Android/MainActivity.cs), `ConfigurationChanges` was expanded so Android never tears down the Activity on peripheral connection.
* **Thread Guards**: `engineStarted` is marked `static` to prevent concurrent engine thread spawns.
* **Idempotent Support Directory**: In [`Platform.cs`](file:///data/data/com.termux/files/home/chat/dune/OpenRA.Game/Platform.cs), `OverrideSupportDir` was made idempotent if the path matches the active support directory, avoiding `InvalidOperationException` crashes.

---

## 9. Automated Content Importer & Storage Permissions (Android 11–14+)

On modern Android (Android 11–14+), scoped storage prevents apps from reading public storage without proper declarations and grants.

* **Manifest Permissions**: Declares `MANAGE_EXTERNAL_STORAGE`, `ReadExternalStorage`, `WriteExternalStorage`, and media permissions (`READ_MEDIA_AUDIO`, `READ_MEDIA_VIDEO`) without `MaxSdkVersion` restrictions so Android settings never grey out permissions on Android 13/14 tablets.
* **Auto-Permission Prompt**: On startup, if `!Environment.IsExternalStorageManager` on Android 11+, the app automatically opens the system "All files access" settings screen for Dune 2000.
* **Automated Asset & Map Scanner**: Runs on `OnCreate` and `OnResume`:
  * Scans `/sdcard/Download/d2k/Music` for original `.aud` tracks.
  * Scans `/sdcard/Download/d2k/Movies` for Westwood `.vqa` cutscenes.
  * Scans `/sdcard/Download/d2k/Maps` for custom `.oramap` community maps.
  * Automatically copies missing files into the game's internal support directory, with instant Toast and DevConsole notifications.

---

## 10. In-App Diagnostics & DevConsole Shutdown

The in-game floating diagnostic console (`🐛`) provides direct inspection of engine events on device:
* **Zero Overhead Shutdown (`🛑 Off`)**: Disconnects event delegates, unhooks log listeners, frees buffer memory, and hides the floating bubble until next restart.
* **Auto-Expand on Crash**: Automatically expands full-screen on unhandled exceptions for instant triage.
