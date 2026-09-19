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

		static AndroidPlatform()
		{
			// .NET Android does not apply the legacy Mono dllmap from OpenAL-CS.dll.config, so
			// DllImport("soft_oal") fails to resolve to libsoft_oal.so. Register a resolver on the
			// OpenAL assembly that performs the mapping manually.
			try
			{
				// Pre-load via Java's loader, which searches the app's nativeLibraryDir.
				Java.Lang.JavaSystem.LoadLibrary("soft_oal");
			}
			catch { /* may throw if already loaded — that's fine */ }

	
			try
			{
				var openalAssembly = Assembly.Load("OpenAL-CS");
				NativeLibrary.SetDllImportResolver(openalAssembly, (libraryName, asm, searchPath) =>
				{
					if (libraryName == "soft_oal")
					{
						// Try several name variants that .NET Android may search for.
						foreach (var name in new[] { "soft_oal", "libsoft_oal.so", "libsoft_oal" })
							if (NativeLibrary.TryLoad(name, asm, DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.UserDirectories, out var handle))
								return handle;
					}

					return IntPtr.Zero;
				});
			}
			catch { }

			// Also register a resolver for the current assembly (which contains FreeType imports).
			// NativeLibrary.TryLoad with ApplicationDirectory does NOT search the APK native lib
			// dir on .NET Android. We must resolve the absolute path via Android's ApplicationInfo.
			try
			{
				var nativeLibDir = global::Android.App.Application.Context.ApplicationInfo.NativeLibraryDir;
				var freetypePath = System.IO.Path.Combine(nativeLibDir, "libfreetype6.so");
				var exists = System.IO.File.Exists(freetypePath);
				global::Android.Util.Log.Info("OpenRA", $"FreeType resolver: nativeLibDir={nativeLibDir}, path={freetypePath}, exists={exists}");

				// Also dump to sdcard so Termux can read it without ADB
				System.IO.File.WriteAllText("/sdcard/openra_freetype_diag.txt",
					$"nativeLibDir: {nativeLibDir}\npath: {freetypePath}\nexists: {exists}\n");

				var freetypeHandle = NativeLibrary.Load(freetypePath);
				global::Android.Util.Log.Info("OpenRA", $"FreeType pre-loaded via NativeLibrary.Load: handle={freetypeHandle}");
				System.IO.File.AppendAllText("/sdcard/openra_freetype_diag.txt", $"handle: {freetypeHandle}\n");

				var thisAssembly = Assembly.GetExecutingAssembly();
				NativeLibrary.SetDllImportResolver(thisAssembly, (libraryName, asm, searchPath) =>
				{
					if (libraryName == "freetype6")
						return freetypeHandle;

					return IntPtr.Zero;
				});
			}
			catch (Exception ex)
			{
				global::Android.Util.Log.Error("OpenRA", $"FreeType resolver setup failed: {ex}");
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
				global::Android.Util.Log.Info("OpenRA", "CreateSound: initializing OpenAL...");
				var engine = new OpenAlSoundEngine(device);
				global::Android.Util.Log.Info("OpenRA", "CreateSound: OpenAL initialized successfully");
				return engine;
			}
			catch (Exception e)
			{
				global::Android.Util.Log.Error("OpenRA", $"CreateSound: OpenAL failed: {e}");
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
