# Drive the non-web parts of Bloom over Windows UI Automation: its WinForms dialogs, buttons and
# tabs, and the native OS dialogs it opens (the file picker, a raw MessageBox). No pointer
# movement, no keystrokes, no focus change. Stock Windows PowerShell 5.1; nothing to install.
# CDP only reaches the web content inside Bloom's WebView2s; this is for everything around it.
# See "Driving WinForms and OS dialogs" in SKILL.md for what has been proven and the gotchas.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File .github/skills/bloom-automation/winformsUia.ps1 `
#       <command> -ProcessId <bloomPid> [-Window <autoId|name>] [-Control <autoId|name>] [-Value <text>] [-Depth n] [-TimeoutMs n]
#
# Commands:
#   list      the process's top-level windows
#   windows   every Window element the process owns, including dialogs owned by the Shell (a modal
#             WinForms dialog is a descendant of its owner in UIA, not a child of the desktop)
#   tree      a window's control tree (WebView2 subtrees are pruned; drive those over CDP)
#   select    SelectionItemPattern.Select on a tab item or list item
#   invoke    InvokePattern.Invoke on a button or link
#   setvalue  ValuePattern.SetValue on an edit box (e.g. a file picker's "File name:")
#   close     WindowPattern.Close on a window
#
# -Window and -Control match either the AutomationId (which for WinForms is the control's designer
# Name, e.g. CollectionSettingsDialog, _okButton, _tab) or the visible Name (e.g. "Book Making",
# "OK", "File name:"). Finding the window polls until -TimeoutMs (default 10 s), so a call can
# follow the click that opens the dialog without a separate wait. When several controls share a
# name, the outermost one that supports the command's pattern wins.
#
# Examples:
#   ... windows  -ProcessId 42824
#   ... tree     -ProcessId 42824 -Window CollectionSettingsDialog -Depth 2
#   ... select   -ProcessId 42824 -Window CollectionSettingsDialog -Control "Book Making"
#   ... invoke   -ProcessId 42824 -Window CollectionSettingsDialog -Control _cancelButton
#   ... setvalue -ProcessId 42824 -Window Open -Control "File name:" -Value "C:\full\path\to\image.png"
#   ... invoke   -ProcessId 42824 -Window Open -Control Open
#   ... tree     -ProcessId 42824 -Window Error -Depth 3      # read a MessageBox's text
#   ... invoke   -ProcessId 42824 -Window Error -Control OK
#
# Exit code is 0 on success and 1 with a message on stderr when the window or control is not found
# or does not support the pattern (a WinForms LinkLabel, for one, supports none).
param(
  [Parameter(Mandatory)][ValidateSet('list','windows','tree','select','invoke','setvalue','close')][string]$Command,
  [Parameter(Mandatory)][int]$ProcessId,
  [string]$Window, [string]$Control, [string]$Value, [int]$Depth = 3, [int]$TimeoutMs = 10000
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, UIAutomationClientsideProviders
# WinForms and WebView2 publish their own UIA providers, but plain Win32 controls (the OS file
# picker, a raw MessageBox's OK button) are served by client-side proxies that the managed UIA
# client does NOT load when hosted in PowerShell: without this, every control in a file dialog
# is an inert [Pane] with no patterns. Registering them from PowerShell's own reflection throws
# a NullReferenceException, and so does a void C# wrapper; this exact shape (compiled C#, the
# call inside try/catch, returning a string) is the one that was found to work. Do not simplify
# it without re-running the file-picker proof in SKILL.md.
Add-Type -ReferencedAssemblies UIAutomationClient, UIAutomationTypes, UIAutomationClientsideProviders -TypeDefinition @"
using System.Windows.Automation;
public static class BloomUiaProxies {
  public static string Register() {
    try {
      ClientSettings.RegisterClientSideProviders(UIAutomationClientsideProviders.UIAutomationClientSideProviders.ClientSideProviderDescriptionTable);
      return "registered";
    } catch (System.Exception e) { return "failed: " + e.Message; }
  }
}
"@
$proxyState = [BloomUiaProxies]::Register()
if ($proxyState -ne 'registered') {
  [Console]::Error.WriteLine("Win32 UIA proxies not registered ($proxyState); OS dialogs will show as inert panes.")
}
$ae = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]
$pidCond = New-Object System.Windows.Automation.PropertyCondition($ae::ProcessIdProperty, $ProcessId)

function Describe($el) {
  $c = $el.Current
  $pats = ($el.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName -replace 'PatternIdentifiers.Pattern','' }) -join ','
  "[$($c.ControlType.ProgrammaticName -replace 'ControlType.','')] name='$($c.Name)' autoId='$($c.AutomationId)' class='$($c.ClassName)' enabled=$($c.IsEnabled) patterns=($pats)"
}

function Dump($el, $d) {
  ("  " * $d) + (Describe $el)
  if ($d -ge $Depth) { return }
  # A WebView2 subtree is enormous and is not what this script is for.
  if ($el.Current.ClassName -like 'Chrome_*') { ("  " * ($d+1)) + "...(WebView2 subtree; drive it over CDP)"; return }
  foreach ($k in $el.FindAll($scope::Children, [System.Windows.Automation.Condition]::TrueCondition)) { Dump $k ($d+1) }
}

