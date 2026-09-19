# OpenRA: Dune 2000 Android Port

[![Android Build](https://github.com/iillaa/OpenRA-android-d2k/actions/workflows/android.yml/badge.svg)](https://github.com/iillaa/OpenRA-android-d2k/actions/workflows/android.yml)
[![Target Platform](https://img.shields.io/badge/Platform-Android%20arm64--v8a-brightgreen.svg)]()
[![Runtime](https://img.shields.io/badge/.NET-9.0--android-blue.svg)]()
[![Mod](https://img.shields.io/badge/Mod-Dune%202000%20(d2k)-orange.svg)]()

Welcome to the native Android port and fork of **OpenRA: Dune 2000 (`d2k`)**!

This project brings Westwood Studios' legendary classic RTS *Dune 2000* to modern Android devices (smartphones, high-resolution tablets, and foldables) running completely natively on **`arm64-v8a`** using **.NET 9**, **Raw EGL / OpenGL ES 3.2**, **OpenAL-Soft**, and **FreeType 6**—with zero emulation layers.

---

## Key Highlights & Features

* **100% Native Architecture**: Built directly with .NET 9 Android (`net9.0-android`), compiled for 64-bit ARM (`arm64-v8a`) with Native AOT/Release performance.
* **Modern Tablet UI Scaling (Unified 1.8× HUD)**: Specially optimized for large, high-density displays (such as 12.7" 2560×1600 tablets). The sidebar, production palette, radar ($364\times364$), command bar, and stance selector are uniformly scaled by 1.8× for comfortable touch navigation while preserving 100% sharp native resolution on the battlefield.
* **Viewport Edge Clamping**: Eliminates the legacy issue where scrolling to the border filled 50% of the screen with empty black void. The camera now keeps the desert map occupying 88%–90% of the display at all times.
* **Hybrid Dual-Input Control System**:
  * **Touchscreen**: Seamless 1-finger smooth map panning (zero jump, zero misclicks), 1-finger tap select and orders, 2-finger dedicated box-selection, and 4-finger pinch-to-zoom.
  * **Bluetooth / Hardware Mouse**: True PC-style desktop gameplay with native Left-Click box selection, Right-Click move/attack commands, Scroll Wheel zoom, 25px screen-edge scrolling, and hot-plug resilience.
* **Engineered for Long Matches (Zero Memory Leaks / No Stalls)**:
  * Zero-allocation streaming vertex buffers using native C# `unsafe fixed` stack pointers, eliminating millions of GC handle allocations in 30+ minute matches.
  * OpenGL **Buffer Orphaning** (`glBufferData(..., IntPtr.Zero, ...)`) that completely prevents Qualcomm Adreno GPU driver alias pool exhaustion and pipeline stalls.
* **Automated Asset Importer**: Automatically detects and imports original soundtrack music (`.aud`) and video cutscenes (`.vqa`) placed in `/sdcard/Download/d2k`.
* **Built-in Diagnostic & Developer Tools**:
  * Floating draggable **DevConsole** bubble (`🐛`) with full-screen color-coded logs, clipboard copy, and a `🛑 Off` shutdown button to completely disable logging overhead when not needed.
  * Auto-recovering EGL surface on background/resume (eliminating the black-screen freeze on resume).
  * Automatic `CrashLogActivity` with single-tap clipboard copy.

---

## Supported Devices & Requirements

| Specification | Minimum Requirement | Recommended |
|---|---|---|
| **Architecture** | `arm64-v8a` (64-bit ARM) | Snapdragon 865 / Dimensity 1200 or newer |
| **Android Version**| Android 7.0 (API 24+) | Android 12+ (API 31+) |
| **Graphics** | OpenGL ES 3.2 via EGL | Adreno 650+ / Mali-G77+ |
| **RAM** | 3 GB | 4 GB+ |
| **Display** | Any 16:9 / 16:10 / 20:9 screen | 10"–13" Tablet (e.g. 2560×1600) |
| **Input** | Touchscreen | Touchscreen + Bluetooth Mouse |

---

## Quick Start: Download & Install

1. Navigate to the latest successful build on [GitHub Actions](https://github.com/iillaa/OpenRA-android-d2k/actions/workflows/build-android.yml).
2. Download the `OpenRA-Dune2000-apk` zip artifact.
3. Extract the APK file and install it on your Android device (ensure *Install from unknown sources* is enabled).
4. Launch **Dune 2000**. On first run, the game will automatically download the freeware game content (original audio, FMV sequences, and campaign assets) directly from OpenRA mirrors.

### Adding Custom Music & Movies (FMVs)
To add high-quality music (`.AUD`) or campaign video cutscenes (`.VQA`), place them in your device's standard **Download** folder:
* **Music**: `/storage/emulated/0/Download/d2k/Music/`
* **Movies**: `/storage/emulated/0/Download/d2k/Movies/`

On launch, the game automatically detects and imports them into its internal storage—no PC, root, or file manager hacks required!

---

## Documentation Index

Explore the comprehensive technical and operational documentation in this directory:

* 📖 [**Controls Guide (`controls.md`)**](controls.md): Detailed guide for touchscreen gestures, Bluetooth mouse controls, unit stances, and battlefield orders.
* ⚙️ [**Technical Architecture (`technical.md`)**](technical.md): Deep dive into .NET 9 Android workload, EGL/GLES 3.2 rendering, OpenAL-Soft audio, FreeType integration, and GPU memory optimizations.
* 🛠️ [**Developer & Build Guide (`developer.md`)**](developer.md): Prerequisites, compiling NDK native libraries, GitHub Actions workflows, APK packaging, and in-game debugging.
* 📈 [**Project Progress & History (`progress.md`)**](progress.md): The full chronological story from broken builds and startup crashes to an optimized, rock-solid Android RTS.
* 📋 [**Roadmap & TODO (`todo.md`)**](todo.md): Current tasks, planned input fine-tuning, lifecycle fixes, and feature backlog.
* 🤝 [**Attribution & Contributions (`contribution.md`)**](contribution.md): Upstream attribution to Tarek Hosni and detailed chronicle of all contributions and features added in this fork.
