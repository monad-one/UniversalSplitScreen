﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using UniversalSplitScreen.Core;
using UniversalSplitScreen.SendInput;

namespace UniversalSplitScreen.RawInput
{
	internal class MessageProcessor
	{
		/// <summary>
		/// Only updated when split screen is deactivated
		/// </summary>
		public IntPtr LastKeyboardPressed { get; private set; } = IntPtr.Zero;
		
		//leftMiddleRight: left=1, middle=2, right=3, xbutton1=4, xbutton2=5
		private readonly Dictionary<ButtonFlags, (MouseInputNotifications msg, uint wParam, ushort leftMiddleRight, bool isButtonDown, int VKey)> _buttonFlagToMouseInputNotifications = new Dictionary<ButtonFlags, (MouseInputNotifications, uint, ushort, bool, int)>()
		{
			{ ButtonFlags.RI_MOUSE_LEFT_BUTTON_DOWN,	(MouseInputNotifications.WM_LBUTTONDOWN ,	0x0001,		1, true,    0x01) },
			{ ButtonFlags.RI_MOUSE_LEFT_BUTTON_UP,		(MouseInputNotifications.WM_LBUTTONUP,		0,			1, false,   0x01) },

			{ ButtonFlags.RI_MOUSE_RIGHT_BUTTON_DOWN,	(MouseInputNotifications.WM_RBUTTONDOWN,	0x0002,		2, true,    0x02) },
			{ ButtonFlags.RI_MOUSE_RIGHT_BUTTON_UP,		(MouseInputNotifications.WM_RBUTTONUP,		0,			2, false,   0x02) },

			{ ButtonFlags.RI_MOUSE_MIDDLE_BUTTON_DOWN,	(MouseInputNotifications.WM_MBUTTONDOWN,	0x0010,		3, true,    0x04) },
			{ ButtonFlags.RI_MOUSE_MIDDLE_BUTTON_UP,	(MouseInputNotifications.WM_MBUTTONUP,		0,			3, false,   0x04) },

			{ ButtonFlags.RI_MOUSE_BUTTON_4_DOWN,		(MouseInputNotifications.WM_XBUTTONDOWN,	0x0120,		4, true,    0x05) },// (0x0001 << 8) | 0x0020 = 0x0120
			{ ButtonFlags.RI_MOUSE_BUTTON_4_UP,			(MouseInputNotifications.WM_XBUTTONUP,		0,			4, false,   0x05) },

			{ ButtonFlags.RI_MOUSE_BUTTON_5_DOWN,		(MouseInputNotifications.WM_XBUTTONDOWN,    0x0240,		5, true,    0x06) },//(0x0002 << 8) | 0x0040 = 0x0240
			{ ButtonFlags.RI_MOUSE_BUTTON_5_UP,			(MouseInputNotifications.WM_XBUTTONUP,		0,			5, false,   0x06) }
		};

		#region End key
		private ushort _endVKey = 0x23;//End. 0x23 = 35
		private bool _waitingToSetEndKey = false;


		public void WaitToSetEndKey()
		{
			_waitingToSetEndKey = true;
			Program.Form.SetEndButtonText("Press a key...");
		}

		public void StopWaitingToSetEndKey()
		{
			_waitingToSetEndKey = false;
			Program.Form.SetEndButtonText($"Stop button = {System.Windows.Input.KeyInterop.KeyFromVirtualKey(_endVKey)}");
			Options.CurrentOptions.EndVKey = _endVKey;
		}
		#endregion
		
		public MessageProcessor()
		{
			_endVKey = Options.CurrentOptions.EndVKey;
		}

		public void WndProc(ref Message msg)
		{
			if (msg.Msg == WinApi.WM_INPUT)
			{
				IntPtr hRawInput = msg.LParam;

				Process(hRawInput);
			}
		}

