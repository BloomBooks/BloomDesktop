# Run Bloom in the invariant culture

Card: [BL-16981](https://issues.bloomlibrary.org/youtrack/issue/BL-16981). Target: 6.6 (master).

## Goal

Bloom's process runs with `CultureInfo.CurrentCulture` set to the invariant culture. The user's
regional format is captured once at startup and used only where we show or read numbers, dates,
and sorted lists to or from the user. Code that forgets about culture then "just works" with
machine data (CSS, versions, file names, logs, tool output), and the worst a mistake can do is show
a user an English-style number.

`CurrentUICulture` is not touched. L10NSharp uses it to choose translations.

## Why this is enough for the recent bugs

A .NET 8 probe (ICU, as Bloom runs):

| Call | th-TH | invariant |
| --- | --- | --- |
| `"en-US".IndexOf("-")` | 0 | 2 |
| `"abc".IndexOf("/")` / `IndexOf("?")` | 0 | -1 |
| `"pic.png".LastIndexOf(".")` | 7 | 3 |
| `"abc".IndexOf("\u00AD")` (soft hyphen), `"\u200B"`, `"\u200D"` | 0 | **0** |
| `"I".ToLower()`, `double.Parse("1.5")` | (tr-TR, fr-FR break these) | `i`, `1.5` |

So the invariant culture would have prevented the Thai bugs BL-16932, BL-16806, BL-16945 and
BL-16926, including the ones inside libpalaso and TagLib#, and most of what BL-16934's sweep to
ordinal guards against. It does not make searches for default-ignorable
characters (soft hyphen, zero-width space and joiner) ordinal. Any comparison that isn't ordinal,
including the invariant culture, drops those characters from the search string, and a string made
only of them "matches" at the start index (see the remarks on
[`String.IndexOf`](https://learn.microsoft.com/dotnet/api/system.string.indexof) and the `\0`
examples in [Globalization and ICU](https://learn.microsoft.com/dotnet/core/extensions/globalization-icu)).
Book text contains those characters (e.g. the zero-width space in the audio split marker), so the
CA1310 rule stays.

BL-16947 (ordinal searches in libpalaso) is still worth merging: libpalaso is shared with
FieldWorks and others that don't run invariant. Once this plan is in, Bloom no longer depends on it
for any known bug.

### Number and date parsing and formatting

This is the biggest win. Every call that does not name a culture uses `.` for decimals and `,` for
thousands, whatever the machine is set to:

- parsing: `double.Parse`, `float.TryParse`, `decimal.Parse`, `Convert.ToDouble(string)`. This is
  the BL-15064 class: `pxToNumber` read `"12.5px"` as 125 on a French machine and left images
  uncropped;
- formatting: `x.ToString()`, `x.ToString("0.0")`, `string.Format`, and interpolation such as
  `$"{width}px"`, the most common way a comma could get into CSS, SVG or HTML values;
- `DateTime.Parse` and `DateTime.ToString()`, for logs and file data;
- the same calls made inside libpalaso, TagLib# and other in-process libraries.

Not changed:

- numbers and dates shown to the user display English-style until moved to `UserCulture.Current`
  (step 3);
- code that already pins invariant (Newtonsoft JSON, `XmlConvert`, explicit
  `CultureInfo.InvariantCulture`) behaves as before;
- the front end: JavaScript's `parseFloat`/`Number()` always use `.`; `toLocaleString`, `Intl`
  and `toLocaleLowerCase`/`toLocaleUpperCase` follow the WebView2 locale (BL-16754 fixed a Turkish
  `toLocaleLowerCase` bug in `hyperlinks.ts`);
- separate processes, until step 2 reaches them;
- `double.Parse("1,5")` returns 15 under invariant, because `,` is the thousands separator, the same
  as on an English machine today. If we ever parse machine data that might contain a comma, pass
  `NumberStyles.Float` so it fails instead.

Libraries running in Bloom's process get the fix for free. Separate processes do not: our own
`WebView2PdfMaker.exe` and `BloomFreezeDoctor.exe`, and third-party tools (ffmpeg, Reading App
Builder, Gradle). Arguments we build for those tools are still formatted in Bloom's process, so
they do get the fix. The front end has its own locale from WebView2 and is out of scope here.

## Steps

1. **`UserCulture` class** (`src/BloomExe/UserCulture.cs`, or wherever shared code fits). Holds the
   culture captured at startup. One method, `UseInvariantCulture()`, which:
   - captures `CultureInfo.CurrentCulture` as `UserCulture.Current` (the user's regional format);
   - sets `CultureInfo.DefaultThreadCurrentCulture` and `CultureInfo.CurrentCulture` to
     `CultureInfo.InvariantCulture`.

   .NET reads `DefaultThreadCurrentCulture` lazily for every thread that has not set its own
   culture, and `CurrentCulture` flows into async continuations, so nothing more is needed.

2. **Call it first in every `Main`**: `Bloom` (`Program.cs:159`), `WebView2PdfMaker`,
   `BloomFreezeDoctor`. It must run before anything saves the culture in a static field.

3. **Move the deliberate user-culture sites to `UserCulture.Current`:**
   - `Publish/BloomLibrary/BloomLibraryPublishModel.cs:848-849` (dates shown to the user)
   - `Book/LicenseChecker.cs:321` (list separator)
   - `Collection/NaturalSortComparer.cs:89,92` (book title order)
   - `FontProcessing/FontsApi.cs:164` (font list order)

   Judgement calls (invariant is acceptable; switch only if it matters):
   - `FontsApi.cs:48`, `MiscUI/LanguageFontDetails.cs:25`: `list.Sort()` on font names. Nearly all
     font names are Latin, so invariant order looks the same.
   - `Collection/ScriptSettingsDialog.cs:42,84,96`: the line-spacing list formats and parses its own
     items, so it round-trips either way; a French user would see `1.5` instead of `1,5`.

4. **Delete the workarounds this makes redundant**, each with its th-TH test moved to the new setup
   (step 6) so it still proves something:
   - `ToPalaso/IetfLanguageTagExtra.cs` (the whole class; call `IetfLanguageTag` directly). BL-16947
     removes it too; whichever lands first does it.
   - `ImageProcessing/TagLibCultureFix.cs`, the TagLib# `FileTypeResolver` from BL-16926, and the
     call that registers it at startup.
   - the blocks that set `CurrentCulture` and `CurrentUICulture` to invariant around starting
     ffmpeg and other tools: `ToPalaso/CommandLineRunnerExtra.cs` (`RunWithInvariantCulture`),
     `Publish/Video/RecordVideoWindow.cs` (`RunFfmpeg`) and
     `web/controllers/AudioSegmentationApi.cs`. They came from BL-12235 (2023, on .NET Framework),
     on the theory that the child process inherits Bloom's culture. The actual ffmpeg bug was
     comma decimals in the arguments, which the same work fixed by formatting them invariantly.
     On .NET 8, setting `CultureInfo` does not change the Windows locale a child process gets, so
     these blocks very likely do nothing. Confirm that (e.g. a child process that prints its
     culture), then delete them.

5. **Guard rails**
   - **Banned-API analyzer** (`Microsoft.CodeAnalysis.BannedApiAnalyzers`, error severity), banning
     `CultureInfo.CurrentCulture` (get and set), `Thread.CurrentCulture`,
     `StringComparison.CurrentCulture[IgnoreCase]` and `StringComparer.CurrentCulture[IgnoreCase]`.
     `UserCulture.cs` is the only allowed exception. The message points to `UserCulture.Current`.
   - **Runtime check** that `CultureInfo.CurrentCulture` is still invariant, to catch a library
     that sets it. Cheap enough to do on every API request in `BloomServer`; report it through the
     usual non-fatal-problem path, and fail in tests.
   - **Keep CA1310** (ordinal string search), for the ignorable-character cases above.
   - Do **not** turn on CA1304/CA1305; requiring an explicit culture on every call is what this
     plan makes unnecessary.

6. **Make the tests match production.** `src/BloomTests/TestCulture.cs` always calls
   `UserCulture.UseInvariantCulture()`. `BLOOM_TEST_CULTURE` then sets `UserCulture.Current`
   instead of `CurrentCulture`. The weekly sweep keeps working and now tests what users actually
   run. Tests that set a culture themselves, by assignment or NUnit's `[SetCulture]`
   (`XMatterPackFinderTests`, `ThaiCultureStringSearchTests`, `TranslationGroupManagerTests`,
   `TagLibCultureFixTests`, `InternalSpreadsheetTests`, `ExportEpubTests`, `AnalyticsApiTests`,
   `SubscriptionTests`, `CollectionSettingsTests`), or read `CurrentCulture` (`LicenseCheckerTests`,
   `TestCultureTests`), are reviewed one by one: either they test a user-culture path (use
   `UserCulture.Current`) or code's behaviour under a hostile culture (set `CurrentCulture` with an
   allowed exception), or they become redundant along with the workaround they cover.

7. **Update the guidance**: the "Don't assume the machine is running in English" section of the
   root `AGENTS.md` and `src/BloomTests/AGENTS.md` become "the process is invariant; use
   `UserCulture.Current` for anything shown to or read from the user".

## Order

Steps 1, 2, 5 (analyzer) and 6 together, since the analyzer will flag every site in step 3 and the
tests need the new setup in the same change. Step 4 can be a follow-up commit.

## Open questions

- Should the WebView2 front end get a similar policy? It mostly passes explicit locales to `Intl`,
  but locale-sensitive calls such as `toLocaleLowerCase` have bitten it (BL-16754).