function ByIdOrName($id) {
  New-Object System.Windows.Automation.OrCondition(
    (New-Object System.Windows.Automation.PropertyCondition($ae::AutomationIdProperty, $id)),
    (New-Object System.Windows.Automation.PropertyCondition($ae::NameProperty, $id)))
}

# Every Window element below $top (a dialog owned by the Shell, a message box owned by a dialog),
# found by walking down ourselves so WebView2 subtrees are skipped rather than crawled.
function OwnedWindows($top) {
  $found = @()
  $queue = New-Object System.Collections.Queue; $queue.Enqueue($top)
  while ($queue.Count) {
    $el = $queue.Dequeue()
    if ($el.Current.ClassName -like 'Chrome_*') { continue }
    foreach ($k in $el.FindAll($scope::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
      if ($k.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) { $found += $k }
      $queue.Enqueue($k)
    }
  }
  $found
}

function FindWindow() {
  if (-not $Window) { throw "This command needs -Window." }
  $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
  do {
    foreach ($top in $ae::RootElement.FindAll($scope::Children, $pidCond)) {
      if ($top.Current.AutomationId -eq $Window -or $top.Current.Name -eq $Window) { return $top }
      foreach ($w in OwnedWindows $top) {
        if ($w.Current.AutomationId -eq $Window -or $w.Current.Name -eq $Window) { return $w }
      }
    }
    Start-Sleep -Milliseconds 200
  } while ((Get-Date) -lt $deadline)
  throw "No window '$Window' in pid $ProcessId within ${TimeoutMs}ms."
}

function FindControl($w, $needsPattern) {
  if (-not $Control) { throw "This command needs -Control." }
  # Walk the tree ourselves so WebView2 subtrees can be skipped; a Descendants search would
  # crawl every DOM node in them. Breadth-first, so the outermost match wins. When the command
  # needs a pattern, a match without it is passed over: in an OS file dialog the "File name:"
  # ComboBox and the Edit inside it share a name, and only the Edit takes a value.
  $queue = New-Object System.Collections.Queue; $queue.Enqueue($w)
  $sawWithoutPattern = $null
  while ($queue.Count) {
    $el = $queue.Dequeue()
    if ($el.Current.ClassName -like 'Chrome_*') { continue }
    foreach ($k in $el.FindAll($scope::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
      if ($k.Current.AutomationId -eq $Control -or $k.Current.Name -eq $Control) {
        $p = $null
        if (-not $needsPattern -or $k.TryGetCurrentPattern($needsPattern, [ref]$p)) { return $k }
        if (-not $sawWithoutPattern) { $sawWithoutPattern = $k }
      }
      $queue.Enqueue($k)
    }
  }
  if ($sawWithoutPattern) { return $sawWithoutPattern }  # let Pattern() explain what it lacks
  throw "No control '$Control' in window '$Window'."
}

function Pattern($el, $pattern, $what) {
  $p = $null
  if (-not $el.TryGetCurrentPattern($pattern, [ref]$p)) {
    $label = if ($el.Current.AutomationId) { $el.Current.AutomationId } else { $el.Current.Name }
    throw "'$label' is a $($el.Current.ControlType.ProgrammaticName -replace 'ControlType.','') and does not support $what; it cannot be driven this way."
  }
  $p
}

try {
  switch ($Command) {
    'list' { foreach ($t in $ae::RootElement.FindAll($scope::Children, $pidCond)) { Describe $t } }
    'windows' {
      foreach ($t in $ae::RootElement.FindAll($scope::Children, $pidCond)) {
        Describe $t
        foreach ($w in OwnedWindows $t) { "  " + (Describe $w) }
      }
    }
    'tree' { Dump (FindWindow) 0 }
    'select' {
      $c = FindControl (FindWindow) ([System.Windows.Automation.SelectionItemPattern]::Pattern)
      (Pattern $c ([System.Windows.Automation.SelectionItemPattern]::Pattern) 'SelectionItemPattern').Select()
      "selected: " + (Describe $c)
    }
    'setvalue' {
      if ($null -eq $Value) { throw "setvalue needs -Value." }
      $c = FindControl (FindWindow) ([System.Windows.Automation.ValuePattern]::Pattern)
      (Pattern $c ([System.Windows.Automation.ValuePattern]::Pattern) 'ValuePattern').SetValue($Value)
      "set: " + (Describe $c) + " value='$Value'"
    }
    'invoke' {
      $c = FindControl (FindWindow) ([System.Windows.Automation.InvokePattern]::Pattern)
      $before = Describe $c
      (Pattern $c ([System.Windows.Automation.InvokePattern]::Pattern) 'InvokePattern').Invoke()
      # Described before invoking: a button that closes its dialog is gone by now.
      "invoked: " + $before
    }
    'close' {
      $w = FindWindow
      $before = Describe $w
      (Pattern $w ([System.Windows.Automation.WindowPattern]::Pattern) 'WindowPattern').Close()
      "closed: " + $before
    }
  }
} catch {
  [Console]::Error.WriteLine($_.Exception.Message)
  exit 1
}
