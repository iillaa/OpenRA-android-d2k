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
using System.Collections.Concurrent;
using System.Diagnostics;
using Android.Views;
using OpenRA;
using OpenRA.Primitives;

namespace OpenRA.Platforms.Android
{
	// Translates Android MotionEvents (multi-touch and hardware-mouse) into OpenRA MouseInputs.
	//
	// Touch model:
	//   - Single tap          = left click (with double-tap detection via MultiTapDetection)
	//   - Long press (>500ms) = right-click (context menu, unit orders)
	//   - Drag                = mouse move with left button held (scroll the map, drag-select)
	//   - Two-finger pinch    = scroll/zoom (synthesized as MouseInputEvent.Scroll)
	//   - Two-finger drag     = map pan (mouse move with right button held)
	//
	// Mouse model (hardware mouse via OnGenericMotionEvent):
	//   - Left/right/middle buttons map directly and instantly to MouseButton events (no
	//     long-press timer).
	//   - Hover motion = Move with no button held (cursor position update).
	//   - Scroll wheel = Scroll event.
	sealed class AndroidInput
	{
		readonly ConcurrentQueue<PendingInput> pending = new();

		// Primary finger state (left button).
		int primaryPointerId = -1;
		int2 primaryDownPos;
		Stopwatch primaryDownTimer;

		// Secondary finger state (right button / pan).
		int secondaryPointerId = -1;

		// Pinch state.
		float lastPinchDist;

		// Long-press detection threshold.
		const int LongPressMs = 500;
		const int TouchSlopPx = 16;

		// Suppresses the Up event when a long-press already fired a right-click.
		bool longPressFired;

		// Physical-mouse state tracking (hardware mouse / trackball via OnGenericMotionEvent).
		// These are kept separate from the touch-pointer state above so mouse actions bypass
		// the touch long-press timer entirely for instant left/right/middle clicks.
		int lastMouseButtonState;
		int2 lastMousePos;

		// Android MotionEvent button-state bitmasks (see MotionEvent.BUTTON_*).
		const int MouseBtnPrimary = 1;   // left
		const int MouseBtnSecondary = 2; // right
		const int MouseBtnTertiary = 4;  // middle

		struct PendingInput
		{
			public MotionEventActions Action;
			public float X;
			public float Y;
			public int PointerId;
			public long TimestampMs;
			// Filled only for mouse events (IsMouse == true). Carries the Android ButtonState
			// bitmask so PumpInput can diff against the previous state to synthesize exact
			// button Down/Up transitions.
			public int ButtonState;
			// For mouse ACTION_SCROLL: the scroll delta (read by PumpInput).
			public int ScrollDelta;
			public bool IsMouse;
		}

		public void Enqueue(MotionEvent e, Size windowSize)
		{
			var action = e.ActionMasked;
			var index = e.ActionIndex;

			if (action == MotionEventActions.Move)
			{
				// Forward moves for all tracked fingers.
				for (var i = 0; i < e.PointerCount; i++)
				{
					var pid = e.GetPointerId(i);
					if (pid == primaryPointerId || pid == secondaryPointerId)
					{
						pending.Enqueue(new PendingInput
						{
							Action = action,
							X = e.GetX(i),
							Y = e.GetY(i),
							PointerId = pid,
							TimestampMs = e.EventTime
						});
					}
				}

				// Detect pinch zoom when two fingers are down.
				if (primaryPointerId >= 0 && secondaryPointerId >= 0 && e.PointerCount >= 2)
				{
					var i0 = e.FindPointerIndex(primaryPointerId);
					var i1 = e.FindPointerIndex(secondaryPointerId);
					if (i0 >= 0 && i1 >= 0)
					{
						var dx = e.GetX(i0) - e.GetX(i1);
						var dy = e.GetY(i0) - e.GetY(i1);
						var dist = (float)Math.Sqrt(dx * dx + dy * dy);
						if (lastPinchDist > 0)
						{
							var delta = (int)(dist - lastPinchDist);
							if (Math.Abs(delta) > 2)
							{
								pending.Enqueue(new PendingInput
								{
									Action = MotionEventActions.Scroll,
									X = (e.GetX(i0) + e.GetX(i1)) / 2,
									Y = (e.GetY(i0) + e.GetY(i1)) / 2,
									PointerId = -1,
									TimestampMs = e.EventTime
								});
								// Store the delta in the Y field via a side channel — we'll read it in PumpInput.
								pinchDelta = delta;
							}
						}

						lastPinchDist = dist;
					}
				}
			}
			else
			{
				pending.Enqueue(new PendingInput
				{
					Action = action,
					X = e.GetX(index),
					Y = e.GetY(index),
					PointerId = e.GetPointerId(index),
					TimestampMs = e.EventTime
				});
			}
		}

