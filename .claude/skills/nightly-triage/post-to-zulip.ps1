# Posts the nightly-triage summary to Zulip, in the same channel and topic as the nightly's own
# report, so the triage reads as a reply to it.
#
# The credentials live in the developer's ~/.zuliprc, the file Zulip's own tools use:
#
#   [api]
#   email=<the bot's email>
#   key=<the bot's API key>
#   site=https://sil-bloom.zulipchat.com
#
# This script is the only thing that reads that file, and it never prints any of it, so an agent
# can post without ever seeing the key. If the file is missing or incomplete it says so and exits
# with code 2 without posting.

[CmdletBinding()]
param(
	# The message, as Zulip markdown. Pass this or -ContentFile.
	[string]$Content,
	# A UTF-8 file holding the message, for content that is awkward to quote on a command line.
	[string]$ContentFile,
	[string]$Channel = "developers",
	[string]$Topic = "Nightly run"
)

$ErrorActionPreference = "Stop"

$rcPath = Join-Path $HOME ".zuliprc"
if (-not (Test-Path -LiteralPath $rcPath)) {
	Write-Host "post-to-zulip: no ~/.zuliprc, so nothing was posted."
	exit 2
}

$settings = @{}
foreach ($line in [System.IO.File]::ReadAllLines($rcPath)) {
	if ($line -match '^\s*(email|key|site)\s*=\s*(.+?)\s*$') {
		$settings[$Matches[1]] = $Matches[2]
	}
}
foreach ($name in "email", "key", "site") {
	if (-not $settings[$name]) {
		Write-Host "post-to-zulip: ~/.zuliprc has no '$name' setting, so nothing was posted."
		exit 2
	}
}

if ($ContentFile) {
	$Content = [System.IO.File]::ReadAllText($ContentFile, [System.Text.Encoding]::UTF8)
}
if ([string]::IsNullOrWhiteSpace($Content)) {
	throw "post-to-zulip: no message to post. Pass -Content or -ContentFile."
}

# Built by hand, with explicit UTF-8, because Windows PowerShell 5.1 does not reliably encode
# non-ASCII text in a form body it builds itself.
$fields = [ordered]@{ type = "stream"; to = $Channel; topic = $Topic; content = $Content }
$body = ($fields.GetEnumerator() | ForEach-Object {
	"$($_.Key)=$([System.Uri]::EscapeDataString($_.Value))"
}) -join "&"
$auth = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("$($settings.email):$($settings.key)"))

try {
	$response = Invoke-RestMethod -Method Post -Uri "$($settings.site.TrimEnd('/'))/api/v1/messages" `
		-Headers @{ Authorization = "Basic $auth" } `
		-ContentType "application/x-www-form-urlencoded; charset=utf-8" `
		-Body ([System.Text.Encoding]::UTF8.GetBytes($body))
} catch {
	# Report the status only; the exception text can echo the request.
	$status = $_.Exception.Response.StatusCode.value__
	throw "post-to-zulip: Zulip refused the message (HTTP $status)."
}
Write-Host "post-to-zulip: posted to #$Channel > $Topic (message id $($response.id))."
