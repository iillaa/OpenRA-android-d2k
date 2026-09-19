#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenRA;
using OpenRA.Primitives;

namespace OpenRA.Platforms.Android
{
	// Android implementation of IPlatform. The Activity creates the window (it owns the
	// SurfaceView lifecycle) and registers it via Window before Game.InitializeAndRun is called.
	public class AndroidPlatform : IPlatform
	{
		public static AndroidPlatformWindow Window { get; private set; }

		// App layer (OpenRA.Android) sets this to receive platform log messages in the
		// in-app DevConsole without creating a circular project reference.
		public static Action<string, string> PlatformLogger { get; set; }
		public static Action<string, string> PlatformErrorLogger { get; set; }

		static void PLog(string tag, string msg)
		{
			global::Android.Util.Log.Info("OpenRA", $"[{tag}] {msg}");
			PlatformLogger?.Invoke(tag, msg);
		}

		static void PLogError(string tag, string msg)
		{
			global::Android.Util.Log.Error("OpenRA", $"[{tag}] {msg}");
			PlatformErrorLogger?.Invoke(tag, msg);
		}

		static AndroidPlatform()
		{
			// ── OpenAL (soft_oal) ───────────────────────────────────────────────
			// .NET Android does not apply the legacy Mono dllmap from OpenAL-CS.dll.config,
			// so DllImport("soft_oal") fails. Pre-load via Java's loader then register resolver.
			try { Java.Lang.JavaSystem.LoadLibrary("soft_oal"); }
			catch { /* already loaded — fine */ }

			try
			{
				var openalAssembly = Assembly.Load("OpenAL-CS");
				NativeLibrary.SetDllImportResolver(openalAssembly, (libraryName, asm, searchPath) =>
				{
					if (libraryName == "soft_oal")
						foreach (var name in new[] { "soft_oal", "libsoft_oal.so", "libsoft_oal" })
							if (NativeLibrary.TryLoad(name, asm, DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.UserDirectories, out var handle))
								return handle;
					return IntPtr.Zero;
				});
			}
			catch { }

			// ── FreeType (freetype6) ────────────────────────────────────────────
			// NativeLibrary.TryLoad with ApplicationDirectory does NOT search the APK native
			// lib dir on .NET Android. Load via absolute path from ApplicationInfo.NativeLibraryDir.
			try
			{
				var nativeLibDir = global::Android.App.Application.Context.ApplicationInfo.NativeLibraryDir;
				var freetypePath = System.IO.Path.Combine(nativeLibDir, "libfreetype6.so");
				var exists = System.IO.File.Exists(freetypePath);

				PLog("FreeType", $"nativeLibDir: {nativeLibDir}");
				PLog("FreeType", $"path: {freetypePath}  exists: {exists}");

				var freetypeHandle = NativeLibrary.Load(freetypePath);
				PLog("FreeType", $"NativeLibrary.Load OK, handle={freetypeHandle}");

				var thisAssembly = Assembly.GetExecutingAssembly();
				NativeLibrary.SetDllImportResolver(thisAssembly, (libraryName, asm, searchPath) =>
				{
					if (libraryName == "freetype6")
						return freetypeHandle;
					return IntPtr.Zero;
				});

				PLog("FreeType", "DllImport resolver registered.");
			}
			catch (Exception ex)
			{
				PLogError("FreeType", $"Resolver setup FAILED: {ex}");
				try { System.IO.File.AppendAllText("/sdcard/openra_freetype_diag.txt", $"EXCEPTION: {ex}\n"); } catch { }
			}
		}

		public static void SetWindow(AndroidPlatformWindow window) => Window = window;

		public IPlatformWindow CreateWindow(
			Size size, WindowMode windowMode, float scaleModifier, int vertexBatchSize, int indexBatchSize, int videoDisplay, GLProfile profile)
		{
			// The window has already been created by the Activity (it needs the SurfaceView on the
			// UI thread). Apply the engine's requested scale modifier and hand back the existing instance.
			if (scaleModifier > 0)
				Window.SetScaleModifier(scaleModifier);

			// The EGL surface is created asynchronously by the Activity's SurfaceHolder callback on
			// the UI thread. The Renderer constructor accesses Window.Context immediately after this
			// returns, so we must block here until the surface is ready AND GL has been initialized
			// on this (the game) thread.
			Window.WaitForSurfaceAndInitializeGl();

			return Window;
		}

		public ISoundEngine CreateSound(string device)
		{
			try
			{
				PLog("OpenAL", "Initializing OpenAL...");
				var engine = new OpenAlSoundEngine(device);
				PLog("OpenAL", "Initialized successfully.");
				return engine;
			}
			catch (Exception e)
			{
				PLogError("OpenAL", $"Failed: {e}");
				Log.Write("sound", "Failed to initialize OpenAL device. Error was");
				Log.Write("sound", e);
				return new DummySoundEngine();
			}
		}

		public IFont CreateFont(byte[] data)
		{
			return new FreeTypeFont(data);
		}
	}
}
