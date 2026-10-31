# Getting rid of WinForms that comes in through dependencies

*Master as of 2026-10-30 (07b63183ac): libpalaso 18.0.0, L10NSharp 12.0.0-beta0006, WebView2
1.0.1518.46. Our own WinForms code (Forms, Designer files, dialogs) is out of scope here; this is
about what third-party packages force on us.*

## Where WinForms enters through dependencies

Six NuGet packages in BloomExe are WinForms or Windows-desktop-only: four declare a WindowsDesktop
framework reference in `project.assets.json`, SIL.Windows.Forms.Keyboarding no longer declares one
but still ships only `net8.0-windows`, and WebView2 is used through its WinForms control. Everything
else in the graph is netstandard or plain net8.0.

| Package | Why it's WinForms | How much of Bloom touches it |
|---|---|---|
| SIL.Windows.Forms | net8.0-windows only; owns ClearShare `Metadata` and the image-bearing licenses (`CreativeCommonsLicense`, `CustomLicense`, `ILicenseWithImage`), `PalasoImage`, `ImageUtils`, `Registration`, `PortableClipboard`, `ProgressDialog`, `ConfirmRecycleDialog`, `UniqueToken`, `WinFormsErrorReporter`. Also the source of the Enchant.Net compat shim. | 50 non-Designer files in BloomExe, 13 in BloomTests. The big one. |
| SIL.Windows.Forms.Keyboarding | net8.0-windows only; drags in ibusdotnet and KeymanLegacyBundle (.NET Framework-only builds, source of the NU1701 warnings suppressed in `BloomExe.csproj`) | One call, `KeyboardController.IsFormUsingInputProcessor`, in `web/controllers/KeyboardingConfigApi.cs` |
| SIL.Media | net8.0-windows only; wraps NAudio (Windows) and ALSA (Linux). No macOS backend at all. | One file, `Edit/AudioRecording.cs` (`AudioRecorder`, `RecordingDevice`) |
| L10NSharp.Windows.Forms | The `L10NSharpExtender` designer component. A direct reference at **12.0.0-beta0006**, matching core L10NSharp; SIL.Windows.Forms and Keyboarding would otherwise bring 11.0.1 | 11 Designer files plus `NewCollectionWizard.cs`, and `using L10NSharp.Windows.Forms` in `Program.cs` and `Workspace/WorkspaceView.cs`. The core `LocalizationManager` API lives in the netstandard `L10NSharp` package, so this goes away with our own WinForms dialogs. |
| Microsoft-WindowsAPICodePack-Shell | References PresentationFramework and System.Windows.Forms | One `CommonOpenFileDialog` in `MiscUI/BloomFolderChooser.cs` |
| Microsoft.Web.WebView2 | The package is fine; we use its WinForms wrapper control | `BloomWebView2.cs`, `WebView2Browser.cs`, and the WebView2PdfMaker project |

## What each one takes to remove

### SIL.Windows.Forms: mostly a libpalaso problem, and libpalaso has started on it

