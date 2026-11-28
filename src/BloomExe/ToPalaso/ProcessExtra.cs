using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Reporting;

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

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        /// <summary>
        /// Really make this window the foreground window, even though some other application currently
        /// holds the foreground. Windows refuses a plain SetForegroundWindow in that case: the window is
        /// left where it was in the z-order and only its taskbar button flashes. Briefly attaching our
        /// input queue to the foreground window's thread makes Windows treat us as entitled to the
        /// foreground, which is the standard way around that rule.
        ///
        /// Only call this where the user has just asked Bloom to do something that makes its window the
        /// thing they want to look at (BL-16784: switching collections closes Bloom's window and opens a
        /// new one, and whatever was behind Bloom -- Chrome, say -- gets the foreground in between).
        /// Don't use it to pull Bloom in front of an application the user has deliberately moved on to.
        /// </summary>
        public static void ForceWindowToForeground(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !Platform.IsWindows)
                return;
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == hWnd)
                return;
            var ourThread = GetCurrentThreadId();
            var foregroundThread = GetWindowThreadProcessId(foregroundWindow, out _);
            // Attaching a thread to itself fails, and there would be nothing to gain anyway.
            var attached =
                foregroundThread != 0
                && foregroundThread != ourThread
                && AttachThreadInput(ourThread, foregroundThread, true);
            try
            {
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(ourThread, foregroundThread, false);
            }
        }

        /// <summary>
        /// True if the given window belongs to this (Bloom) process. Used to decide whether it is safe
        /// to steal the OS foreground back: if Bloom currently holds it (e.g. an off-screen browser
        /// grabbed it) we may restore the prior window, but if some other application now holds the
        /// foreground the user has moved on and we must not yank focus away from them.
        /// </summary>
        public static bool IsWindowInCurrentProcess(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return false;
            GetWindowThreadProcessId(hWnd, out uint processId);
            return processId == GetCurrentProcessId();
        }

        /// <summary>
        /// Safely start the process when the program code merely supplies the URL (or a command).
        /// </summary>
        public static void SafeStartInFront(string urlOrCmd)
        {
            LogDebugInfo($"DEBUG SafeStartInFront(\"{urlOrCmd}\")");
            var windowMap = GetAllWindows();
            var processList = Process.GetProcesses();

            SIL.Program.Process.SafeStart(urlOrCmd);

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
            if (Platform.IsWindows)
                EnsureSelectedInExplorer(path, new HashSet<IntPtr>(windowMap.Keys));
            // The rest of this happens on a timeout, so that we don't have to sleep and hold things up
            // Windows explorer doesn't show up in the processlist, but the path appears in the window
            // title, so we can use that to find the window.
            BringDesiredWindowToFront(Path.GetDirectoryName(path), processList, windowMap);
        }

        // SVSI_SELECT | SVSI_DESELECTOTHERS | SVSI_ENSUREVISIBLE | SVSI_FOCUSED, for
        // IShellFolderViewDual.SelectItem.
        private const int kSelectOnlyThisItem = 0x1 | 0x4 | 0x8 | 0x10;

        // How long to look for the File Explorer window that shows the item's folder.
        private static readonly TimeSpan kExplorerWindowWait = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Make sure that a File Explorer window showing the folder that holds <paramref name="path"/>
        /// has that item selected, selecting it in the window that was just opened if not.
        ///
        /// SHOpenFolderAndSelectItems selects the item when the folder is already open in a window. When
        /// it has to open a new window, current versions of Windows 11 (seen on 25H2 and on the GitHub
        /// Actions runners) open the folder and select nothing, so the user has to look for the file.
        /// So, on a thread of its own, this waits for the window to show the folder and
        /// selects the item through the window's own view. The window belongs to Explorer, so there is no
        /// event to wait on, and this checks every 100 ms until the item is selected or
        /// kExplorerWindowWait passes. It never changes the selection in a window that was open before
        /// (<paramref name="windowsBefore"/>, the top-level windows that existed before the request):
        /// that may be the user's own, with a selection they want to keep.
        /// </summary>
        private static void EnsureSelectedInExplorer(string path, HashSet<IntPtr> windowsBefore)
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var name = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(name))
                return; // a drive's root has no folder to show it in
            var folder = Path.GetDirectoryName(fullPath)!.TrimEnd(Path.DirectorySeparatorChar);
            var thread = new Thread(() =>
            {
                try
                {
                    dynamic shell = Activator.CreateInstance(
                        Type.GetTypeFromProgID("Shell.Application", true)
                    );
                    var deadline = DateTime.UtcNow + kExplorerWindowWait;
                    do
                    {
                        if (
                            SelectInOpenExplorerWindow(shell, folder, name, fullPath, windowsBefore)
                        )
                        {
                            LogDebugInfo(
                                $"DEBUG EnsureSelectedInExplorer(\"{fullPath}\"): the File Explorer window has it selected"
                            );
                            return;
                        }
                        Thread.Sleep(100);
                    } while (DateTime.UtcNow < deadline);
                    LogDebugInfo(
                        $"DEBUG EnsureSelectedInExplorer(\"{fullPath}\"): no File Explorer window showed it selected within {kExplorerWindowWait.TotalSeconds} seconds"
                    );
                }
                catch (Exception e)
                {
                    // Explorer still shows the folder; only the selection is missing.
                    Logger.WriteError($"EnsureSelectedInExplorer(\"{fullPath}\") failed", e);
                }
            });
            // Shell.Application and the views it hands out are apartment-threaded.
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        /// <summary>
        /// Look through the open File Explorer windows for ones showing <paramref name="folder"/>.
        /// Returns true when one of them has <paramref name="name"/> (at <paramref name="fullPath"/>)
        /// selected; otherwise asks each such window that is not one of
        /// <paramref name="windowsBefore"/> to select it, which a later call sees. A window that throws
        /// (still loading, or closed meanwhile) is left for the next call.
        /// </summary>
        private static bool SelectInOpenExplorerWindow(
            dynamic shell,
            string folder,
            string name,
            string fullPath,
            HashSet<IntPtr> windowsBefore
        )
        {
            foreach (dynamic window in shell.Windows())
            {
                try
                {
                    dynamic view = window.Document;
                    string windowFolder = view.Folder.Self.Path;
                    if (
                        !string.Equals(
                            windowFolder?.TrimEnd(Path.DirectorySeparatorChar),
                            folder,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        continue;
                    if (IsSelectedInView(view, fullPath))
                        return true;
                    if (windowsBefore.Contains(new IntPtr((long)window.HWND)))
                        continue;
                    dynamic item = view.Folder.ParseName(name);
                    if (item != null) // null until the view has listed the item
                        view.SelectItem(item, kSelectOnlyThisItem);
                }
                catch (Exception)
                {
                    // not a folder view (Internet Explorer, say), not ready yet, or closed meanwhile
                }
            }
            return false;
        }

        /// <summary>
        /// True if the File Explorer folder view <paramref name="view"/> has the item at
        /// <paramref name="fullPath"/> among its selected items.
        /// </summary>
        private static bool IsSelectedInView(dynamic view, string fullPath)
        {
            foreach (dynamic selected in view.SelectedItems())
            {
                string selectedPath = selected.Path;
                if (string.Equals(selectedPath, fullPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Safely start the process when the program code explicitly invokes "xdg-open" (on Linux)
        /// or another command.
        /// </summary>
        public static void SafeStartInFront(string command, string arguments)
        {
            LogDebugInfo($"DEBUG SafeStartInFront(\"{command}\", \"{arguments}\")");
            var windowMap = GetAllWindows();
            var processList = Process.GetProcesses();
            SIL.Program.Process.SafeStart(command, arguments);
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
