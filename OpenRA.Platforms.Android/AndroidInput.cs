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
	// Translates Android MotionEvents (multi-touch and hardware/Bluetooth mouse) into OpenRA MouseInputs.
	//
	// Touch model:
	//   - Single tap          = Left click (Select unit / order move or attack in Classic mode / click HUD)
	//   - Single-finger drag  = Map panning (smooth 1:1 camera navigation across the battlefield)
	//   - Two-finger drag     = Unit box-selection (draws green box to select multiple units)
	//   - Two-finger pinch    = Zoom in/out
	//
	// Mouse model (hardware / Bluetooth mouse):
	//   - Left/right/middle buttons map directly and instantly to MouseButton events (no touch delays).
	//   - Hover motion = Move with no button held (updates cursor position and enables edge scrolling).
	//   - Scroll wheel = Scroll event (zooms in/out).
	sealed class AndroidInput
	{
		readonly ConcurrentQueue<PendingInput> pending = new();

		enum TouchGestureState { None, PotentialTap, Panning, TwoFingerPending, BoxSelecting, Pinching }
		TouchGestureState touchState = TouchGestureState.None;

		// Primary finger state
		int primaryPointerId = -1;
		int2 primaryDownPos;
		int2 primaryLastPos;

		// Secondary finger state
		int secondaryPointerId = -1;
		int2 secondaryDownPos;
		int2 secondaryLastPos;

		// Pinch tracking
		float initialPinchDist;
		float lastPinchDist;

		const int TouchSlopPx = 14;

		// Physical-mouse state tracking
		int lastMouseButtonState;
		int2 lastMousePos;

		const int MouseBtnPrimary = 1;   // left
		const int MouseBtnSecondary = 2; // right
		const int MouseBtnTertiary = 4;  // middle

		struct PendingInput
		{
			public MotionEventActions Action;
			public float X;
			public float Y;
			public float X2;
			public float Y2;
			public int PointerCount;
			public int PointerId;
			public long TimestampMs;
			public int ButtonState;
			public int ScrollDelta;
			public bool IsMouse;
		}

		public void Enqueue(MotionEvent e, Size windowSize)
		{
			var action = e.ActionMasked;
			var index = e.ActionIndex;
			var count = e.PointerCount;

			var pi = new PendingInput
			{
				Action = action,
				PointerCount = count,
				PointerId = e.GetPointerId(index),
				X = e.GetX(0),
				Y = e.GetY(0),
				TimestampMs = e.EventTime,
				IsMouse = false
			};

			if (count >= 2)
			{
				pi.X2 = e.GetX(1);
				pi.Y2 = e.GetY(1);
			}

			pending.Enqueue(pi);
		}

		public void EnqueueMouse(MotionEvent e, Size windowSize)
		{
			var action = e.ActionMasked;
			var pos = new int2((int)e.GetX(0), (int)e.GetY(0));

			if (action == MotionEventActions.Scroll)
			{
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

				var pos2 = p.PointerCount >= 2 ? new int2((int)(p.X2 * scaleX), (int)(p.Y2 * scaleY)) : int2.Zero;

				switch (p.Action)
				{
					case MotionEventActions.Down:
						primaryPointerId = p.PointerId;
						primaryDownPos = pos;
						primaryLastPos = pos;
						secondaryPointerId = -1;
						touchState = TouchGestureState.PotentialTap;
						break;

					case MotionEventActions.PointerDown:
						if (secondaryPointerId < 0)
						{
							secondaryPointerId = p.PointerId;
							secondaryDownPos = pos2;
							secondaryLastPos = pos2;

							// If 1-finger panning was in progress, cleanly end it before transitioning to 2-finger mode
							if (touchState == TouchGestureState.Panning)
								inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Right, primaryLastPos, int2.Zero, Modifiers.None, 1));

							touchState = TouchGestureState.TwoFingerPending;
							initialPinchDist = (pos - pos2).Length;
							lastPinchDist = initialPinchDist;
						}

						break;

					case MotionEventActions.Move:
						if (p.PointerCount >= 2)
						{
							var currentDist = (pos - pos2).Length;
							var pinchDeltaDist = Math.Abs(currentDist - initialPinchDist);

							if (touchState == TouchGestureState.Pinching || (touchState != TouchGestureState.BoxSelecting && pinchDeltaDist > 35))
							{
								// Pinch-to-zoom
								touchState = TouchGestureState.Pinching;
								var delta = (int)(currentDist - lastPinchDist);
								if (Math.Abs(delta) > 2)
								{
									var center = (pos + pos2) / 2;
									inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Scroll, MouseButton.None, center, new int2(0, delta), Modifiers.Ctrl, 0));
									lastPinchDist = currentDist;
								}
							}
							else
							{
								// Two-finger box selection
								if (touchState != TouchGestureState.BoxSelecting)
								{
									touchState = TouchGestureState.BoxSelecting;
									inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Left, primaryDownPos, int2.Zero, Modifiers.None, 1));
								}

								inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.Left, pos2, int2.Zero, Modifiers.None, 0));
							}

							primaryLastPos = pos;
							secondaryLastPos = pos2;
						}
						else if (secondaryPointerId < 0)
						{
							// Single finger
							if (touchState == TouchGestureState.PotentialTap)
							{
								if ((pos - primaryDownPos).Length > TouchSlopPx)
								{
									touchState = TouchGestureState.Panning;
									// Start map pan (Right-click Down initiates standard drag scroll in Classic mode)
									inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Right, primaryDownPos, int2.Zero, Modifiers.None, 1));
									inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.Right, pos, int2.Zero, Modifiers.None, 0));
									primaryLastPos = pos;
								}
							}
							else if (touchState == TouchGestureState.Panning)
							{
								inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.Right, pos, int2.Zero, Modifiers.None, 0));
								primaryLastPos = pos;
							}
						}

						break;

					case MotionEventActions.PointerUp:
						if (p.PointerId == secondaryPointerId || p.PointerCount <= 2)
						{
							if (touchState == TouchGestureState.BoxSelecting)
								inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, secondaryLastPos, int2.Zero, Modifiers.None, 1));

							touchState = TouchGestureState.None;
							secondaryPointerId = -1;
						}

						break;

					case MotionEventActions.Up:
						if (touchState == TouchGestureState.PotentialTap)
						{
							// Quick tap: crisp single-click (selects unit, gives move/attack order in Classic mode, or clicks UI/radar)
							var tapCount = MultiTapDetection.DetectFromMouse(0, primaryDownPos);
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Down, MouseButton.Left, primaryDownPos, int2.Zero, Modifiers.None, tapCount));
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, primaryDownPos, int2.Zero, Modifiers.None, tapCount));
						}
						else if (touchState == TouchGestureState.Panning)
						{
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Right, pos, int2.Zero, Modifiers.None, 1));
						}
						else if (touchState == TouchGestureState.BoxSelecting)
						{
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, pos, int2.Zero, Modifiers.None, 1));
						}

						touchState = TouchGestureState.None;
						primaryPointerId = -1;
						secondaryPointerId = -1;

						// Send a neutral cursor move to screen center so edge scrolling does not linger after lifting finger
						inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.None, new int2(windowSize.Width / 2, windowSize.Height / 2), int2.Zero, Modifiers.None, 0));
						break;

					case MotionEventActions.Cancel:
						if (touchState == TouchGestureState.Panning)
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Right, pos, int2.Zero, Modifiers.None, 1));
						else if (touchState == TouchGestureState.BoxSelecting)
							inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, pos, int2.Zero, Modifiers.None, 1));

						touchState = TouchGestureState.None;
						primaryPointerId = -1;
						secondaryPointerId = -1;
						inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Move, MouseButton.None, new int2(windowSize.Width / 2, windowSize.Height / 2), int2.Zero, Modifiers.None, 0));
						break;
				}
			}
		}

		void HandleMouse(IInputHandler inputHandler, in PendingInput p, int2 pos)
		{
			lastMousePos = pos;

			if (p.Action == MotionEventActions.Scroll)
			{
				inputHandler.OnMouseInput(new MouseInput(
					MouseInputEvent.Scroll, MouseButton.None, pos,
					new int2(0, p.ScrollDelta), Modifiers.Ctrl, 0));
				lastMouseButtonState = p.ButtonState;
				return;
			}

			var prev = lastMouseButtonState;
			var curr = p.ButtonState;

			// If Android reports ACTION_DOWN with ButtonState 0, fallback to primary (left) button
			if (p.Action == MotionEventActions.Down && curr == 0)
				curr = MouseBtnPrimary;

			// Fire Down for buttons newly pressed
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

			// Fire Up for buttons that were released
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

			// If ACTION_UP arrives and previous state had no flags recorded, ensure Left Up is fired
			if (p.Action == MotionEventActions.Up && prev == 0)
			{
				inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Up, MouseButton.Left, pos, int2.Zero, Modifiers.None, MultiTapDetection.InfoFromMouse(0)));
			}

			// Determine held button for dragging
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

			// Forward hover and drag moves (updates cursor position and drives edge scrolling)
			if (p.Action == MotionEventActions.HoverMove || p.Action == MotionEventActions.Move)
			{
				inputHandler.OnMouseInput(new MouseInput(
					MouseInputEvent.Move, heldButton, pos, int2.Zero, Modifiers.None, tapCount));
			}

			lastMouseButtonState = curr;
		}
	}
}