		static void PostMessageA(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
		{
			SendInput.WinApi.PostMessageA(hWnd, msg, wParam, lParam);
		}
		
		private void Process(IntPtr hRawInput)
		{
			uint pbDataSize = 0;
			/*Return Value (of GetRawInputData)
			Type: UINT
			If pData is NULL and the function is successful, the return value is 0.If pData is not NULL and the function is successful, the return value is the number of bytes copied into pData.
			If there is an error, the return value is (UINT) - 1.*/
			int ret = WinApi.GetRawInputData(hRawInput, DataCommand.RID_INPUT, IntPtr.Zero, ref pbDataSize, Marshal.SizeOf(typeof(RAWINPUTHEADER)));

			if (ret == 0 && pbDataSize == WinApi.GetRawInputData(hRawInput, DataCommand.RID_INPUT, out RAWINPUT rawBuffer, ref pbDataSize, Marshal.SizeOf(typeof(RAWINPUTHEADER))))
			{
				switch ((HeaderDwType)rawBuffer.header.dwType)
				{
					case HeaderDwType.RIM_TYPEKEYBOARD:
						{
							uint keyboardMessage = rawBuffer.data.keyboard.Message;
							bool keyUpOrDown = keyboardMessage == (uint)KeyboardMessages.WM_KEYDOWN || keyboardMessage == (uint)KeyboardMessages.WM_KEYUP;
							
							//Logger.WriteLine($"KEYBOARD. key={rawBuffer.data.keyboard.VKey:x}, message = {rawBuffer.data.keyboard.Message:x}, device pointer = {rawBuffer.header.hDevice}");

							if (!Program.SplitScreenManager.IsRunningInSplitScreen)
							{
								if (keyUpOrDown)
								{
									if (_waitingToSetEndKey)
									{
										_endVKey = rawBuffer.data.keyboard.VKey;
										StopWaitingToSetEndKey();
									}

									LastKeyboardPressed = rawBuffer.header.hDevice;
									break;
								}
							}
							else
							{ 
								if (keyUpOrDown && rawBuffer.data.keyboard.VKey == _endVKey)//End key
								{
									Logger.WriteLine("End key pressed");
									Intercept.InterceptEnabled = false;
									Program.SplitScreenManager.DeactivateSplitScreen();
									InputDisabler.Unlock();//Just in case
								}

								if (keyUpOrDown)
								{
									foreach (Window window in Program.SplitScreenManager.GetWindowsForDevice(rawBuffer.header.hDevice))
									{
										IntPtr hWnd = window.hWnd;
										
										if (Options.CurrentOptions.SendNormalKeyboardInput)
										{
											uint scanCode = rawBuffer.data.keyboard.MakeCode;
											ushort vKey = rawBuffer.data.keyboard.VKey;
											
											bool keyDown = keyboardMessage == (uint)KeyboardMessages.WM_KEYDOWN;

											//uint code = 0x000000000000001 | (scanCode << 16);//32-bit
											uint code = scanCode << 16;//32-bit

											BitArray keysDown = window.keysDown;
											bool stateChangedSinceLast = vKey < keysDown.Length && keyDown != keysDown[vKey];

											if (keyDown)
											{
												//bit 30 : The previous key state. The value is 1 if the key is down before the message is sent, or it is zero if the key is up.
												if (vKey < keysDown.Length && keysDown[vKey])
												{
													code |= 0x40000000;
												}
											}
											else
											{
												code |= 0xC0000000;//WM_KEYUP requires the bit 31 and 30 to be 1
												code |= 0x000000000000001;
											}

											code |= 1;

											if (vKey < keysDown.Length) keysDown[vKey] = keyDown;
											
											if (Options.CurrentOptions.Hook_GetKeyState || Options.CurrentOptions.Hook_GetAsyncKeyState)
											{
												if(stateChangedSinceLast)
												{ 
													window.HooksCPPNamedPipe?.WriteMessage(0x02, vKey, keyDown ? 1 : 0);
												}
											}

											//This also makes GetKeyboardState work, as windows uses the message queue for GetKeyboardState
											SendInput.WinApi.PostMessageA(hWnd, keyboardMessage, (IntPtr)vKey, (UIntPtr)code);
										}

										//Resend raw input to application. Works for some games only
										if (Options.CurrentOptions.SendRawKeyboardInput)
											SendInput.WinApi.PostMessageA(window.borderlands2_DIEmWin_hWnd == IntPtr.Zero ? hWnd : window.borderlands2_DIEmWin_hWnd,
												(uint)SendMessageTypes.WM_INPUT, (IntPtr)0x0000, hRawInput);
									}
								}
							}

							break;
						}
					case HeaderDwType.RIM_TYPEMOUSE:
						{
							RAWMOUSE mouse = rawBuffer.data.mouse;
							IntPtr mouseHandle = rawBuffer.header.hDevice;

							if (!Program.SplitScreenManager.IsRunningInSplitScreen)
							{
								if ((mouse.usButtonFlags & (ushort)ButtonFlags.RI_MOUSE_LEFT_BUTTON_UP) > 0 && Program.Form.ButtonPressed)
								{
									Logger.WriteLine($"Set mouse, handle = {rawBuffer.header.hDevice}");
									Program.SplitScreenManager.SetMouseHandle(rawBuffer.header.hDevice);
								}
								break; 
							}

							var windows = Program.SplitScreenManager.GetWindowsForDevice(mouseHandle);
							for (int windowI = 0; windowI < windows.Length; windowI++)
							{
								Window window = windows[windowI];
								IntPtr hWnd = window.hWnd;

								//Resend raw input to application. Works for some games only
								if (Options.CurrentOptions.SendRawMouseInput)
								{
									SendInput.WinApi.PostMessageA(window.borderlands2_DIEmWin_hWnd == IntPtr.Zero ? hWnd : window.borderlands2_DIEmWin_hWnd, 
										(uint)SendMessageTypes.WM_INPUT, (IntPtr)0x0000, hRawInput);
								}

								IntVector2 mouseVec = window.MousePosition;

								int deltaX = mouse.lLastX;
								int deltaY = mouse.lLastY;

								mouseVec.x = Math.Min(window.Width, Math.Max(mouseVec.x + deltaX, 0));
								mouseVec.y = Math.Min(window.Height, Math.Max(mouseVec.y + deltaY, 0));
								
								if (Options.CurrentOptions.Hook_GetCursorPos)
								{
									window.HooksCPPNamedPipe?.SendMousePosition(deltaX, deltaY, mouseVec.x, mouseVec.y);
								}
								
								long packedXY = mouseVec.y * 0x10000 + mouseVec.x;

								window.UpdateCursorPosition();

								//Mouse buttons.
								ushort f = mouse.usButtonFlags;
								if (f != 0)
								{
									foreach (var pair in _buttonFlagToMouseInputNotifications)
									{
										if ((f & (ushort)pair.Key) > 0)
										{
											(MouseInputNotifications msg, uint wParam, ushort leftMiddleRight, bool isButtonDown, int vKey) = pair.Value;
											//Logger.WriteLine(pair.Key);

											var state = window.MouseState;

											bool oldBtnState = false;
											if (leftMiddleRight == 1)
												oldBtnState = state.l;
											else if (leftMiddleRight == 2)
												oldBtnState = state.r;
											else if (leftMiddleRight == 3)
												oldBtnState = state.m;
											else if (leftMiddleRight == 4)
												oldBtnState = state.x1;
											else if (leftMiddleRight == 5)
												oldBtnState = state.x2;

											if (oldBtnState != isButtonDown)
												SendInput.WinApi.PostMessageA(hWnd, (uint)msg, (IntPtr)wParam, (IntPtr)packedXY);
											
											if (Options.CurrentOptions.Hook_GetAsyncKeyState || Options.CurrentOptions.Hook_GetKeyState && (oldBtnState != isButtonDown))
												window.HooksCPPNamedPipe?.WriteMessage(0x02, vKey, isButtonDown ? 1 : 0);


											if (leftMiddleRight == 1)
											{
												state.l = isButtonDown;

												if (Options.CurrentOptions.RefreshWindowBoundsOnMouseClick)
													window.UpdateBounds();
											}
											else if (leftMiddleRight == 2)
											{
												state.r = isButtonDown;
											}
											else if (leftMiddleRight == 3)
											{
												state.m = isButtonDown;
											}
											else if (leftMiddleRight == 4)
											{
												state.x1 = isButtonDown;
											}
											else if (leftMiddleRight == 5)
											{
												state.x2 = isButtonDown;
											}

											window.MouseState = state;
										}
									}
									
									if (Options.CurrentOptions.SendScrollwheel && (f & (ushort)ButtonFlags.RI_MOUSE_WHEEL) > 0)
									{
										ushort delta = mouse.usButtonData;
										PostMessageA(hWnd, (uint)MouseInputNotifications.WM_MOUSEWHEEL, (IntPtr)((delta * 0x10000) + 0), (IntPtr)packedXY);
									}
								}

								if (Options.CurrentOptions.SendNormalMouseInput)
								{
									ushort mouseMoveState = 0x0000;
									(bool l, bool m, bool r, bool x1, bool x2) = window.MouseState;
									if (l) mouseMoveState |= (ushort)WM_MOUSEMOVE_wParam.MK_LBUTTON;
									if (m) mouseMoveState |= (ushort)WM_MOUSEMOVE_wParam.MK_MBUTTON;
									if (r) mouseMoveState |= (ushort)WM_MOUSEMOVE_wParam.MK_RBUTTON;
									if (x1) mouseMoveState |= (ushort)WM_MOUSEMOVE_wParam.MK_XBUTTON1;
									if (x2) mouseMoveState |= (ushort)WM_MOUSEMOVE_wParam.MK_XBUTTON2;
									mouseMoveState |= 0b10000000;//Signature for USS 
									PostMessageA(hWnd, (uint)MouseInputNotifications.WM_MOUSEMOVE, (IntPtr)mouseMoveState, (IntPtr)packedXY);
								}
							}

							break;
						}
				}
			}

		}
	}
}
