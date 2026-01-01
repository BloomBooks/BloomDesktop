#!/bin/bash
# Open a component in the Vite harness in a shared, remote-debugging-enabled browser session.
# Usage:
#   ./show-scope.sh <modulePath> <exportName>
# Example:
#   ./show-scope.sh ../color-picking/component-tests/colorPickerManualHarness ColorPickerManualHarness
#
# Notes:
# - This does NOT use Playwright.
# - This is the recommended flow when you want external tools to attach to the same tab via the
#   Chrome DevTools Protocol (remote debugging).

set -euo pipefail

cleanup() {
  # Kill any background jobs started by this script (e.g., open_scope_browser)
  jobs -pr | xargs -r kill 2>/dev/null || true

  # If a detached server was started earlier, stop it via Windows PID file.
  local pidfile="$SCRIPT_DIR/.scope-vite-dev.pid"
  if [ -f "$pidfile" ] && command -v taskkill.exe >/dev/null 2>&1; then
    local wp
    wp="$(tr -d '\r\n' < "$pidfile")"
    if [ -n "$wp" ]; then
      taskkill.exe //PID "$wp" //T //F >/dev/null 2>&1 || true
    fi
  fi
}

trap 'cleanup; exit 130' INT TERM


DETACH="${SCOPE_DETACH:-0}"

while [ $# -gt 0 ]; do
    case "$1" in
        --detach)
            DETACH=1
            shift
            ;;
        --help|-h)
            echo "Usage: $0 [--detach] <modulePath> <exportName>" >&2
            exit 0
            ;;
        --)
            shift
            break
            ;;
        -*)
            echo "Unknown option: $1" >&2
            echo "Usage: $0 [--detach] <modulePath> <exportName>" >&2
            exit 1
            ;;
        *)
            break
            ;;
    esac
done

if [ $# -lt 2 ]; then
    echo "Usage: $0 [--detach] <modulePath> <exportName>" >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MODULE_PATH="$1"
EXPORT_NAME="$2"

VITE_HOST="${SCOPE_VITE_HOST:-127.0.0.1}"

if ! command -v node >/dev/null 2>&1; then
    echo "node is required (for URL encoding)." >&2
    exit 1
fi

ENCODED_MODULE_PATH="$(node -e "console.log(encodeURIComponent(process.argv[1]))" "$MODULE_PATH")"
ENCODED_EXPORT_NAME="$(node -e "console.log(encodeURIComponent(process.argv[1]))" "$EXPORT_NAME")"

VITE_PORT="${SCOPE_VITE_PORT:-}"

FOUND_EXISTING_SERVER=0
SHOULD_START_SERVER=0

ensure_component_tester_deps() {
    # If node_modules is present but executable shims are missing (e.g. .bin/vite),
    # starting the dev server will fail with "Command \"vite\" not found".
    # Fail fast and tell the user how to fix it.
    if (cd "$SCRIPT_DIR" && yarn -s vite --version >/dev/null 2>&1); then
        return 0
    fi

    echo "component-tester dependencies appear to be missing." >&2
    echo "Fix: (cd \"$SCRIPT_DIR\" && yarn install)" >&2
    exit 1
}

get_windows_listener_pid() {
    local port="$1"
    if ! command -v powershell.exe >/dev/null 2>&1; then
        return 0
    fi

    # On Windows, netstat shows a Windows PID, but backgrounding from Git Bash yields an MSYS PID.
    # Use Get-NetTCPConnection when available to get the Windows PID directly.
    powershell.exe -NoProfile -Command "& {
        try {
            $c = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction Stop | Select-Object -First 1
            if ($c) { Write-Output $c.OwningProcess }
        } catch { }
    }" 2>/dev/null | tr -d '\r' || true
}