The netstandard SIL.Core assembly has the license and metadata core in `SIL.Core.ClearShare`
([PR #1478](https://github.com/sillsdev/libpalaso/pull/1478)): `MetadataCore`, `LicenseInfo`,
`CreativeCommonsLicenseInfo`, `CustomLicenseInfo`, `NullLicense`, `LicenseUtils`. 7 BloomExe files
import `SIL.Core.ClearShare`.

Still coming from `SIL.Windows.Forms.ClearShare` (13 files):

- `Metadata`, including `Metadata.FromFile`: ~60 lines across 14 files. Every one of the 13 files
  needs it, so this is what keeps the import.
- `CreativeCommonsLicense` and `CustomLicense`, mainly in `Book/BookCopyrightAndLicense.cs`,
  `web/controllers/CopyrightAndLicenseApi.cs`, `ImageGalleryApi.cs` and `AiImageEditorApi.cs`.
- `ILicenseWithImage.GetImage()` (the CC badge bitmap) in `BookCopyrightAndLicense.cs` and
  `CopyrightAndLicenseApi.cs`. The core `*Info` types have no badge.

Moving the license code to the `*Info` types on its own removes no package and no import, and would
mean copying libpalaso's badge PNGs into Bloom. It belongs in the libpalaso contribution below:
add the badges to SIL.Core (a `CreativeCommonsLicenseInfo` method returning PNG bytes, no
System.Drawing), and Bloom's switch becomes mechanical. Two traps for whoever does it:

- `Metadata` returns the WinForms subclasses when it reads an image, so every type check must
  test the `*Info` base.
- Bloom writes the badge into each book as `license.png`. To keep that file byte-identical in
  existing books, the badge bytes must be what GDI+ writes when it saves libpalaso's resource
  bitmaps, not the raw resource PNGs.

The image side has **not** moved either: `PalasoImage` and `ImageUtils`, built on System.Drawing.
Our usage is narrow but deep:

- `PalasoImage.FromFileRobustly` in 16 places (8 files), `SaveImageRobustly` in 2.
- `SIL.Windows.Forms.ImageToolbox` imported in 11 files, much of it through our own wrappers in
  `ImageProcessing/ImageUtils.cs`.

The realistic path is a libpalaso contribution: a netstandard image core, with `Metadata.FromFile`
reading via TagLibSharp, which is already netstandard. Prefer SkiaSharp: libpalaso removed
ImageSharp from SIL.Core.Tests over its known vulnerabilities
([PR #1553](https://github.com/sillsdev/libpalaso/pull/1553)), so it is unlikely to accept it in a
shipping assembly.

The remaining SIL.Windows.Forms pieces we use are small and replaceable one by one:
`Registration.Default` is a settings class we only touch for the settings-upgrade path;
`UniqueToken` and `PortableClipboard` have trivial cross-platform equivalents; the progress and
recycle dialogs go away with our own WinForms UI; `WinFormsErrorReporter` is only a fallback inside
`HtmlErrorReporter`.

### The two one-call packages: an afternoon each

- The keyboarding call is a Windows TSF check; make it a platform-guarded no-op or hide it behind an
  interface. Dropping this package removes ibusdotnet and KeymanLegacyBundle. Enchant.Net, the third NU1701 shim, comes from SIL.Windows.Forms and stays
  until that does.
- The folder chooser becomes a native dialog from whatever host framework replaces WinForms.

### SIL.Media: needs a replacement, not a port

Recording is the only thing we use it for, and there is no macOS implementation to port. Options:

- A cross-platform native audio library (PortAudio bindings, miniaudio, OpenTK.Audio).
- Record in the browser with `MediaRecorder` and post the WAV to the server. This removes the native
  dependency entirely and is worth evaluating first.

We also reference NAudio directly. Besides feeding SIL.Media, it is used once ourselves:
`Mp3FileReader` in `Utils/MiscUtils.cs` to get an MP3's duration, which TagLibSharp or the ffmpeg we
already ship can do.

### WebView2: the architectural decision, not a dependency swap

WebView2's Core is Windows-only regardless of wrapper, so leaving WinForms means choosing a new browser
host: Avalonia plus a WebView control, Photino, CEF, Electron with a .NET sidecar, or Tauri-style
native webviews. Everything in `WebView2Browser` and `IBrowser`, plus the WebView2PdfMaker process
that calls `PrintToPdf`, is coupled to that choice. PDF generation will need a Chromium-based path on
every platform, most likely headless Chromium or Playwright driving `Page.printToPDF`.

## Not WinForms, but Windows-only just the same

These block cross-platform even after WinForms is gone, so they belong on the same list.

- **System.Drawing.Common** is imported by 63 non-Designer files (68 counting fully qualified
  names) and comes transitively through SIL.Core.Desktop, QRCoder, EPPlus.System.Drawing,
  sillsdev.dotImpose, and SIL.Windows.Forms. On .NET 7+ it throws on non-Windows.
  - QRCoder: we use `ArtQRCode` (`Book/BookStorage.cs`), which needs System.Drawing.
    `PngByteQRCode` doesn't, but loses the art styling.
  - EPPlus: we're on 6.2.4; EPPlus 7 drops System.Drawing.
  - dotImpose: ours, now net8.0, still references System.Drawing.Common lightly.
  - The bulk is our own image code, which follows whatever imaging library the libpalaso work picks.
- **WPF** (`UseWPF=true`) is used in 6 files:
  - `GlyphTypeface` font inspection in `FontProcessing/*` (3 files); this can move to a managed
    OpenType reader (SixLabors.Fonts, Typography).
  - `LocalPrintServer` in `Publish/PDF/MakePdfUsingExternalPdfMakerProgram.cs`; printing goes with
    the host framework.
  - `BitmapSource` in `web/controllers/ImageGalleryApi.cs`; it moves with the imaging workstream.
  - A stray `using System.Windows.Controls` in `Book/HtmlDom.cs`.
- **PodcastUtilities.PortableDevices** (`lib/dotnet`, Windows Portable Devices COM) drives Android
  USB publishing. Cross-platform means ADB, an MTP library, or relying on the Wi-Fi path.
- **System.Management** (WMI) in `Utils/MiscUtils.cs`, `Utils/PerformanceMeasurement.cs`,
  `Utils/FileAccessDiagnostics.cs`, and BloomFreezeDoctor.Core needs platform guards or `/proc` and
  `sysctl` equivalents.
- **Microsoft.Win32.Registry** in 6 files, `ProtectedData`, and ~107 `Platform.IsWindows`-style
  checks are ordinary cleanup.
- **Cairo** (CairoSharp) and **Ghostscript** for PDF post-processing are cross-platform in principle
  but need per-platform native binaries shipped.

Everything else in the graph (Autofac, AWSSDK, Sentry, RestSharp, Newtonsoft, SharpZipLib,
HtmlAgilityPack, Markdig, PDFsharp 6, SQLite, Velopack, TagLibSharp, icu.net, CommandLineParser,
Fleck) is already cross-platform.

## Suggested order

1. Remove the two one-call dependencies: Keyboarding and WindowsAPICodePack.
2. Decide the browser host. Every other UI decision hangs off it.
3. Run the imaging replacement (libpalaso image core with the CC badges, our System.Drawing code,
   the WPF font code) as one workstream, since they share a library choice. Moving Bloom's license
   code to the `*Info` types goes with it, and then `Metadata` leaves SIL.Windows.Forms too.
4. Replace audio recording, USB publishing, and WMI last; they are self-contained.

## How this was measured

Restored `src/BloomExe/BloomExe.csproj` and read `project.assets.json` for `frameworkReferences` and
the reverse dependency graph; scanned each package DLL for referenced assembly names; grepped
`using` directives and type names across `src/BloomExe`, `src/BloomTests`, `src/WebView2PdfMaker`,
and the FreezeDoctor projects; checked libpalaso's 18.0.0 changelog and PRs for upstream movement.
