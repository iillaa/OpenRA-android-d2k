# OpenRA: Dune 2000 on Android

Native Android port of OpenRA: Dune 2000 (`d2k`), built with .NET 9 (`net9.0-android`) and raw EGL/GLES 3.2 — no SDL2, no emulation layer.

## Status

- ✅ Dune 2000 engine + campaign + skirmish run natively on `arm64-v8a`
- ✅ OpenGL ES 3.2 rendering via raw EGL (Adreno / Mali tested with 60 FPS locked)
- ✅ Modern touch input: 1-finger smooth pan (zero jump), 1-finger tap/select/order, 2-finger box select, 4-finger zoom
- ✅ Full PC Bluetooth / USB mouse: Classic controls, 25px edge scrolling, scroll wheel zoom, hot-plug resilience
- ✅ Unified 1.8× HUD scaling (sidebar, radar 364×364, command bar, stance bar) at 100% native resolution
- ✅ Viewport edge clamping: eliminates the legacy 50% black void, keeping the map filling 88–90% of the screen
- ✅ Audio via OpenAL-Soft (OpenSL ES backend) + auto-import for Music (`.aud`) & Cutscene Movies (`.vqa`) from `Download/d2k`
- ✅ In-app diagnostics: draggable DevConsole (`🐛`) with `🛑 Off` shutdown button & CrashLogActivity
- ✅ Freeware game content auto-download from openra.net mirrors

## Architecture

```
OpenRA.Android (net9.0-android, the APK app)
 ├─ OpenRA.Game              (multi-targets net8.0;net9.0-android)
 ├─ OpenRA.Mods.Common/Cnc/D2k (multi-target, compiled-in, no disk loading)
 └─ OpenRA.Platforms.Android (EGL + GLES + touch/mouse + OpenAL audio)
```

The Android platform layer (`OpenRA.Platforms.Android`) implements OpenRA's `IPlatform`/`IPlatformWindow`/`IGraphicsContext` interfaces using:
- **EGL** via P/Invoke to `libEGL.so` (context, surface, swap)
- **GLES 3** function-pointer loader (reuses the engine's `OpenGL.cs` with `eglGetProcAddress`)
- **SurfaceView** + `ISurfaceHolderCallback` for the window surface with zero-black-screen resume
- **MotionEvent** translation for multi-touch and Bluetooth mouse input
- **OpenAL-Soft** for audio (`libsoft_oal.so`, OpenSL ES backend)
- **FreeType** for font rendering (`libfreetype6.so`)

## Build prerequisites

- .NET 9 SDK + `dotnet workload install android`
- Android SDK (API 34+), NDK r26d/r27
- JDK 17
- CMake 3.22+

## Building the native libraries

The three native dependencies (FreeType, Lua, OpenAL-Soft) are cross-compiled with the NDK for `arm64-v8a` with 16KB page-size alignment:

```bash
# Unified Linux / CI script in thirdparty/
bash thirdparty/build-android-linux.sh "$ANDROID_NDK_ROOT"
```

## Building and deploying the APK

```bash
# Build Release APK with Native AOT
dotnet build OpenRA.Android/OpenRA.Android.csproj -c Release -p:BuildForAndroid=true

# Sign with debug keystore
APK=OpenRA.Android/bin/Release/net9.0-android/net.openra.mod.d2k.apk
zipalign -f -p 4 "$APK" "${APK%.apk}-aligned.apk"
apksigner sign --ks ~/.android/debug.keystore --ks-pass pass:android \
  --out "${APK%.apk}-Signed.apk" "${APK%.apk}-aligned.apk"

adb install -r "${APK%.apk}-Signed.apk"
adb shell am start -n net.openra.mod.d2k/net.openra.android.MainActivity
```

## Controls & Input

### Touchscreen Gestures
| Gesture | Action |
|---|---|
| **1-Finger Tap** | Select unit / Click UI buttons & production tabs / Issue orders in Classic mode |
| **1-Finger Drag** | Smooth map pan (seeded drag origin, 25px touch slop, zero jump) |
| **2-Finger Frame** | Unit box selection (draws green box between fingers, lingering touch shield) |
| **4-Finger Pinch** | Zoom in / Zoom out |

### Bluetooth / USB Mouse
| Mouse Action | In-Game Function |
|---|---|
| **Left Click** | Select unit / Click UI buttons & production tabs |
| **Left Click + Drag** | Draw green unit selection box |
| **Right Click** | Issue Move, Attack, or Harvest orders / Cancel |
| **Scroll Wheel** | Smooth zoom in / out |
| **Edge Hover (25px)** | Smooth edge scrolling across the battlefield |

## Engine modifications

Changes to the core engine are minimal and guarded by `OperatingSystem.IsAndroid()`:
- `ObjectCreator.cs` — resolves mod assemblies from the default load context (compiled-in)
- `Game.cs` — instantiates `AndroidPlatform` directly; skips LOH compaction (unsupported on Mono)
- `Settings.cs` — defaults to `Classic` mouse, `Standard` scroll, `ViewportEdgeScroll = true`, margin 25 on Android
- `Viewport.cs` — dynamic `GetEffectiveScrollBounds()` preventing camera from scrolling into half-screen black void
- `PlatformInterfaces.cs` — added `StartTextInput()`/`StopTextInput()` for soft keyboard
- `Widget.cs` — calls `StartTextInput`/`StopTextInput` on focus gain/loss
- `*.csproj` — multi-target `net8.0;net9.0-android` via `BuildForAndroid` property

Desktop builds are completely unaffected.
