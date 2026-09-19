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

### B. OpenGL Buffer Orphaning
* **Legacy Problem**: Continuously writing to the same `tempVertexBuffer` with `glBufferSubData` forced the Adreno GPU driver to allocate internal memory aliases to avoid stalls. Once the driver's alias pool exhausted, the CPU was blocked by GPU pipeline stalls, causing extreme stutter and game slowness.
* **Solution**: Implemented **Buffer Orphaning**:
  ```csharp
  if (start == 0 && bufferSize > 0)
  {
      OpenGL.glBufferData(OpenGL.GL_ARRAY_BUFFER,
          new IntPtr(VertexSize * bufferSize),
          IntPtr.Zero,
          OpenGL.GL_DYNAMIC_DRAW);
  }
  ```
  Passing `IntPtr.Zero` informs the driver that the previous contents can be discarded. The driver immediately provides a fresh memory block with zero stalls.

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
  * Interactive panels (sidebar, production palette, and radar) are scaled up by **1.8×** in `mods/d2k/chrome/ingame-player.yaml`.
* **Dynamic Grid Recalculation**:
  * `ProductionPaletteWidget.cs` scales `IconSize` ($104\times86$) and `IconMargin` ($4\times0$).
  * `SupportPowersWidget.cs` scales support power icons ($108\times86$).
  * `RadarWidget.cs` renders at $364\times364$ pixels with full touch camera navigation.
