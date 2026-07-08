# Keeps every top-level window of one Bloom.exe process on a specific monitor, so E2E runs
# can be confined to a rarely-used screen instead of stealing the developer's workspace.
# Spawned (detached, one per launched instance) by harness/windowPlacement.ts when the
# BLOOM_E2E_SCREEN environment variable is set; exits on its own when the Bloom PID dies.
#
# Why a poll rather than a one-time move: Bloom recreates its main window during
# createCloudTeamCollection's reopen-collection callback (Program.SwitchToCollection closes
# the Shell and opens a new one), shows a splash screen first, and opens WinForms dialogs --
# each a NEW top-level window that would appear on the default monitor. Re-checking every
# couple of seconds catches all of them.
param(
    [Parameter(Mandatory = $true)][int]$TargetPid,
    # 1-based, counting screens left-to-right by their X coordinate (see windowPlacement.ts
    # and the README for how to list screens).
    [Parameter(Mandatory = $true)][int]$ScreenIndex
)

$ErrorActionPreference = "SilentlyContinue"
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class BloomWindowMover {
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
    const int SW_RESTORE = 9;
    const int SW_MAXIMIZE = 3;
    public struct RECT { public int Left, Top, Right, Bottom; }
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static List<IntPtr> VisibleTopLevelWindows(uint targetPid) {
        var result = new List<IntPtr>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == targetPid && IsWindowVisible(h)) result.Add(h);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    // Moves the window onto the target screen (given by its working-area rectangle) if its
    // center isn't already there. Preserves size (clamped to the screen) and maximized state.
    public static void EnsureOnScreen(IntPtr hWnd, int sx, int sy, int sw, int sh) {
        RECT r;
        if (!GetWindowRect(hWnd, out r)) return;
        int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
        bool onTarget = cx >= sx && cx < sx + sw && cy >= sy && cy < sy + sh;
        if (onTarget) return;
        bool wasZoomed = IsZoomed(hWnd);
        if (wasZoomed) ShowWindow(hWnd, SW_RESTORE);
        int w = Math.Min(r.Right - r.Left, sw), h = Math.Min(r.Bottom - r.Top, sh);
        MoveWindow(hWnd, sx, sy, w, h, true);
        if (wasZoomed) ShowWindow(hWnd, SW_MAXIMIZE); // re-maximizes onto the NEW monitor
    }
}
"@

$screens = [System.Windows.Forms.Screen]::AllScreens | Sort-Object { $_.Bounds.X }, { $_.Bounds.Y }
if ($ScreenIndex -lt 1 -or $ScreenIndex -gt $screens.Count) {
    Write-Output "watchWindowScreen: screen index $ScreenIndex out of range (1..$($screens.Count)); exiting."
    exit 1
}
$target = $screens[$ScreenIndex - 1].WorkingArea

while ($true) {
    $bloom = Get-Process -Id $TargetPid -ErrorAction SilentlyContinue
    if (-not $bloom) { exit 0 }
    foreach ($hwnd in [BloomWindowMover]::VisibleTopLevelWindows($TargetPid)) {
        [BloomWindowMover]::EnsureOnScreen($hwnd, $target.X, $target.Y, $target.Width, $target.Height)
    }
    Start-Sleep -Seconds 2
}