		// Hardware-mouse / trackball events arrive on a separate callback (OnGenericMotionEvent)
		// because Android does not deliver mouse motion through OnTouchEvent. We route them here
		// with IsMouse=true so PumpInput can bypass the touch long-press timer and map ButtonState
		// directly to instant MouseButton Down/Up events.
		public void EnqueueMouse(MotionEvent e, Size windowSize)
		{
			var action = e.ActionMasked;
			var pos = new int2((int)e.GetX(0), (int)e.GetY(0));

			if (action == MotionEventActions.Scroll)
			{
				// Scroll-wheel delta. Per the Android MotionEvent docs, ACTION_SCROLL reports
				// the scroll offset in AXIS_VSCROLL (9) / AXIS_HSCROLL (10), not the relative axes.
				var dy = (int)(e.GetAxisValue(Axis.Vscroll) * -10);
				var dx = (int)(e.GetAxisValue(Axis.Hscroll) * 10);
				pending.Enqueue(new PendingInput
				{
					Action = action,
					X = pos.X,
					Y = pos.Y,
					PointerId = -1,
					TimestampMs = e.EventTime,
					IsMouse = true,
					ButtonState = (int)e.ButtonState,
					ScrollDelta = dy != 0 ? dy : dx
				});
			}
			else
			{
				pending.Enqueue(new PendingInput
				{
					Action = action,
					X = pos.X,
					Y = pos.Y,
					PointerId = -1,
					TimestampMs = e.EventTime,
					IsMouse = true,
					ButtonState = (int)e.ButtonState
				});
			}
		}

		int pinchDelta;

