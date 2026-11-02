// The Windows clipboard, set up the way another program would leave it.
//
// A real Ctrl+V in Bloom's WebView2 reads the operating system's clipboard, and so does Bloom's C#
// when it pastes a picture (editView/pasteImage). A test cannot fill that clipboard through the
// page: a copy the page makes can hold only what the page can produce, which is never a bitmap.
// So these helpers set it from outside, with Windows Forms in a short-lived PowerShell process,
// as a picture copied in another program would.
//
// The clipboard belongs to the whole Windows session, not to the Bloom under test. A local run
// therefore replaces whatever the developer had copied.

import { spawnSync } from "node:child_process";
import * as fs from "node:fs";

// Windows Forms needs a single-threaded apartment for the clipboard, hence -STA below. The path
// goes into the script as a single-quoted PowerShell literal, and the script goes to PowerShell
// base64-encoded, so that nothing on the way reinterprets a backslash or a quote in either.
// SetDataObject retries because another program may hold the clipboard open for a moment. The
// final checks read the clipboard back, so a copy that silently did not happen fails here rather
// than as a paste that seemed to do nothing.
const putPictureScript = (filePath: string) => `
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$path = '${filePath.replace(/'/g, "''")}'
$picture = [System.Drawing.Image]::FromFile($path)
$data = New-Object System.Windows.Forms.DataObject
$data.SetImage($picture)
$data.SetText($path)
[System.Windows.Forms.Clipboard]::SetDataObject($data, $true, 20, 100)
if (-not [System.Windows.Forms.Clipboard]::ContainsImage()) { throw 'The clipboard has no picture after the copy.' }
$text = [System.Windows.Forms.Clipboard]::GetText()
if ($text -ne $path) { throw "The clipboard's text after the copy is '$text', not the picture's path." }
`;

/**
 * Copy a picture to the Windows clipboard together with its file path as text, the way Bloom's
 * own Copy Image and a "copy image" in many other programs do. The path beside the picture is
 * what a paste into a text block used to insert instead of the picture (BL-17014), so a test
 * of that paste needs both.
 *
 * Returns when the clipboard holds both. Throws, with PowerShell's message, if it does not.
 */
export function copyPictureWithPathToClipboard(filePath: string): void {
    if (!fs.existsSync(filePath))
        throw new Error(
            `copyPictureWithPathToClipboard: there is no picture at ${filePath}.`,
        );
    const result = spawnSync(
        "powershell.exe",
        [
            "-NoProfile",
            "-NonInteractive",
            "-STA",
            "-EncodedCommand",
            Buffer.from(putPictureScript(filePath), "utf16le").toString(
                "base64",
            ),
        ],
        {
            encoding: "utf8",
            timeout: 60000,
        },
    );
    if (result.error || result.status !== 0)
        throw new Error(
            `Could not put ${filePath} on the Windows clipboard (exit ${result.status}): ` +
                `${result.error?.message ?? ""}${result.stderr}${result.stdout}`,
        );
}
