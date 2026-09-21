# Getting rid of WinForms that comes in through dependencies

*Analysis of master as of 2026-09-17, against libpalaso 18.0.0-beta0028, L10NSharp 10.0.0-beta0005,
WebView2 1.0.1518.46. Our own WinForms code (Forms, Designer files, dialogs) is out of scope here;
this is about what third-party packages force on us.*

## Where WinForms enters through dependencies

Six NuGet packages in BloomExe declare a WinForms or WindowsDesktop framework reference. Everything
else in the graph is netstandard or plain net8.0.

| Package | Why it's WinForms | How much of Bloom touches it |
|---|---|---|
| SIL.Windows.Forms | net8.0-windows only; owns ClearShare `Metadata`, `PalasoImage`, `ImageUtils`, `Registration`, `PortableClipboard`, `ProgressDialog`, `ConfirmRecycleDialog`, `UniqueToken`, `WinFormsErrorReporter` | ~55 files. The big one. |
| SIL.Windows.Forms.WritingSystems | Pulls in SIL.Windows.Forms and SIL.Windows.Forms.Keyboarding | One static `LanguageLookupModel` in `Collection/WritingSystem.cs` |
| SIL.Windows.Forms.Keyboarding | net8.0-windows; drags in Enchant.Net, ibusdotnet, KeymanLegacyBundle (all .NET Framework compat shims, source of the NU1701 warnings) | One call, `KeyboardController.IsFormUsingInputProcessor`, in `web/controllers/KeyboardingConfigApi.cs` |
| SIL.Media | net8.0-windows only; wraps NAudio (Windows) and ALSA (Linux). No macOS backend at all. | One file, `Edit/AudioRecording.cs` (`AudioRecorder`, `RecordingDevice`) |
| L10NSharp.Windows.Forms | The `L10NSharpExtender` designer component | Designer files plus 2 production files. The core `LocalizationManager` API we call ~480 times lives in the netstandard `L10NSharp` package, so this goes away with our own WinForms dialogs. |
| Microsoft-WindowsAPICodePack-Shell | References PresentationFramework and System.Windows.Forms | One `CommonOpenFileDialog` in `MiscUI/BloomFolderChooser.cs` |
| Microsoft.Web.WebView2 | The package is fine; we use its WinForms wrapper control | `BloomWebView2.cs`, `WebView2Browser.cs`, and the WebView2PdfMaker project |

## What each one takes to remove

### SIL.Windows.Forms: mostly a libpalaso problem, and libpalaso has started on it

