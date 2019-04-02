﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GetRawInputDataHook
{
    public class ServerInterface : MarshalByRefObject
    {
		private IntPtr allowed_hRawInput = IntPtr.Zero;
		private bool shouldExit = false;
		private IntPtr hWnd;

		public void SetToReleaseHook()
		{
			shouldExit = true;
		}
		
		public bool ShouldReleaseHook()
		{
			return shouldExit;
		}

		public void SetGame_hWnd(IntPtr hWnd)
		{
			this.hWnd = hWnd;
		}

		public IntPtr GetGame_hWnd()
		{
			return hWnd;
		}

		public void IsInstalled(int clientPID)
		{
			Console.WriteLine("Injected hook(s) into process {0}.\r\n", clientPID);
		}

		public void ReportMessage(string message)
		{
			Console.WriteLine(message);
		}

		public void SetAllowed_hRawInput_device(IntPtr hRawInput)
		{
			allowed_hRawInput = hRawInput;
		}

		public IntPtr GetAllowed_hRawInput_device()
		{
			return allowed_hRawInput;
		}

		public void Ping()
		{

		}
	}
}
