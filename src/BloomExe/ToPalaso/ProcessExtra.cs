using SIL.PlatformUtilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Bloom.ToPalaso
{
	public class ProcessExtra
	{
		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		private static extern int GetWindowText(IntPtr hWnd, StringBuilder strText, int maxCount);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		private static extern int GetWindowTextLength(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

		[DllImport("user32.dll")]
		public static extern bool SetForegroundWindow(IntPtr hWnd);

		/// <summary>
		/// Safely start the process when the program code merely supplies the URL (or a command).
		/// </summary>
		public static void SafeStartInFront(string urlOrCmd)
		{
			Debug.WriteLine($"DEBUG SafeStartInFront(\"{urlOrCmd}\")");
			// On Linux, we need to temporarily clear the LD_LIBRARY_PATH environment variable
			// so that programs we start don't pick up the wrong version of various libraries.
			string libpath = null;
			if (Platform.IsLinux)
			{
				libpath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
				if (!String.IsNullOrEmpty(libpath))
					Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", null);
			}
			var processList = Process.GetProcesses();
			Process.Start(urlOrCmd);

			if (Platform.IsLinux && !String.IsNullOrEmpty(libpath))
			{
				Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", libpath);
			}
			// The rest of this happens on a timeout, so that we don't have to sleep and hold things up
			// Passing in a folder path opens window explorer, which doesn't show up in the processlist.
			// But the path appears in the window title, so we can use that to find the window.
			BringDesiredWindowToFront(urlOrCmd, processList);
		}

		/// <summary>
		/// Safely start the process when the program code explicitly invokes "xdg-open" (on Linux)
		/// or another command.
		/// </summary>
		public static void SafeStartInFront(string command, string arguments)
		{
			Debug.WriteLine($"DEBUG SafeStartInFront(\"{command}\", \"{arguments}\")");
			// On Linux, we need to temporarily clear the LD_LIBRARY_PATH environment variable
			// so that programs we start don't pick up the wrong version of various libraries.
			string libpath = null;
			if (Platform.IsLinux)
			{
				libpath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
				if (!String.IsNullOrEmpty(libpath))
					Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", null);
			}
			var processList = Process.GetProcesses();
			Process.Start(command, arguments);

			if (Platform.IsLinux && !String.IsNullOrEmpty(libpath))
			{
				Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", libpath);
			}
			// The rest of this happens on a timeout, so that we don't have to sleep and hold things up
			BringDesiredWindowToFront("", processList);
		}

		public static void StartInFront(ProcessStartInfo startInfo)
		{
			var processList = Process.GetProcesses();
			Process.Start(startInfo);

			// The rest of this happens on a timeout, so that we don't have to sleep and hold things up
			BringDesiredWindowToFront("", processList);
		}

		private static void BringDesiredWindowToFront(string windowTitleToMatch, Process[] oldProcesses)
		{
			if (SIL.PlatformUtilities.Platform.IsLinux)
				return; // TODO: implement this for Linux.  See CommmonApi.BringFolderToFrontInLinux() for ideas.

			int count = 0;
			var timer = new System.Timers.Timer(100);
			timer.Start();
			timer.Elapsed += (sender, e) =>
			{
				timer.Stop();
				var newProcesses = Process.GetProcesses();
				var process = FindNewOrRetitledProcess(oldProcesses, newProcesses);
				if (process != null)
				{
					Debug.WriteLine($"DEBUG BringDesiredWindowToFront(\"{windowTitleToMatch}\",oldProcesses) found a new(?) process in {count}.1 second(s) [\"{process.MainWindowTitle}\" / {process.ProcessName}]");
					SetForegroundWindow(process.MainWindowHandle);
					timer.Dispose();
					return;
				}

				if (!String.IsNullOrEmpty(windowTitleToMatch))
				{
					// Note: listing processes instead of windows never lists any file explorer process, and
					// we know that a single file explorer process controls all explorer windows so it's a tossup
					// which would show as the "main window" in any case.
					var windows = FindWindowsWithText(windowTitleToMatch);
					if (windows.Count > 0)
					{
						Debug.WriteLine($"DEBUG BringDesiredWindowToFront(\"{windowTitleToMatch}\",oldProcesses) matched {windows.Count} window(s) in {count}.1 second(s)");
						SetForegroundWindow(windows[0]);
						timer.Dispose();
						return;
					}
				}

				if (++count > 10)
				{
					Debug.WriteLine($"DEBUG BringDesiredWindowToFront(\"{windowTitleToMatch}\",oldProcesses): failed to match after 10.1 seconds");
					foreach (var proc in newProcesses)
						Debug.WriteLine($"    process: {proc.ProcessName} [\"{proc.MainWindowTitle}\"]");
					timer.Dispose();
				}
				else
				{
					// Check again in another second.
					timer.Interval = 1000;
					timer.Start();
				}
			};
		}

		private static Process FindNewOrRetitledProcess(Process[] oldProcesses, Process[] newProcesses)
		{
			Process retitledProcess = null;
			foreach (var newProcess in newProcesses)
			{
				bool found = false;
				foreach (var oldProcess in oldProcesses)
				{
					if (newProcess.Id == oldProcess.Id)
					{
						found = true;
						if (!String.IsNullOrEmpty(newProcess.MainWindowTitle) && !String.IsNullOrEmpty(oldProcess.MainWindowTitle) &&
							newProcess.MainWindowTitle != oldProcess.MainWindowTitle)
						{
							Debug.WriteLine($"DEBUG: retitled from \"{oldProcess.MainWindowTitle}\" to \"{newProcess.MainWindowTitle}\" [{newProcess.ProcessName}]");
							retitledProcess = newProcess;
						}
						break;
					}
				}
				if (!found && newProcess.MainWindowHandle != IntPtr.Zero)
				{
					Debug.WriteLine($"DEBUG: new process \"{newProcess.ProcessName}\" [\"{newProcess.MainWindowTitle}\", {newProcess.MainWindowHandle}]");
					return newProcess;
				}
			}
			return retitledProcess;
		}

		// The following code was copied/adapted from
		// https://stackoverflow.com/questions/19867402/how-can-i-use-enumwindows-to-find-windows-with-a-specific-caption-title

		// Delegate to filter which windows to include
		private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

		/// <summary>
		/// Get the text for the window pointed to by hWnd
		/// </summary>
		private static string GetWindowText(IntPtr hWnd)
		{
			int size = GetWindowTextLength(hWnd);
			if (size > 0)
			{
				var builder = new StringBuilder(size + 1);
				GetWindowText(hWnd, builder, builder.Capacity);
				return builder.ToString();
			}
			return String.Empty;
		}

		/// <summary>
		/// Find all windows that match the given filter
		/// </summary>
		/// <param name="filter">
		/// A delegate that returns true for windows that should be returned and false for windows that should not be returned
		/// </param>
		private static List<IntPtr> FindWindows(EnumWindowsProc filter)
		{
			List<IntPtr> windows = new List<IntPtr>();

			EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
			{
				if (filter(hWnd, lParam))
				{
					// only add the windows that pass the filter
					windows.Add(hWnd);
				}
				// but return true here so that we iterate all windows
				return true;
			}, IntPtr.Zero);

			return windows;
		}

		/// <summary>
		/// Find all windows that contain the given title text
		/// </summary>
		/// <param name="titleText">
		/// The text that the window title must contain.
		/// </param>
		private static List<IntPtr> FindWindowsWithText(string titleText)
		{
			return FindWindows(delegate (IntPtr hWnd, IntPtr lParam)
			{
				return GetWindowText(hWnd).Contains(titleText);
			});
		}
	}
}
