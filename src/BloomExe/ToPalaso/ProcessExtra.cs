using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Reporting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            LogDebugInfo($"DEBUG SafeStartInFront(\"{urlOrCmd}\")");
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
            var windowMap = GetAllWindows();
            Process.Start(urlOrCmd);

            if (Platform.IsLinux && !String.IsNullOrEmpty(libpath))
            {
                Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", libpath);
            }
            // The rest of this happens on a timeout, so that we don't have to sleep and hold things up
            // Passing in a folder path opens window explorer, which doesn't show up in the processlist.
            // But the path appears in the window title, so we can use that to find the window.
            var titleToMatch = urlOrCmd;
            // Many programs show the filename of the file they load.  We can use that to find the window.
            // Don't bother with this if urlOrCmd is a URL, not a file path.  See BL-13008.
            if (!urlOrCmd.Contains("://"))
            {
                var extension = Path.GetExtension(urlOrCmd).ToLowerInvariant();
                switch (extension)
                {
                    case ".xlsx":
                    case ".pdf":
                    case ".txt":
                    case ".doc":
                    case ".csv": // comma-separated values
                    case ".tsv": // tab-separated values (audio timing file)
                    case ".3gp": // audio/video extensions
                    case ".mp4":
                    case ".webm":
                    case ".mp3":
                        titleToMatch = Path.GetFileName(urlOrCmd);
                        break;
                    default:
                        break;
                }
            }
            BringDesiredWindowToFront(titleToMatch, processList, windowMap);
        }

        /// <summary>
        /// Start the file explorer on the parent folder of the given file/folder, selecting the given
        /// file/folder, and moving the file explorer window to the foreground if possible.
        /// </summary>
        public static void ShowFileInExplorerInFront(string path)
        {
            LogDebugInfo($"DEBUG ShowFileInExplorerInFront(\"{path}\")");
            var processList = Process.GetProcesses();
            var windowMap = GetAllWindows();
            PathUtilities.SelectFileInExplorer(path);
            // The rest of this happens on a timeout, so that we don't have to sleep and hold things up
            // Windows explorer doesn't show up in the processlist, but the path appears in the window
            // title, so we can use that to find the window.
            BringDesiredWindowToFront(Path.GetDirectoryName(path), processList, windowMap);
        }

        /// <summary>
        /// Safely start the process when the program code explicitly invokes "xdg-open" (on Linux)
        /// or another command.
        /// </summary>
        public static void SafeStartInFront(string command, string arguments)
        {
            LogDebugInfo($"DEBUG SafeStartInFront(\"{command}\", \"{arguments}\")");
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
            var windowMap = GetAllWindows();
            Process.Start(command, arguments);

            if (Platform.IsLinux && !String.IsNullOrEmpty(libpath))
            {
                Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", libpath);
            }
            // The rest of this happens on a timeout, so that we don't have to sleep and hold things up
            BringDesiredWindowToFront("", processList, windowMap);
        }

        public static void StartInFront(ProcessStartInfo startInfo)
        {
            LogDebugInfo($"DEBUG StartInFront(\"{startInfo}\")");
            var processList = Process.GetProcesses();
            var windowMap = GetAllWindows();
            Process.Start(startInfo);

            // The rest of this happens on a timeout, so that we don't have to sleep and hold things up
            BringDesiredWindowToFront("", processList, windowMap);
        }

        private static void BringDesiredWindowToFront(
            string windowTitleToMatch,
            Process[] oldProcesses,
            Dictionary<IntPtr, string> windowMap
        )
        {
            if (Platform.IsLinux)
                return; // TODO: implement this for Linux.  See CommmonApi.BringFolderToFrontInLinux() for ideas.

            int count = 0;
            var timer = new System.Timers.Timer(100);
            timer.Start();
            timer.Elapsed += (sender, e) =>
            {
                timer.Stop();

                if (!String.IsNullOrEmpty(windowTitleToMatch))
                {
                    IntPtr hWnd = FindNewWindowWithText(windowTitleToMatch, windowMap);
                    if (hWnd != IntPtr.Zero)
                    {
                        LogDebugInfo(
                            $"DEBUG BringDesiredWindowToFront(\"{windowTitleToMatch}\",...) found a new matching window in {count}.1 second(s)"
                        );
                        SetForegroundWindow(hWnd);
                        timer.Dispose();
                        return;
                    }
                }
                var newProcesses = Process.GetProcesses();
                var process = FindNewOrRetitledProcess(oldProcesses, newProcesses);
                if (process != null)
                {
                    LogDebugInfo(
                        $"DEBUG BringDesiredWindowToFront(\"{windowTitleToMatch}\",...) found a new(?) process in {count}.1 second(s) [\"{process.MainWindowTitle}\" / {process.ProcessName}]"
                    );
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
                        LogDebugInfo(
                            $"DEBUG BringDesiredWindowToFront(\"{windowTitleToMatch}\",...) matched {windows.Count} window(s) in {count}.1 second(s)"
                        );
                        SetForegroundWindow(windows[0]);
                        timer.Dispose();
                        return;
                    }
                }

                if (++count > 10)
                {
                    var bldr = new StringBuilder();
                    bldr.Append(
                        $"DEBUG BringDesiredWindowToFront(\"{windowTitleToMatch}\",...): failed to match after 10.1 seconds"
                    );
                    foreach (var proc in newProcesses)
                    {
                        if (!String.IsNullOrEmpty(proc.MainWindowTitle))
                            bldr.Append(
                                $"      process: {proc.ProcessName} [\"{proc.MainWindowTitle}\"]"
                            );
                    }
                    var allWindows = GetAllWindows();
                    foreach (var key in allWindows.Keys)
                    {
                        if (windowMap.ContainsKey(key))
                            continue;
                        var title = allWindows[key];
                        if (!String.IsNullOrEmpty(title))
                            bldr.Append($"      window: {key} [\"{title}\"]");
                    }
                    LogDebugInfo(bldr.ToString());
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

        private static IntPtr FindNewWindowWithText(
            string windowTitleToMatch,
            Dictionary<IntPtr, string> windowMap
        )
        {
            IntPtr hWndFound = IntPtr.Zero;
            EnumWindows(
                delegate(IntPtr hWnd, IntPtr lParam)
                {
                    // Look only at new windows.
                    if (!windowMap.ContainsKey(hWnd))
                    {
                        var title = GetWindowText(hWnd);
                        if (title.Contains(windowTitleToMatch))
                        {
                            hWndFound = hWnd;
                            return false;
                        }
                    }
                    // Return true here so that we iterate all windows until finding one that matches.
                    return true;
                },
                IntPtr.Zero
            );
            return hWndFound;
        }

        private static Process FindNewOrRetitledProcess(
            Process[] oldProcesses,
            Process[] newProcesses
        )
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
                        if (
                            !String.IsNullOrEmpty(newProcess.MainWindowTitle)
                            && !String.IsNullOrEmpty(oldProcess.MainWindowTitle)
                            && newProcess.MainWindowTitle != oldProcess.MainWindowTitle
                        )
                        {
                            LogDebugInfo(
                                $"DEBUG FindNewOrRetitledProcess(): retitled from \"{oldProcess.MainWindowTitle}\" to \"{newProcess.MainWindowTitle}\" [{newProcess.ProcessName}]"
                            );
                            retitledProcess = newProcess;
                        }
                        break;
                    }
                }
                if (!found && newProcess.MainWindowHandle != IntPtr.Zero)
                {
                    LogDebugInfo(
                        $"DEBUG FindNewOrRetitledProcess(): new process \"{newProcess.ProcessName}\" [\"{newProcess.MainWindowTitle}\", {newProcess.MainWindowHandle}]"
                    );
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

            EnumWindows(
                delegate(IntPtr hWnd, IntPtr lParam)
                {
                    if (filter(hWnd, lParam))
                    {
                        // only add the windows that pass the filter
                        windows.Add(hWnd);
                    }
                    // but return true here so that we iterate all windows
                    return true;
                },
                IntPtr.Zero
            );

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
            return FindWindows(
                delegate(IntPtr hWnd, IntPtr lParam)
                {
                    var title = GetWindowText(hWnd);
                    if (title.Contains(titleText))
                    {
                        LogDebugInfo(
                            $"DEBUG FindWindowsWithText(\"{titleText}\") sees \"{title}\""
                        );
                        return true;
                    }
                    return false;
                }
            );
        }

        private static Dictionary<IntPtr, string> GetAllWindows()
        {
            var windows = new Dictionary<IntPtr, string>();
            if (Platform.IsLinux)
                return windows; // TODO: implement this for Linux.
            EnumWindows(
                delegate(IntPtr hWnd, IntPtr lParam)
                {
                    windows[hWnd] = GetWindowText(hWnd);
                    return true;
                },
                IntPtr.Zero
            );
            return windows;
        }

        private static void LogDebugInfo(string message)
        {
            if (
                ApplicationUpdateSupport.IsDevOrAlpha
                || ApplicationUpdateSupport.ChannelName.ToLowerInvariant().Contains("beta")
            )
            {
                Console.WriteLine(message);
                Logger.WriteEvent(message);
            }
        }
    }
}