start_component_tester_dev_server_detached() {
    local port="$1"
    local host="$2"
    local logfile_unix="$SCRIPT_DIR/.scope-vite-dev.log"
    local pidfile_unix="$SCRIPT_DIR/.scope-vite-dev.pid"

    ensure_component_tester_deps

    : >"$logfile_unix"

    local script_dir_win="$SCRIPT_DIR"
    local logfile_win="$logfile_unix"
    if command -v cygpath >/dev/null 2>&1; then
        script_dir_win="$(cygpath -w "$SCRIPT_DIR")"
        logfile_win="$(cygpath -w "$logfile_unix")"
    fi

    if ! command -v powershell.exe >/dev/null 2>&1; then
        echo "powershell.exe is required to start the dev server in detached mode on Windows." >&2
        exit 1
    fi

    # Start Vite in the background and capture the Windows PID (so the user can kill it).
    local windows_pid
    windows_pid="$(powershell.exe -NoProfile -Command "& {
        $wd = '${script_dir_win}'
        $out = '${logfile_win}'
        $err = '${logfile_win}'
        $args = @('dev','--','--host','${host}','--port','${port}','--strictPort')
        $p = Start-Process -FilePath 'yarn' -ArgumentList $args -WorkingDirectory $wd -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
        Write-Output $p.Id
    }" | tr -d '\r')"

    if [ -z "$windows_pid" ]; then
        echo "component-tester dev server failed to start (no PID returned)." >&2
        echo "Log tail:" >&2
        tail -n 80 "$logfile_unix" >&2 || true
        exit 1
    fi

    echo "$windows_pid" >"$pidfile_unix"
}

find_vite_port() {
    SCOPE_VITE_HOST="$VITE_HOST" SCOPE_VITE_PORT="${SCOPE_VITE_PORT:-}" node - <<'NODE' 2>/dev/null
const host = process.env.SCOPE_VITE_HOST || "127.0.0.1";
const preferred = process.env.SCOPE_VITE_PORT ? Number(process.env.SCOPE_VITE_PORT) : undefined;
const ports = preferred ? [preferred] : Array.from({ length: 20 }, (_, i) => 5183 + i);

const isHarness = async (port) => {
    try {
        const res = await fetch(`http://${host}:${port}/`, { cache: "no-store" });
        if (!res.ok) return false;
        const text = await res.text();
        return text.includes("Playwright Test Harness");
    } catch {
        return false;
    }
};

(async () => {
    for (const port of ports) {
        if (await isHarness(port)) {
            console.log(String(port));
            process.exit(0);
        }
    }
    process.exit(1);
})();
NODE
}

if [ -z "$VITE_PORT" ]; then
        VITE_PORT="$(find_vite_port || true)"
fi

if [ -n "$VITE_PORT" ]; then
    FOUND_EXISTING_SERVER=1
else
    SHOULD_START_SERVER=1
    VITE_PORT="${SCOPE_VITE_PORT:-5183}"
fi

if [ -z "$VITE_PORT" ]; then
        echo "Could not find a running component-tester dev server (expected title 'Playwright Test Harness')." >&2
        echo "Try running: (cd $SCRIPT_DIR && yarn dev)" >&2
        exit 1
fi

URL="http://${VITE_HOST}:${VITE_PORT}/?modulePath=${ENCODED_MODULE_PATH}&exportName=${ENCODED_EXPORT_NAME}"

