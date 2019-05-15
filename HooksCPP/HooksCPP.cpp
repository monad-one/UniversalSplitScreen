#include "stdafx.h"
#include <easyhook.h>
#include <string>
#include <iostream>
#include <Xinput.h>
#include <thread>
#include <mutex>
#include <condition_variable>
using namespace std;


HWND hWnd = 0;
string _ipcChannelName;
static int x;
static int y;
UINT16 vkey_state;
int controllerIndex = 0;

BOOL WINAPI GetCursorPos_Hook(LPPOINT lpPoint)
{
	POINT p = POINT();
	p.x = x;
	p.y = y;
	ClientToScreen(hWnd, &p);
	*lpPoint = p;
	return true;
}

HWND WINAPI GetForegroundWindow_Hook()
{
	return hWnd;
}

inline int getBitShiftForVKey(int VKey)
{
	int shift = 0;
	if (VKey <= 6)
	{
		return VKey - 1;
	}
	else
	{
		switch (VKey)
		{
			case 0x41: return 6;
			case 0x44: return 7;
			case 0x53: return 8;
			case 0x57: return 9;
			default: return 10;
		}
	}
}

SHORT WINAPI GetAsyncKeyState_Hook(int vKey)
{
	return (vkey_state & (1 << getBitShiftForVKey(vKey))) == 0 ? 0 : 0b1000000000000000;
}

SHORT WINAPI GetKeyState_Hook(int nVirtKey)
{
	if (nVirtKey == 0x41 || nVirtKey == 0x44 || nVirtKey == 0x53 || nVirtKey == 0x57)//WASD
	{
		return (vkey_state & (1 << getBitShiftForVKey(nVirtKey))) == 0 ? 0 : 0b1000000000000000;
	}
	else
	{
		return GetKeyState(nVirtKey);
	}
}

LRESULT WINAPI CallWindowProc_Hook(WNDPROC lpPrevWndFunc, HWND hWnd, UINT Msg, WPARAM wParam, LPARAM lParam)
{
	/*ofstream logging;
	logging.open("C:\\Projects\\UniversalSplitScreen\\UniversalSplitScreen\\bin\\x86\\Debug\\HooksCPP_Output.txt", std::ios_base::app);
	logging << "Received msg = "<< Msg << endl;
	logging.close();*/

	//USS signature is 1 << 7 or 0b10000000 for WM_MOUSEMOVE(0x0200). If this is detected, allow event to pass
	if (Msg == WM_MOUSEMOVE && ((int)wParam & 0b10000000) > 0)
		return CallWindowProc(lpPrevWndFunc, hWnd, Msg, wParam, lParam);

	// || Msg == 0x00FF
	else if ((Msg >= WM_XBUTTONDOWN && Msg <= WM_XBUTTONDBLCLK) || Msg == WM_MOUSEMOVE || Msg == WM_MOUSEACTIVATE || Msg == WM_MOUSEHOVER || Msg == WM_MOUSELEAVE || Msg == WM_MOUSEWHEEL || Msg == WM_SETCURSOR)//Other mouse events. 
		return 0;
	else
	{
		if (Msg == WM_ACTIVATE) //0x0006 is WM_ACTIVATE, which resets the mouse position for starbound [citation needed]
			return CallWindowProc(lpPrevWndFunc, hWnd, Msg, 1, 0);
		else
			return CallWindowProc(lpPrevWndFunc, hWnd, Msg, wParam, lParam);
	}
}

BOOL WINAPI SetCursorPos_Hook(int X, int Y)
{
	POINT p;
	p.x = X;
	p.y = Y;

	ScreenToClient(hWnd, &p);

	x = p.x;
	y = p.y;

	return TRUE;
}

BOOL WINAPI RegisterRawInputDevices_Hook(PCRAWINPUTDEVICE pRawInputDevices, UINT uiNumDevices, UINT cbSize)
{
	return true;
}

DWORD WINAPI XInputGetState_Hook(DWORD dwUserIndex, XINPUT_STATE *pState)
{
	if (controllerIndex == 0)
		return ERROR_SUCCESS;
	else
		return XInputGetState(controllerIndex - 1, pState);
}

inline int bytesToInt(BYTE* bytes)
{
	return (int)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
}

void startPipeListen()
{	
	char _pipeNameChars[256];
	sprintf_s(_pipeNameChars, "\\\\.\\pipe\\%s", _ipcChannelName.c_str());

	HANDLE pipe = CreateFile(
		_pipeNameChars,
		GENERIC_READ,
		FILE_SHARE_READ | FILE_SHARE_WRITE,
		NULL,
		OPEN_EXISTING,
		FILE_ATTRIBUTE_NORMAL,
		NULL
	);

	if (pipe == INVALID_HANDLE_VALUE)
	{
		cout << "Failed to connect to pipe\n";
		return;
	}

	cout << "Connected to pipe\n";

	for (;;)
	{
		BYTE buffer[9];
		DWORD bytesRead = 0;

		BOOL result = ReadFile(
			pipe,
			buffer,
			9 * sizeof(BYTE),
			&bytesRead,
			NULL
		);

		if (result && bytesRead == 9)
		{
			int param1 = bytesToInt(&buffer[1]);

			int param2 = bytesToInt(&buffer[5]);

			//cout << "Received message. Msg=" << (int)buffer[0] << ", param1=" << param1 << ", param2=" << param2 << "\n";

			switch (buffer[0])
			{
				case 0x01:
				{
					x = param1;
					y = param2;
					break;
				}
				case 0x02:
				{
					UINT16 shift = (1 << getBitShiftForVKey(param1));
					if (param2 == 0)//Button up
					{
						vkey_state &= (~shift);//Sets to 0
					}
					else//Button down
					{
						vkey_state |= shift;//Sets to 1
					}
					break;
				}
				case 0x03:
				{
					cout << "Received pipe closed message. Closing pipe..." << endl;
					return;
				}
				default:
				{
					break;
				}
			}
		}
		else
		{
			//cout << "Failed to read message\n";
		}
	}
}