		public void PumpInput(IInputHandler inputHandler, Size windowSize, Size surfaceSize, float scale)
		{
			var scaleX = (surfaceSize.Width > 0 && windowSize.Width > 0) ? (float)windowSize.Width / surfaceSize.Width : 1f;
			var scaleY = (surfaceSize.Height > 0 && windowSize.Height > 0) ? (float)windowSize.Height / surfaceSize.Height : 1f;

			while (pending.TryDequeue(out var p))
			{
				var pos = new int2((int)(p.X * scaleX), (int)(p.Y * scaleY));

				if (p.IsMouse)
				{
					HandleMouse(inputHandler, p, pos);
					continue;
				}

				switch (p.Action)
				{
					case MotionEventActions.Down:
						primaryPointerId = p.PointerId;
						primaryDownPos = pos;
						primaryDownTimer = Stopwatch.StartNew();
						longPressFired = false;
						var tapCount = MultiTapDetection.DetectFromMouse(0, pos);
						inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Left, pos, int2.Zero, Modifiers.None, tapCount));
						break;

					case MotionEventActions.PointerDown:
						if (secondaryPointerId < 0)
						{
							secondaryPointerId = p.PointerId;
							lastPinchDist = 0;

							// If the primary finger is down, start a right-button drag (map pan).
							// Release the left button first so we don't hold Left+Right simultaneously
							// (which would draw a giant selection box instead of just panning).
							if (primaryPointerId >= 0 && !longPressFired)
							{
								inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, pos, int2.Zero, Modifiers.None, 1));
								inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Right, pos, int2.Zero, Modifiers.None, 1));
							}
						}

						break;

					case MotionEventActions.Move:
						if (p.PointerId == primaryPointerId)
						{
							// Check for long-press (right-click) if the finger hasn't moved much.
							if (!longPressFired && primaryDownTimer != null && primaryDownTimer.ElapsedMilliseconds > LongPressMs)
							{
								var moved = (pos - primaryDownPos).Length;
								if (moved < TouchSlopPx)
								{
									longPressFired = true;
									inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, pos, int2.Zero, Modifiers.None, 1));
									inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Right, pos, int2.Zero, Modifiers.None, 1));
								}
							}
							else
							{
								inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.Left, pos, int2.Zero, Modifiers.None, 0));
							}
						}
						else if (p.PointerId == secondaryPointerId && primaryPointerId >= 0)
						{
							// Two-finger pan: move with right button.
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.Right, pos, int2.Zero, Modifiers.None, 0));
						}

						break;

					case MotionEventActions.PointerUp:
						if (p.PointerId == secondaryPointerId)
						{
							// End right-button drag.
							if (primaryPointerId >= 0 && !longPressFired)
								inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Right, pos, int2.Zero, Modifiers.None, 1));
							secondaryPointerId = -1;
							lastPinchDist = 0;
						}

						break;

					case MotionEventActions.Up:
						if (longPressFired)
						{
							// The long-press already sent a right-click Down; send the matching Up.
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Right, pos, int2.Zero, Modifiers.None, 1));
						}
						else
						{
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, pos, int2.Zero, Modifiers.None, MultiTapDetection.InfoFromMouse(0)));
						}

						primaryPointerId = -1;
						primaryDownTimer = null;
						longPressFired = false;
						break;

					case MotionEventActions.Cancel:
						// The system aborted the gesture (e.g. an incoming call or the OS
						// intercepting the touch). Release any held buttons and reset state
						// so we don't leave the engine thinking a button is permanently down.
						if (primaryPointerId >= 0 && !longPressFired)
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, pos, int2.Zero, Modifiers.None, 1));
						else if (primaryPointerId >= 0 && longPressFired)
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Right, pos, int2.Zero, Modifiers.None, 1));
						if (secondaryPointerId >= 0 && primaryPointerId >= 0 && !longPressFired)
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Right, pos, int2.Zero, Modifiers.None, 1));
						primaryPointerId = -1;
						secondaryPointerId = -1;
						primaryDownTimer = null;
						longPressFired = false;
						lastMouseButtonState = 0;
						break;

					case MotionEventActions.Scroll:
						// Pinch-to-zoom: synthesize a scroll event. The zoom modifier (Ctrl) is needed
						// by ViewportControllerWidget, so we set it to make zoom work without a keyboard.
						inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Scroll, MouseButton.None, pos, new int2(0, pinchDelta), Modifiers.Ctrl, 0));
						pinchDelta = 0;
						break;
				}
			}
		}

		// Translate a hardware-mouse MotionEvent into OpenRA MouseInput events.
		// Android reports button changes as bitmask diffs between consecutive events, so we
		// diff ButtonState to emit exact Down/Up transitions for Left/Right/Middle and a Move
		// with the current cursor position (for dragging while a button is held).
		void HandleMouse(IInputHandler inputHandler, in PendingInput p, int2 pos)
		{
			lastMousePos = pos;

			if (p.Action == MotionEventActions.Scroll)
			{
				// Scroll wheel → Scroll event. Ctrl modifier enables zoom in ViewportControllerWidget.
				inputHandler.OnMouseInput(new MouseInput(
					MouseInputEvent.Scroll, MouseButton.None, pos,
					new int2(0, p.ScrollDelta), Modifiers.Ctrl, 0));
				lastMouseButtonState = p.ButtonState;
				return;
			}

			var prev = lastMouseButtonState;
			var curr = p.ButtonState;

			// Fire Down for buttons newly pressed since the last event.
			if ((curr & MouseBtnPrimary) != 0 && (prev & MouseBtnPrimary) == 0)
			{
				var downTapCount = MultiTapDetection.DetectFromMouse(0, pos);
				inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Left, pos, int2.Zero, Modifiers.None, downTapCount));
			}

			if ((curr & MouseBtnSecondary) != 0 && (prev & MouseBtnSecondary) == 0)
			{
				inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Right, pos, int2.Zero, Modifiers.None, 1));
			}

			if ((curr & MouseBtnTertiary) != 0 && (prev & MouseBtnTertiary) == 0)
			{
				inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Middle, pos, int2.Zero, Modifiers.None, 1));
			}

			// Fire Up for buttons that were released.
			if ((prev & MouseBtnPrimary) != 0 && (curr & MouseBtnPrimary) == 0)
			{
				inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, pos, int2.Zero, Modifiers.None, MultiTapDetection.InfoFromMouse(0)));
			}

			if ((prev & MouseBtnSecondary) != 0 && (curr & MouseBtnSecondary) == 0)
			{
				inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Right, pos, int2.Zero, Modifiers.None, 1));
			}

			if ((prev & MouseBtnTertiary) != 0 && (curr & MouseBtnTertiary) == 0)
			{
				inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Middle, pos, int2.Zero, Modifiers.None, 1));
			}

			// Emit a Move with the currently-held button(s) so drag operations (select box,
			// right-drag pan) continue to work while the mouse moves. Move with no button held
			// just updates the cursor position.
			var heldButton = MouseButton.None;
			var tapCount = 0;
			if ((curr & MouseBtnPrimary) != 0)
				heldButton = MouseButton.Left;
			else if ((curr & MouseBtnSecondary) != 0)
				heldButton = MouseButton.Right;
			else if ((curr & MouseBtnTertiary) != 0)
				heldButton = MouseButton.Middle;

			if (heldButton == MouseButton.Left)
				tapCount = MultiTapDetection.InfoFromMouse(0);

			// Only emit a move on HOVER_MOVE / MOVE actions (not on pure press/release, where
			// the Down/Up events above are sufficient and a redundant Move can misposition the
			// selection drag origin).
			if (p.Action == MotionEventActions.HoverMove || p.Action == MotionEventActions.Move)
			{
				inputHandler.OnMouseInput(new MouseInput(
					MouseInputEvent.Move, heldButton, pos, int2.Zero, Modifiers.None, tapCount));
			}

			lastMouseButtonState = curr;
		}
	}
}