The unreleased libpalaso changelog and [PR #1478](https://github.com/sillsdev/libpalaso/pull/1478)
moved the license and metadata core into a new `SIL.Core.ClearShare` namespace in the netstandard
SIL.Core assembly: `MetadataCore`, `LicenseInfo`, `CreativeCommonsLicenseInfo`, `CustomLicenseInfo`,
`NullLicense`, `LicenseUtils`. The beta0028 we already reference contains them.

What has **not** moved is the image side: `PalasoImage`, `ImageUtils`, and `Metadata.FromFile`, all
built on System.Drawing. Our usage is narrow but deep:

- `PalasoImage.FromFileRobustly` in 16 places, `PalasoImage.SaveImageRobustly` in 2.
- `Metadata` in ~200 lines across 20 files.
- ~25 distinct `ImageUtils` methods, many called from our own wrappers in
  `ImageProcessing/ImageUtils.cs`.

The realistic path is a libpalaso contribution: a netstandard image core on ImageSharp or SkiaSharp
(libpalaso's own new `MetadataCore` tests already use ImageSharp), with `Metadata.FromFile` reading via
TagLibSharp, which is already netstandard.

The remaining SIL.Windows.Forms pieces we use are small and replaceable one by one:
`Registration.Default` is a settings class we only touch for the settings-upgrade path;
`UniqueToken` and `PortableClipboard` have trivial cross-platform equivalents; the progress and
recycle dialogs go away with our own WinForms UI; `WinFormsErrorReporter` is only a fallback inside
`HtmlErrorReporter`.

### The three one-call packages: an afternoon each

- `LanguageLookupModel` has a netstandard equivalent (`LanguageLookup`) in SIL.WritingSystems.
- The keyboarding call is a Windows TSF check; make it a platform-guarded no-op or hide it behind an
  interface. Dropping this package also removes the three .NET Framework compat shims.
- The folder chooser becomes a native dialog from whatever host framework replaces WinForms.

### SIL.Media: needs a replacement, not a port

Recording is the only thing we use it for, and there is no macOS implementation to port. Options:

- A cross-platform native audio library (PortAudio bindings, miniaudio, OpenTK.Audio).
- Record in the browser with `MediaRecorder` and post the WAV to the server. This removes the native
  dependency entirely and is worth evaluating first.

### WebView2: the architectural decision, not a dependency swap

WebView2's Core is Windows-only regardless of wrapper, so leaving WinForms means choosing a new browser
host: Avalonia plus a WebView control, Photino, CEF, Electron with a .NET sidecar, or Tauri-style
native webviews. Everything in `WebView2Browser` and `IBrowser`, plus the WebView2PdfMaker process
that calls `PrintToPdf`, is coupled to that choice. PDF generation will need a Chromium-based path on
every platform, most likely headless Chromium or Playwright driving `Page.printToPDF`.

## Not WinForms, but Windows-only just the same

These block cross-platform even after WinForms is gone, so they belong on the same list.

- **System.Drawing.Common** is referenced directly by 63 non-Designer files and transitively by
  SIL.Core.Desktop, QRCoder, EPPlus.System.Drawing, sillsdev.dotImpose, and SIL.Windows.Forms. On
  .NET 7+ it throws on non-Windows. QRCoder's `PngByteQRCode` and EPPlus 7 both drop the dependency;
  dotImpose is ours and uses it lightly; the bulk is our own image code, which follows whatever
  imaging library the libpalaso work picks.
- **WPF** (`UseWPF=true`) is used in 8 files for `GlyphTypeface` font inspection
  (`FontProcessing/*`), `LocalPrintServer`, and `BitmapSource`. Font metadata can move to a managed
  OpenType reader (SixLabors.Fonts, Typography); printing goes with the host framework.
- **PodcastUtilities.PortableDevices** (`lib/dotnet`, Windows Portable Devices COM) drives Android
  USB publishing. Cross-platform means ADB, an MTP library, or relying on the Wi-Fi path.
- **System.Management** (WMI) in `Utils/MiscUtils.cs`, `Utils/PerformanceMeasurement.cs`, and
  BloomFreezeDoctor.Core needs platform guards or `/proc` and `sysctl` equivalents.
- **Microsoft.Win32.Registry** in 5 files, `ProtectedData`, and ~80 `Platform.IsWindows`-style checks
  are ordinary cleanup.
- **Cairo** (CairoSharp) and **Ghostscript** for PDF post-processing are cross-platform in principle
  but need per-platform native binaries shipped.

Everything else in the graph (Autofac, AWSSDK, Sentry, RestSharp, Newtonsoft, SharpZipLib,
HtmlAgilityPack, Markdig, PDFsharp 6, SQLite, Velopack, TagLibSharp, icu.net, CommandLineParser,
Fleck) is already cross-platform.

## Suggested order

1. Upgrade to a libpalaso release with `SIL.Core.ClearShare` and switch our license and metadata code
   to the core types. Available now; shrinks the WinForms surface immediately.
2. Remove the three one-call dependencies: WritingSystems, Keyboarding, WindowsAPICodePack.
3. Decide the browser host. Every other UI decision hangs off it.
4. Run the imaging replacement (libpalaso image core, our System.Drawing code, the WPF font code) as
   one workstream, since they share a library choice.
5. Replace audio recording, USB publishing, and WMI last; they are self-contained.

## How this was measured

Restored `src/BloomExe/BloomExe.csproj` and read `project.assets.json` for `frameworkReferences` and
the reverse dependency graph; scanned each package DLL for referenced assembly names; grepped
`using` directives and type names across `src/BloomExe`, `src/BloomTests`, `src/WebView2PdfMaker`,
and the FreezeDoctor projects.