NTSTATUS installHook(LPCSTR moduleHandle, LPCSTR lpProcName, void* InCallback)
{
	HOOK_TRACE_INFO hHook = { NULL };

	NTSTATUS hookResult = LhInstallHook(
		GetProcAddress(GetModuleHandle(moduleHandle), lpProcName),
		InCallback,
		NULL,
		&hHook);

	if (!FAILED(hookResult))
	{
		ULONG ACLEntries[1] = { 0 };
		LhSetExclusiveACL(ACLEntries, 1, &hHook);
		cout << "Successfully installed hook " << lpProcName << " in module '" << moduleHandle << "'\n";
	}
	else
	{
		cout << "Failed to install hook " << lpProcName << " in module '"<< moduleHandle << "', NTSTATUS: " << hookResult << "\n";
	}

	return hookResult;
}

struct UserData
{
	HWND hWnd;
	char ipcChannelName[256];//Name will be 30 characters
	int controllerIndex;
	bool HookGetCursorPos;
	bool HookGetForegroundWindow;
	bool HookGetAsyncKeyState;
	bool HookGetKeyState;
	bool HookCallWindowProcW;
	bool HookRegisterRawInputDevices;
	bool HookSetCursorPos;
	bool HookXInput;
};

extern "C" __declspec(dllexport) void __stdcall NativeInjectionEntryPoint(REMOTE_ENTRY_INFO* inRemoteInfo)
{
	//Cout will go to the games console
	cout << "Injected CPP\n";
	cout << "Injected by host process ID: " << inRemoteInfo->HostPID << "\n";
	cout << "Passed in data size:" << inRemoteInfo->UserDataSize << "\n";
	
	if (inRemoteInfo->UserDataSize == sizeof(UserData))
	{
		//Get UserData
		UserData userData = *reinterpret_cast<UserData *>(inRemoteInfo->UserData);

		hWnd = userData.hWnd;
		cout << "Received hWnd: " << hWnd << endl;

		string ipcChannelName(userData.ipcChannelName);
		_ipcChannelName = ipcChannelName;
		cout << "Received IPC channel: " << ipcChannelName << endl;

		controllerIndex = userData.controllerIndex;
		cout << "Received controller index: " << controllerIndex << endl;
		
		//Install hooks
		if (userData.HookGetCursorPos)				installHook(TEXT("user32"),	"GetCursorPos",				GetCursorPos_Hook);
		if (userData.HookGetForegroundWindow)		installHook(TEXT("user32"),	"GetForegroundWindow",		GetForegroundWindow_Hook);
		if (userData.HookGetAsyncKeyState)			installHook(TEXT("user32"), "GetAsyncKeyState",			GetAsyncKeyState_Hook);
		if (userData.HookGetKeyState)				installHook(TEXT("user32"), "GetKeyState",				GetKeyState_Hook);
		if (userData.HookCallWindowProcW)			installHook(TEXT("user32"), "CallWindowProcW",			CallWindowProc_Hook);
		if (userData.HookRegisterRawInputDevices)	installHook(TEXT("user32"), "RegisterRawInputDevices",	RegisterRawInputDevices_Hook);
		if (userData.HookSetCursorPos)				installHook(TEXT("user32"), "SetCursorPos",				SetCursorPos_Hook);

		//Hook XInput dll
		if (userData.HookXInput)
		{
			LPCSTR xinputNames[] = { "xinput1_3.dll", "xinput1_4.dll", "xinput1_2.dll", "xinput1_1.dll", "xinput9_1_0.dll" };//todo: switch 1_3/1_4?
			NTSTATUS ntResult = 1;//0 = success
			int xi = 0;
			while (ntResult != 0)
			{
				ntResult = installHook(xinputNames[xi++], "XInputGetState", XInputGetState_Hook);
			}
		}

		//De-register from Raw Input
		if (userData.HookRegisterRawInputDevices)
		{
			RAWINPUTDEVICE rid[1];
			rid[0].usUsagePage = 0x01;
			rid[0].usUsage = 0x02;
			rid[0].dwFlags = RIDEV_REMOVE;
			rid[0].hwndTarget = NULL;

			BOOL unregisterSuccess = RegisterRawInputDevices(rid, 4, sizeof(rid[0]));
			cout << "Raw mouse input unregister success: " << unregisterSuccess << endl;
		}
		
		//Start named pipe client
		startPipeListen();
	}
	else
	{
		cout << "Failed getting user data\n";
	}

	return;
}