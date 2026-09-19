# Developer & Build Guide

This guide contains everything required to build, package, test, and debug the **OpenRA: Dune 2000 (`d2k`)** native Android port from source, both locally and via CI.

---

## 1. Development Prerequisites & Environment

To build OpenRA Android on a Linux development workstation (or CI runner), install the following tools:

| Component | Minimum Version | Purpose |
|---|---|---|
| **.NET SDK** | .NET 9.0 (`9.0.100+`) | Main C# compilation and .NET Android workload runtime |
| **Android Workload** | .NET 9 Android Workload | Android binding, AOT compilation, APK packaging |
| **Java JDK** | OpenJDK / Temurin 17 | Android Gradle / SDK build tooling |
| **Android SDK** | API Level 34 (Android 14) | Target framework and Android system APIs |
| **Android NDK** | NDK r26d (`26.3.11579264`) or r27 | Native C/C++ cross-compilation for `arm64-v8a` |
| **CMake** | 3.22+ | Building FreeType and OpenAL-Soft native libraries |
| **Build Tools** | `ninja`, `make`, `curl`, `unzip` | Compilation and archive extraction utilities |

### Setting Up .NET 9 Android Workload
```bash
# Install the Android workload for .NET 9
dotnet workload install android
dotnet workload restore
```

---

## 2. Compiling Native C/C++ Libraries (`arm64-v8a`)

OpenRA depends on three native C shared libraries that must be cross-compiled for 64-bit ARM (`arm64-v8a`):
1. **Lua 5.1.5** (`liblua51.so`): Map and campaign mission scripting.
2. **FreeType 2.13.3** (`libfreetype6.so` & `libfreetype.so`): In-game text and font rendering.
3. **OpenAL-Soft 1.23.1** (`libsoft_oal.so`): 3D positional audio and sound effect mixing via Android OpenSL ES backend.