open_scope_browser() {
    local wait_for_harness="$1"

    # Note: cmd.exe treats '&' as a command separator even inside quotes, so prefer PowerShell.
    if command -v powershell.exe >/dev/null 2>&1; then
        SCOPE_URL="$URL" \
            SCOPE_PROFILE_DIR="$PROFILE_DIR_FOR_BROWSER" \
            SCOPE_BROWSER_EXE="$SCOPE_BROWSER_EXE" \
            SCOPE_DEBUG_PORT="$SCOPE_CHROME_DEBUG_PORT" \
            SCOPE_WAIT_FOR_HARNESS="$wait_for_harness" \
            powershell.exe -NoProfile -Command '& {
            $url = $Env:SCOPE_URL
            $profileDir = $Env:SCOPE_PROFILE_DIR
            $browserExe = $Env:SCOPE_BROWSER_EXE
            $debugPort = $Env:SCOPE_DEBUG_PORT
            $waitForHarness = $Env:SCOPE_WAIT_FOR_HARNESS

            if ($waitForHarness -eq "1") {
                $deadline = (Get-Date).AddSeconds(20)
                while ((Get-Date) -lt $deadline) {
                    try {
                        $res = Invoke-WebRequest -UseBasicParsing -Uri $url -Headers @{ "Cache-Control" = "no-store" }
                        if ($res.StatusCode -eq 200 -and $res.Content -like "*Playwright Test Harness*") {
                            break
                        }
                    } catch { }
                    Start-Sleep -Milliseconds 250
                }
            }

            $resolvedBrowserExe = (Get-Command $browserExe -ErrorAction SilentlyContinue).Source

            # If the user did not override SCOPE_BROWSER_EXE (default is chrome.exe),
            # prefer Chrome Beta when installed.
            $candidates = @()
            if ($browserExe -ieq "chrome.exe") {
                $candidates += @(
                    "${Env:ProgramFiles}\\Google\\Chrome Beta\\Application\\chrome.exe",
                    "${Env:ProgramFiles(x86)}\\Google\\Chrome Beta\\Application\\chrome.exe",
                    "${Env:LocalAppData}\\Google\\Chrome Beta\\Application\\chrome.exe",
                    $resolvedBrowserExe,
                    "${Env:ProgramFiles}\\Google\\Chrome\\Application\\chrome.exe",
                    "${Env:ProgramFiles(x86)}\\Google\\Chrome\\Application\\chrome.exe",
                    "${Env:LocalAppData}\\Google\\Chrome\\Application\\chrome.exe",
                    "${Env:ProgramFiles}\\Microsoft\\Edge\\Application\\msedge.exe",
                    "${Env:ProgramFiles(x86)}\\Microsoft\\Edge\\Application\\msedge.exe",
                    "${Env:LocalAppData}\\Microsoft\\Edge\\Application\\msedge.exe"
                )
            } else {
                $candidates += @(
                    $resolvedBrowserExe,
                    "${Env:ProgramFiles}\\Google\\Chrome Beta\\Application\\chrome.exe",
                    "${Env:ProgramFiles(x86)}\\Google\\Chrome Beta\\Application\\chrome.exe",
                    "${Env:LocalAppData}\\Google\\Chrome Beta\\Application\\chrome.exe",
                    "${Env:ProgramFiles}\\Google\\Chrome\\Application\\chrome.exe",
                    "${Env:ProgramFiles(x86)}\\Google\\Chrome\\Application\\chrome.exe",
                    "${Env:LocalAppData}\\Google\\Chrome\\Application\\chrome.exe",
                    "${Env:ProgramFiles}\\Microsoft\\Edge\\Application\\msedge.exe",
                    "${Env:ProgramFiles(x86)}\\Microsoft\\Edge\\Application\\msedge.exe",
                    "${Env:LocalAppData}\\Microsoft\\Edge\\Application\\msedge.exe"
                )
            }

            $candidates = $candidates | Where-Object { $_ -and (Test-Path $_) }

            $browserPath = $candidates | Select-Object -First 1
            if (-not $browserPath) {
                Write-Error "Could not find browser executable: $browserExe. Set SCOPE_BROWSER_EXE to chrome.exe or msedge.exe."
                exit 1
            }

            $args = @(
                "--remote-debugging-port=$debugPort",
                "--user-data-dir=$profileDir",
                "--auto-open-devtools-for-tabs",
                "--no-first-run",
                "--no-default-browser-check",
                $url
            )

            Start-Process -FilePath $browserPath -ArgumentList $args | Out-Null
        }'
    elif command -v cmd.exe >/dev/null 2>&1; then
        # Fallback: attempt to open Chrome by name (may still work if chrome.exe is on PATH)
        URL_FOR_CMD="${URL//^/^^}"
        URL_FOR_CMD="${URL_FOR_CMD//&/^&}"
        cmd.exe /c start "" "$SCOPE_BROWSER_EXE" "--remote-debugging-port=$SCOPE_CHROME_DEBUG_PORT" "--user-data-dir=$PROFILE_DIR_FOR_BROWSER" "--auto-open-devtools-for-tabs" "$URL_FOR_CMD" >/dev/null 2>&1
    fi
}


# Open a browser we expect external tools to be able to control.
# On Windows we prefer a dedicated Chromium session with remote debugging enabled.
# - Uses a dedicated profile dir (so we don't interfere with the user's normal browser profile)
# - Uses a stable remote debugging port (so tools can attach reliably)
#
# You can override defaults with:
#   SCOPE_BROWSER_EXE=msedge.exe   (or chrome.exe)
#   SCOPE_CHROME_DEBUG_PORT=9223

