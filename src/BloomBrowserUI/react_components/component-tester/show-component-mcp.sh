#!/bin/bash
# Open a component in the Vite harness (MCP-friendly).
# Usage:
#   ./show-component-mcp.sh <modulePath> <exportName>
# Example:
#   ./show-component-mcp.sh ../color-picking/component-tests/colorPickerManualHarness ColorPickerManualHarness
#
# Notes:
# - This does NOT use Playwright.
# - This is the recommended flow when you want chrome-devtools-mcp to be able to see and interact
#   with the same page.

set -euo pipefail

if [ $# -lt 2 ]; then
    echo "Usage: $0 <modulePath> <exportName>" >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MODULE_PATH="$1"
EXPORT_NAME="$2"

VITE_HOST="${MCP_VITE_HOST:-127.0.0.1}"

if ! command -v node >/dev/null 2>&1; then
    echo "node is required (for URL encoding)." >&2
    exit 1
fi

ENCODED_MODULE_PATH="$(node -e "console.log(encodeURIComponent(process.argv[1]))" "$MODULE_PATH")"
ENCODED_EXPORT_NAME="$(node -e "console.log(encodeURIComponent(process.argv[1]))" "$EXPORT_NAME")"

VITE_PORT="${MCP_VITE_PORT:-}"

ensure_component_tester_deps() {
    # If node_modules is present but executable shims are missing (e.g. .bin/vite),
    # starting the dev server will fail with "Command \"vite\" not found".
    # Make the show script self-sufficient by installing deps when needed.
    if (cd "$SCRIPT_DIR" && yarn -s vite --version >/dev/null 2>&1); then
        return 0
    fi

    echo "component-tester dependencies appear to be missing; running yarn install..." >&2
    (cd "$SCRIPT_DIR" && yarn install)
}

start_component_tester_dev_server() {
    local logfile="$SCRIPT_DIR/.mcp-vite-dev.log"
    local pidfile="$SCRIPT_DIR/.mcp-vite-dev.pid"

    # Truncate the log so failures show current output.
    : >"$logfile"

    (cd "$SCRIPT_DIR" && yarn dev >"$logfile" 2>&1) &
    local pid=$!
    echo "$pid" >"$pidfile"

    # Give the process a moment to fail fast (e.g. missing deps, port in use).
    sleep 0.5
    if ! kill -0 "$pid" >/dev/null 2>&1; then
        echo "component-tester dev server failed to start. Log tail:" >&2
        tail -n 80 "$logfile" >&2 || true
        exit 1
    fi
}

find_vite_port() {
        MCP_VITE_HOST="$VITE_HOST" MCP_VITE_PORT="${MCP_VITE_PORT:-}" node - <<'NODE' 2>/dev/null
const host = process.env.MCP_VITE_HOST || "127.0.0.1";
const preferred = process.env.MCP_VITE_PORT ? Number(process.env.MCP_VITE_PORT) : undefined;
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

if [ -z "$VITE_PORT" ]; then
        echo "Starting component-tester dev server..."

    ensure_component_tester_deps
    start_component_tester_dev_server

        # Wait up to ~10s for the server to come up, then re-scan for the actual port.
        for i in 1 2 3 4 5 6 7 8 9 10; do
                VITE_PORT="$(find_vite_port || true)"
                if [ -n "$VITE_PORT" ]; then
                        break
                fi
                sleep 1
        done
fi

if [ -z "$VITE_PORT" ]; then
        echo "Could not find a running component-tester dev server (expected title 'Playwright Test Harness')." >&2
        echo "Try running: (cd $SCRIPT_DIR && yarn dev)" >&2
        exit 1
fi

URL="http://${VITE_HOST}:${VITE_PORT}/?modulePath=${ENCODED_MODULE_PATH}&exportName=${ENCODED_EXPORT_NAME}"


# Open a browser we expect MCP to be able to control.
# On Windows we prefer a dedicated Chromium session with remote debugging enabled.
# - Uses a dedicated profile dir (so we don't interfere with the user's normal browser profile)
# - Uses a stable remote debugging port (so the MCP server can attach reliably)
#
# You can override defaults with:
#   MCP_BROWSER_EXE=msedge.exe   (or chrome.exe)
#   MCP_CHROME_DEBUG_PORT=9223

MCP_BROWSER_EXE="${MCP_BROWSER_EXE:-chrome.exe}"
MCP_CHROME_DEBUG_PORT="${MCP_CHROME_DEBUG_PORT:-9223}"

PROFILE_DIR="$SCRIPT_DIR/.mcp-chrome-profile"
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

MCP_PROFILE_DIR_FOR_NODE="$PROFILE_DIR_FOR_NODE" node -e '
(() => {
    try {
        const fs = require("fs");
        const path = require("path");
        const profileDir = process.env.MCP_PROFILE_DIR_FOR_NODE;
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

# Note: cmd.exe treats '&' as a command separator even inside quotes, so prefer PowerShell.
if command -v powershell.exe >/dev/null 2>&1; then
    MCP_URL="$URL" \
        MCP_PROFILE_DIR="$PROFILE_DIR_FOR_BROWSER" \
        MCP_BROWSER_EXE="$MCP_BROWSER_EXE" \
        MCP_DEBUG_PORT="$MCP_CHROME_DEBUG_PORT" \
        powershell.exe -NoProfile -Command '& {
        $url = $Env:MCP_URL
        $profileDir = $Env:MCP_PROFILE_DIR
        $browserExe = $Env:MCP_BROWSER_EXE
        $debugPort = $Env:MCP_DEBUG_PORT

        $resolvedBrowserExe = (Get-Command $browserExe -ErrorAction SilentlyContinue).Source

        # If the user did not override MCP_BROWSER_EXE (default is chrome.exe),
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
            Write-Error "Could not find browser executable: $browserExe. Set MCP_BROWSER_EXE to chrome.exe or msedge.exe."
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
    cmd.exe /c start "" "$MCP_BROWSER_EXE" "--remote-debugging-port=$MCP_CHROME_DEBUG_PORT" "--user-data-dir=$PROFILE_DIR_FOR_BROWSER" "--auto-open-devtools-for-tabs" "$URL_FOR_CMD" >/dev/null 2>&1
fi

echo ""
echo "This script has opened the scope (MCP-friendly Chromium session) on port $MCP_CHROME_DEBUG_PORT."
echo ""
echo "Do NOT open any browsers."
echo "Use scope-mcp to attach to the already-open tab instead."
echo ""