### Running the Build Script
A unified cross-compilation script is provided at [`thirdparty/build-android-linux.sh`](file:///data/data/com.termux/files/home/chat/dune/thirdparty/build-android-linux.sh):

```bash
# Provide the absolute path to your Android NDK root
bash thirdparty/build-android-linux.sh "$ANDROID_HOME/ndk/27.0.12179864"
```

### Script Execution Flow:
* Downloads source tarballs if not already cached in `thirdparty/`.
* Invokes NDK Clang (`aarch64-linux-android24-clang`) with `-O2 -fPIC`.
* **Enforces 16KB Page-Size Alignment**: Passes `-Wl,-z,max-page-size=16384` to ensure compatibility with modern Android 15+ kernels.
* Compiles FreeType with CMake, stripping unnecessary dependencies (bzip2, brotli, harfbuzz, png) while linking standard `-lz -lm`.
* Compiles OpenAL-Soft with `-DALSOFT_BACKEND_OPENSL=ON` and statically links `libc++_static.a`.
* Outputs compiled binaries into [`OpenRA.Android/jniLibs/arm64-v8a/`](file:///data/data/com.termux/files/home/chat/dune/OpenRA.Android/jniLibs/arm64-v8a/):
  ```
  OpenRA.Android/jniLibs/arm64-v8a/
  ├── libfreetype.so
  ├── libfreetype6.so
  ├── liblua51.so
  └── libsoft_oal.so
  ```

---

## 3. Building the Android APK

Once the native libraries are in `jniLibs/arm64-v8a`, build the APK using `dotnet`:

```bash
# Build a Release APK with Native AOT
dotnet build OpenRA.Android/OpenRA.Android.csproj -c Release -p:BuildForAndroid=true
```

### Build Artifact Location:
The output APK is generated at:
```
OpenRA.Android/bin/Release/net9.0-android/net.openra.mod.d2k-Signed.apk
```

> [!IMPORTANT]
> **Always use Release configuration (`-c Release`)**:
> In .NET Android, `Debug` builds use Mono Fast Deployment (shared runtime directory on device), which causes runtime dynamic linker crashes when loading embedded native libraries. `Release` builds bundle all assemblies and native `.so` libraries directly into the APK.

---

## 4. GitHub Actions CI/CD Pipeline

Continuous integration is fully automated via [`.github/workflows/android.yml`](file:///data/data/com.termux/files/home/chat/dune/.github/workflows/android.yml).

### CI Workflow Stages:
1. **Runner Environment**: Runs on `ubuntu-22.04`.
2. **Toolchain Setup**: Installs .NET 9.0 SDK, Android Workload, JDK 17, and locates/installs Android NDK r26d/r27.
3. **Native Compilation**: Executes `build-android-linux.sh` to generate the `arm64-v8a` shared libraries.
4. **Library Verification**: Inspects ELF dynamic headers using `llvm-readelf -d` to verify Soname and 16KB max-page-size flags.
5. **APK Packaging**: Runs `dotnet build -c Release -p:BuildForAndroid=true`.
6. **Package Inspection**: Unzips the generated APK to verify `libfreetype6.so`, `liblua51.so`, and `libsoft_oal.so` are present in `lib/arm64-v8a/`.
7. **Artifact Upload**: Publishes `OpenRA-Dune2000-apk` for immediate tester download.

---

## 5. In-Game Debugging & Diagnostic Tools

Developing real-time strategy games on mobile devices requires immediate access to runtime logs without connecting a desktop debugger. The Android port includes two dedicated diagnostics tools:

### A. The Floating DevConsole (`🐛`)
Implemented in [`DevConsole.cs`](file:///data/data/com.termux/files/home/chat/dune/OpenRA.Android/DevConsole.cs):
* **Floating Bubble**: A persistent, draggable bug icon (`🐛`) renders in the top-right corner over the OpenGL surface.
* **Full-Screen Console**: Tapping the bug expands a semi-transparent, full-screen terminal showing real-time logs.
* **Color-Coded Output**:
  * 🟢 **Information / Game Events**: Green
  * 🟡 **Warnings & Performance Hints**: Yellow
  * 🔴 **Exceptions & Critical Errors**: Bright Red
* **Interactive Controls**:
  * **Copy (`📋`)**: Copies the entire log history directly to the Android system clipboard.
  * **Disable / Off (`🛑 Off`)**: Shuts down logging, detaches delegates to eliminate all CPU/RAM overhead, and completely hides the floating bubble until the next app launch.
  * **Clear (`🗑`)**: Clears buffered log entries and frees memory.
  * **Close (`✕`)**: Minimizes the console back into the floating bubble.
* **Auto-Expand on Crash**: If an unhandled exception or critical error occurs during gameplay, the DevConsole automatically expands so you can see the stack trace instantly.

### B. CrashLogActivity
Implemented in [`MainActivity.cs`](file:///data/data/com.termux/files/home/chat/dune/OpenRA.Android/MainActivity.cs):
* If a fatal crash occurs before the game window initializes, the global exception handler traps the error and launches a standalone native Android Activity (`CrashLogActivity`).
* Displays a scrollable text view with the full managed stack trace and native crash details.
* Includes a **"Copy Stacktrace & Exit"** button for quick reporting.

### C. Reading Logs via ADB or Termux
If you have ADB connected or are running Termux locally:
```bash
# Stream OpenRA and .NET runtime logs
adb logcat -s OpenRA:V dotnet:V Mono:V DEBUG:V AndroidRuntime:E
```

---

## 6. Android Project Configuration Reference

Key settings in [`OpenRA.Android.csproj`](file:///data/data/com.termux/files/home/chat/dune/OpenRA.Android/OpenRA.Android.csproj):

```xml
<PropertyGroup>
  <TargetFramework>net9.0-android</TargetFramework>
  <SupportedOSPlatformVersion>24.0</SupportedOSPlatformVersion>
  <RuntimeIdentifiers>android-arm64</RuntimeIdentifiers>
  <ApplicationId>net.openra.mod.d2k</ApplicationId>
  <ApplicationVersion>1</ApplicationVersion>
  <ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>
  <AndroidPackageFormat>apk</AndroidPackageFormat>
</PropertyGroup>
```

* **`SupportedOSPlatformVersion = 24.0`**: Android 7.0 Nougat minimum requirement.
* **`RuntimeIdentifiers = android-arm64`**: Strictly targets 64-bit ARM devices (`arm64-v8a`), stripping 32-bit overhead.
* **Dynamic Library Preloading**: In [`MainActivity.cs`](file:///data/data/com.termux/files/home/chat/dune/OpenRA.Android/MainActivity.cs), `Java.Lang.JavaSystem.LoadLibrary("freetype6")`, `LoadLibrary("lua51")`, and `LoadLibrary("soft_oal")` are called during `OnCreate()` to ensure symbols are loaded into memory before any P/Invoke takes place.