SCOPE_BROWSER_EXE="${SCOPE_BROWSER_EXE:-chrome.exe}"
SCOPE_CHROME_DEBUG_PORT="${SCOPE_CHROME_DEBUG_PORT:-9223}"

# Use a temp profile directory (Chrome creates lots of files in a user-data-dir).
# Keep it out of the repo, and stable between runs to avoid creating a new profile every time.
PROFILE_DIR="$(node -e '(() => { const fs = require("fs"); const os = require("os"); const path = require("path"); const dir = path.join(os.tmpdir(), "bloom-scope-chrome-profile"); fs.mkdirSync(dir, { recursive: true }); console.log(dir); })()')"
if command -v cygpath >/dev/null 2>&1; then
    PROFILE_DIR="$(cygpath -u "$PROFILE_DIR")"
fi

PROFILE_DIR_FOR_BROWSER="$PROFILE_DIR"
if command -v cygpath >/dev/null 2>&1; then
    PROFILE_DIR_FOR_BROWSER="$(cygpath -w "$PROFILE_DIR")"
fi

# Try to make DevTools open on the Console panel.
# Chrome stores this as a DevTools preference in the user profile.
# There is no stable command-line switch to pick a specific DevTools tab.
PROFILE_DIR_FOR_NODE="$PROFILE_DIR"
if command -v cygpath >/dev/null 2>&1; then
        PROFILE_DIR_FOR_NODE="$(cygpath -w "$PROFILE_DIR")"
fi

SCOPE_PROFILE_DIR_FOR_NODE="$PROFILE_DIR_FOR_NODE" node -e '
(() => {
    try {
        const fs = require("fs");
        const path = require("path");
        const profileDir = process.env.SCOPE_PROFILE_DIR_FOR_NODE;
        if (!profileDir) return;

        const defaultDir = path.join(profileDir, "Default");
        const prefsPath = path.join(defaultDir, "Preferences");
        const desired = JSON.stringify("console"); // "\"console\""

        let obj = {};
        if (fs.existsSync(prefsPath)) {
            obj = JSON.parse(fs.readFileSync(prefsPath, "utf8"));
        } else {
            fs.mkdirSync(defaultDir, { recursive: true });
        }

        obj.devtools = obj.devtools || {};
        obj.devtools.preferences = obj.devtools.preferences || {};

        if (obj.devtools.preferences["panel-selected-tab"] !== desired) {
            obj.devtools.preferences["panel-selected-tab"] = desired;
            fs.writeFileSync(prefsPath, JSON.stringify(obj));
        }
    } catch {
        // Ignore; failing here should not prevent launching the browser.
    }
})();
' || true

if [ "$SHOULD_START_SERVER" = "1" ] && [ "$DETACH" != "1" ]; then
    # Start the browser opener in the background; it will wait for the harness to be ready.
    open_scope_browser 1 &

    echo ""
    echo "Starting component-tester dev server in this terminal on http://${VITE_HOST}:${VITE_PORT}/"
    echo "(Press Ctrl+C to stop it.)"

    ensure_component_tester_deps
    # (cd "$SCRIPT_DIR" && yarn dev -- --host "$VITE_HOST" --port "$VITE_PORT" --strictPort)
    # exit 0
    cd "$SCRIPT_DIR"
    exec yarn dev -- --host "$VITE_HOST" --port "$VITE_PORT" --strictPort
fi

if [ "$SHOULD_START_SERVER" = "1" ] && [ "$DETACH" = "1" ]; then
    echo "Starting component-tester dev server in the background..."
    start_component_tester_dev_server_detached "$VITE_PORT" "$VITE_HOST"
fi

if [ "$FOUND_EXISTING_SERVER" = "1" ]; then
    existing_pid="$(get_windows_listener_pid "$VITE_PORT")"
    if [ -n "$existing_pid" ]; then
        echo "Found an already-running component-tester dev server on http://${VITE_HOST}:${VITE_PORT}/ (PID $existing_pid)."
    else
        echo "Found an already-running component-tester dev server on http://${VITE_HOST}:${VITE_PORT}/"
    fi
fi

open_scope_browser 0

echo ""
echo "This script has opened the scope on port $SCOPE_CHROME_DEBUG_PORT (remote debugging enabled)."
echo ""
echo "Do NOT open any browsers."
echo "Use a DevTools-protocol tool to attach to the already-open tab instead."
echo ""

