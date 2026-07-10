# Keeps every top-level window of one Bloom.exe process on a specific monitor, so E2E runs
# can be confined to a rarely-used screen instead of stealing the developer's workspace.
# Spawned (detached, one per launched instance) by harness/windowPlacement.ts when the
# BLOOM_E2E_SCREEN environment variable is set; exits on its own when the Bloom PID dies.
#
# Why a poll rather than a one-time move: Bloom recreates its main window during
# createCloudTeamCollection's reopen-collection callback (Program.SwitchToCollection closes
# the Shell and opens a new one), shows a splash screen first, and opens WinForms dialogs --
# each a NEW top-level window that would appear on the default monitor. The poll is fast
# (500ms) for the first minute -- when the splash/chooser/dialog churn happens -- then 2s.
#
# DPI awareness is MANDATORY, not a nicety: without it, PowerShell runs DPI-unaware, so on a
# mixed-scaling multi-monitor setup both the screen bounds it reads AND the MoveWindow
# coordinates it passes are virtualized, and windows land on the WRONG monitor (found live,
# 10 Jul 2026: with the primary at >100% scaling, windows targeted at the leftmost screen
# consistently landed one monitor to its right).
param(
    [Parameter(Mandatory = $true)][int]$TargetPid,
    # 1-based, counting screens left-to-right by their X coordinate (see windowPlacement.ts
    # and the README for how to list screens).
    [Parameter(Mandatory = $true)][int]$ScreenIndex,
    # Optional log file; decisions and moves are appended so misplacement is diagnosable.
    [string]$LogPath
)

$ErrorActionPreference = "SilentlyContinue"

function Write-PlacementLog([string]$message) {
    if ($LogPath) {
        try {
            Add-Content -Path $LogPath -Value "$(Get-Date -Format 'HH:mm:ss.fff') $message"
        } catch {}
    }
}

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
    [DllImport("user32.dll")] static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    const int SW_RESTORE = 9;
    const int SW_MAXIMIZE = 3;
    public struct RECT { public int Left, Top, Right, Bottom; }
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // Must run before ANY window/screen API use. Per-monitor-v2 gives physical (unscaled)
    // coordinates for both Screen bounds and MoveWindow on mixed-DPI setups; the fallback
    // (system aware) still beats DPI-unaware virtualization on the primary monitor.
    public static void MakeDpiAware() {
        // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 == (DPI_AWARENESS_CONTEXT)-4
        if (SetProcessDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero)
            SetProcessDPIAware();
    }

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
    // Returns true when a move actually happened (for logging).
    public static bool EnsureOnScreen(IntPtr hWnd, int sx, int sy, int sw, int sh) {
        RECT r;
        if (!GetWindowRect(hWnd, out r)) return false;
        int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
        bool onTarget = cx >= sx && cx < sx + sw && cy >= sy && cy < sy + sh;
        if (onTarget) return false;
        bool wasZoomed = IsZoomed(hWnd);
        if (wasZoomed) ShowWindow(hWnd, SW_RESTORE);
        int w = Math.Min(r.Right - r.Left, sw), h = Math.Min(r.Bottom - r.Top, sh);
        MoveWindow(hWnd, sx, sy, w, h, true);
        if (wasZoomed) ShowWindow(hWnd, SW_MAXIMIZE); // re-maximizes onto the NEW monitor
        return true;
    }
}
"@

# DPI awareness FIRST -- System.Windows.Forms reads screen bounds at first use, so awareness
# must be set before the assembly queries monitors.
[BloomWindowMover]::MakeDpiAware()
Add-Type -AssemblyName System.Windows.Forms

$screens = [System.Windows.Forms.Screen]::AllScreens | Sort-Object { $_.Bounds.X }, { $_.Bounds.Y }
$screenList = ($screens | ForEach-Object { "$($_.DeviceName)@$($_.Bounds.X),$($_.Bounds.Y) $($_.Bounds.Width)x$($_.Bounds.Height)" }) -join "; "
if ($ScreenIndex -lt 1 -or $ScreenIndex -gt $screens.Count) {
    Write-PlacementLog "ERROR: screen index $ScreenIndex out of range (1..$($screens.Count)); screens: $screenList"
    Write-Output "watchWindowScreen: screen index $ScreenIndex out of range (1..$($screens.Count)); exiting."
    exit 1
}
$target = $screens[$ScreenIndex - 1].WorkingArea
Write-PlacementLog "watching PID $TargetPid; target screen #$ScreenIndex = $($screens[$ScreenIndex - 1].DeviceName) workingArea=$($target.X),$($target.Y) $($target.Width)x$($target.Height); all screens: $screenList"

$started = Get-Date
while ($true) {
    $bloom = Get-Process -Id $TargetPid -ErrorAction SilentlyContinue
    if (-not $bloom) { Write-PlacementLog "PID $TargetPid gone; exiting."; exit 0 }
    foreach ($hwnd in [BloomWindowMover]::VisibleTopLevelWindows($TargetPid)) {
        if ([BloomWindowMover]::EnsureOnScreen($hwnd, $target.X, $target.Y, $target.Width, $target.Height)) {
            Write-PlacementLog "moved hwnd $hwnd to screen #$ScreenIndex"
        }
    }
    # Fast polling during the first minute (splash/chooser/dialog churn), gentler afterward.
    if (((Get-Date) - $started).TotalSeconds -lt 60) {
        Start-Sleep -Milliseconds 500
    } else {
        Start-Sleep -Seconds 2
    }
}
