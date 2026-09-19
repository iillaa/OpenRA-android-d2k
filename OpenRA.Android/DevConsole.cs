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
using System.Collections.Generic;
using System.Text;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace OpenRA.Android
{
	/// <summary>
	/// Thread-safe in-process log buffer. Call DevConsole.Log() from anywhere.
	/// On crash, pass the full log to CrashLogActivity via Intent.
	/// </summary>
	public static class DevConsole
	{
		static readonly object Lock = new();
		static readonly List<string> Lines = new();
		const int MaxLines = 2000;

		public static void Log(string tag, string message)
		{
			var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{tag}] {message}";
			global::Android.Util.Log.Info("OpenRA.DevConsole", line);
			lock (Lock)
			{
				Lines.Add(line);
				if (Lines.Count > MaxLines)
					Lines.RemoveAt(0);
			}
		}

		public static string GetAll()
		{
			lock (Lock)
				return string.Join("\n", Lines);
		}

		public static void Clear()
		{
			lock (Lock)
				Lines.Clear();
		}
	}

	/// <summary>
	/// Full-screen activity shown when OpenRA crashes.
	/// Displays the dev console log + the exception, with a Copy button.
	/// Launch via: CrashLogActivity.Show(context, exception, devLog).
	/// </summary>
	[Activity(
		Label = "OpenRA Crash Log",
		Theme = "@android:style/Theme.Material.NoActionBar",
		Exported = false)]
	public class CrashLogActivity : Activity
	{
		const string ExtraLog = "crash_log";

		public static void Show(Context context, Exception ex, string devLog)
		{
			var sb = new StringBuilder();
			sb.AppendLine("══════════════════════════════════");
			sb.AppendLine($"  OPENRA CRASH  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
			sb.AppendLine("══════════════════════════════════");
			sb.AppendLine($"Type    : {ex?.GetType().FullName}");
			sb.AppendLine($"Message : {ex?.Message}");
			sb.AppendLine();
			if (ex?.InnerException != null)
			{
				sb.AppendLine($"Inner   : {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
				sb.AppendLine();
			}

			sb.AppendLine("── Stack Trace ──────────────────");
			sb.AppendLine(ex?.StackTrace);
			sb.AppendLine();
			sb.AppendLine("── Dev Console Log ──────────────");
			sb.AppendLine(devLog);

			var text = sb.ToString();

			// Also write to sdcard as fallback
			try { System.IO.File.WriteAllText("/sdcard/openra_crash.txt", text); } catch { }

			var intent = new Intent(context, typeof(CrashLogActivity));
			intent.PutExtra(ExtraLog, text);
			intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
			context.StartActivity(intent);
		}

		protected override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			var logText = Intent?.GetStringExtra(ExtraLog) ?? "(no log)";

			// ── Root layout ────────────────────────────────────────────────
			var root = new LinearLayout(this)
			{
				Orientation = Orientation.Vertical,
				LayoutParameters = new LinearLayout.LayoutParams(
					ViewGroup.LayoutParams.MatchParent,
					ViewGroup.LayoutParams.MatchParent)
			};
			root.SetBackgroundColor(Color.ParseColor("#1a1a1a"));
			root.SetPadding(12, 12, 12, 12);

			// ── Title bar ──────────────────────────────────────────────────
			var titleBar = new LinearLayout(this)
			{
				Orientation = Orientation.Horizontal,
				LayoutParameters = new LinearLayout.LayoutParams(
					ViewGroup.LayoutParams.MatchParent,
					ViewGroup.LayoutParams.WrapContent)
			};

			var title = new TextView(this)
			{
				Text = "🛑 OpenRA Crash Log",
				LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
			};
			title.SetTextColor(Color.ParseColor("#ff5555"));
			title.SetTextSize(Android.Util.ComplexUnitType.Sp, 16f);
			title.SetTypeface(null, TypefaceStyle.Bold);

			var copyBtn = new Button(this)
			{
				Text = "📋 Copy",
				LayoutParameters = new LinearLayout.LayoutParams(
					ViewGroup.LayoutParams.WrapContent,
					ViewGroup.LayoutParams.WrapContent)
			};
			copyBtn.SetTextColor(Color.White);
			copyBtn.SetBackgroundColor(Color.ParseColor("#444444"));
			copyBtn.Click += (_, _) =>
			{
				var clipboard = (Android.Content.ClipboardManager)GetSystemService(ClipboardService);
				clipboard?.SetPrimaryClip(Android.Content.ClipData.NewPlainText("OpenRA Crash", logText));
				Toast.MakeText(this, "Copied to clipboard!", ToastLength.Short)?.Show();
			};

			var closeBtn = new Button(this)
			{
				Text = "✕",
				LayoutParameters = new LinearLayout.LayoutParams(
					ViewGroup.LayoutParams.WrapContent,
					ViewGroup.LayoutParams.WrapContent)
			};
			closeBtn.SetTextColor(Color.White);
			closeBtn.SetBackgroundColor(Color.ParseColor("#883333"));
			closeBtn.Click += (_, _) => Finish();

			titleBar.AddView(title);
			titleBar.AddView(copyBtn);
			titleBar.AddView(closeBtn);

			// ── Scrollable log view ────────────────────────────────────────
			var scroll = new ScrollView(this)
			{
				LayoutParameters = new LinearLayout.LayoutParams(
					ViewGroup.LayoutParams.MatchParent, 0, 1f)
			};

			var tv = new TextView(this)
			{
				Text = logText,
				LayoutParameters = new LinearLayout.LayoutParams(
					ViewGroup.LayoutParams.MatchParent,
					ViewGroup.LayoutParams.WrapContent)
			};
			tv.SetTextColor(Color.ParseColor("#ccffcc"));
			tv.SetTextSize(Android.Util.ComplexUnitType.Sp, 11f);
			tv.SetTypeface(Typeface.Monospace, TypefaceStyle.Normal);
			tv.SetTextIsSelectable(true);
			scroll.AddView(tv);

			// Scroll to bottom so latest crash line is visible first
			scroll.Post(() => scroll.FullScroll(FocusSearchDirection.Down));

			root.AddView(titleBar);
			root.AddView(scroll);
			SetContentView(root);
		}
	}
}
