# libpalaso ClearShare Migration Plan

## Purpose
Move BloomDesktop off WinForms-bound ClearShare types in non-UI/domain layers, while preserving current behavior and enabling eventual full WinForms-independent metadata/license workflows.

This plan is library-first: Bloom should avoid custom workaround logic where a clean libpalaso API can solve the boundary.

---

## Current status (already completed in BloomDesktop)

### Completed migration work
- Replaced most concrete-license checks (`CreativeCommonsLicense`, `CustomLicense`) with core type checks (`CreativeCommonsLicenseInfo`, `CustomLicenseInfo`) in domain logic.
- Widened several non-UI method signatures from `Metadata` to `MetadataCore`.
- Updated tests/build to compile with the widened signatures.
- Converted additional core construction sites to instantiate core types directly (`MetadataCore`, `CreativeCommonsLicenseInfo`) instead of WinForms concrete types where behavior is equivalent.
- Updated more test construction sites to use `MetadataCore`/`CreativeCommonsLicenseInfo` when no WinForms-specific behavior is needed.
- Removed additional stale `SIL.Windows.Forms.ClearShare` imports from non-UI/domain files now using core types:
   - `src/BloomExe/Book/Book.cs`
   - `src/BloomExe/Book/BookInfo.cs`
   - `src/BloomExe/Book/ImageUpdater.cs`
   - `src/BloomExe/Edit/EditingModel.cs`
- Switched internal metadata construction in `BookCopyrightAndLicense` to instantiate `MetadataCore` where methods already return `MetadataCore`.
- Updated remaining Bloom-side legacy `CustomLicense` and concrete CC type assumptions in affected tests and API paths.

### Current blocker inventory (as of 2026-02-18)
- Remaining `using SIL.Windows.Forms.ClearShare;` in `BloomExe`: **7 files**.
- Remaining `ILicenseWithImage` / `GetImage()` license-image dependencies: `BookCopyrightAndLicense` and `CopyrightAndLicenseApi`.
- Remaining WinForms-dependent image metadata entrypoint: `RobustFileIO.MetadataFromFile()` and downstream image-edit/credit flows.

### Audit status vs `master` (2026-02-19)
- Audited all ClearShare-related changes currently in the branch delta (`Book`, `BookInfo`, `ImageUpdater`, `EditingModel`, `BookCopyrightAndLicense`, `CopyrightAndLicenseApi`, and related tests).
- Confirmed core-first signatures and core license/type checks are now used across these domain paths.
- Confirmed current `MetadataCore` → WinForms `Metadata` adaptation is limited to image-edit/save boundaries.
- Fixed an unsafe runtime cast in `CopyrightAndLicenseApi` image POST handling by using a dedicated WinForms-`Metadata` construction path at the image boundary.
- No additional branch-delta locations were found where core types could be safely substituted without crossing known libpalaso boundary constraints.

### Commits on branch
- `8ac9cc809f` — core license info checks in Bloom domain logic
- `1e62f6fc92` — start migration to `MetadataCore`
- `dd68bbc06d` — continue migration in Book/Edit layers
- `7a8983dbd1` — fix `MetadataCore` migration mismatches in API/tests

---

## End-state architecture

1. **Core domain/business code** (book/license JSON, publish checks, metadata transfer logic) uses:
   - `SIL.Core.ClearShare.MetadataCore`
   - `SIL.Core.ClearShare.LicenseInfo`
   - `SIL.Core.ClearShare.CreativeCommonsLicenseInfo`
   - `SIL.Core.ClearShare.CustomLicenseInfo`

2. **UI/image rendering boundaries** use either:
   - a libpalaso-provided core-safe image API (preferred), or
   - temporary adapters until library APIs exist.

3. **No Bloom domain logic** depends on `SIL.Windows.Forms.ClearShare` concrete types.

---

## Library work required first (libpalaso)

These are the blockers preventing full Bloom migration without Bloom-specific glue code.

## L1. License image access without WinForms type coupling

### Problem
Bloom currently gets license images via `ILicenseWithImage` / `GetImage()` (WinForms-specific).

### Needed in libpalaso
Provide a **WinForms-independent** API for CC/custom license image retrieval, e.g. one of:
- `LicenseImageUtils.GetImageBytes(LicenseInfo license, string format = "png")`
- `LicenseImageUtils.TryGetImage(LicenseInfo license, out byte[] imageBytes, ... )`
- or a core-safe token/url-to-image resolver.

### Why
This removes the need for Bloom to cast to `ILicenseWithImage` and unblocks full removal of WinForms ClearShare from domain code.

---

## L2. ImageToolbox callback contract should accept core metadata

### Problem
Bloom image toolbox integration still uses `Action<Metadata>` and returns `Metadata` in callback paths.

### Needed in libpalaso
Update toolbox interfaces/delegates to use `MetadataCore` (or a shared abstraction) for metadata edit/save callbacks.

### Why
This is the biggest remaining typed dependency in `EditingView` and image metadata workflows.

---

## L3. Metadata read/write entry points should expose core-first APIs

### Problem
Bloom still relies on WinForms-returning APIs (`Metadata.FromFile`) in `RobustFileIO` and other image flows.

### Needed in libpalaso
Add/standardize core-first entry points for metadata I/O (read/write intellectual-property metadata) that do not require `Metadata`.

### Why
Lets Bloom eliminate remaining `SIL.Windows.Forms.ClearShare` type imports outside UI-only integration points.

---

## BloomDesktop remaining areas to change

This section lists what still needs Bloom changes after/beside library updates.

## B1. License image endpoints and book license icon writing (blocked on L1)

### Files
- `src/BloomExe/web/controllers/CopyrightAndLicenseApi.cs`
- `src/BloomExe/Book/BookCopyrightAndLicense.cs`

### Current dependency
- `ILicenseWithImage` casts and `GetImage()`.

### Planned change
- Replace with libpalaso core-safe license image API.
- Remove WinForms ClearShare dependency from these paths.

---

## B2. Image toolbox metadata callback path (blocked on L2)

### Files
- `src/BloomExe/Edit/EditingView.cs`
- `src/BloomExe/Edit/BloomMetadataEditorDialog.cs` (if still used in active code path)

### Current dependency
- `_originalImageMetadataFromImageToolbox: Metadata`
- `_saveNewImageMetadataActionForImageToolbox: Action<Metadata>`
- methods around `PrepareToEditImageMetadata`, `SaveImageMetadata`, `CopyImageMetadataToAllImages` still typed as `Metadata`.

### Planned change
- Switch these boundary contracts to `MetadataCore` once libpalaso callback interfaces support it.

---

## B3. Metadata file read API (`RobustFileIO`) and downstream users (depends on L3)

### Files
- `src/BloomExe/RobustFileIO.cs`
- plus call sites currently expecting `Metadata`:
  - `src/BloomExe/web/controllers/ImageApi.cs`
  - `src/BloomExe/ImageProcessing/ImageUtils.cs`
  - `src/BloomExe/Book/ImageUpdater.cs`
  - `src/BloomExe/Book/BookCompressor.cs`

### Planned change
- Change `RobustFileIO.MetadataFromFile` to return `MetadataCore` (or add `MetadataCoreFromFile` and migrate callers).
- Update callers to core types.

---

## B4. Remove residual WinForms ClearShare imports after the above

### Files currently still importing `SIL.Windows.Forms.ClearShare`
- `src/BloomExe/Edit/EditingView.cs`
- `src/BloomExe/web/controllers/CopyrightAndLicenseApi.cs`
- `src/BloomExe/web/controllers/ImageApi.cs`
- `src/BloomExe/RobustFileIO.cs`
- `src/BloomExe/ImageProcessing/ImageUtils.cs`
- `src/BloomExe/Edit/BloomMetadataEditorDialog.cs`
- `src/BloomExe/Book/BookCopyrightAndLicense.cs`

### Planned change
- As each blocker resolves, remove imports from files that no longer need WinForms ClearShare types.

---

## Suggested execution sequence

## Phase A — libpalaso implementation
1. Implement L1 license-image API.
2. Implement L2 metadata callback contract changes in ImageToolbox.
3. Implement L3 core-first metadata I/O entry points.
4. Release/publish package(s).

## Phase B — BloomDesktop adoption (minimal, targeted)
1. Migrate B1 to new license-image API.
2. Migrate B2 callback/metadata method signatures to `MetadataCore`.
3. Migrate B3 metadata read call chain to core API.
4. Remove now-unused WinForms ClearShare imports/usages (B4).

## Phase C — cleanup and hardening
1. Add regression tests for CC detection and metadata roundtrip using core types.
2. Verify image C/L dialog and copy-metadata-to-all-images flows.
3. Verify publish/paths that read license URL/token still produce identical output.

---

## Validation checklist

### Build/test gates
- `dotnet build Bloom.sln` succeeds.
- Existing license/copyright tests compile and pass.
- No new runtime casts from `MetadataCore` to `Metadata` in non-UI domain code.

### Behavior gates
- CC vs custom vs contact classification unchanged in UI and publish logic.
- CC0 handling unchanged.
- Image metadata copy-to-all and image C/L edit flows still work.
- License icon rendering still appears where expected.

---

## Risk notes
- Highest risk area is image metadata/toolbox integration due to callback type boundaries.
- Next risk is image-license icon rendering transition away from `ILicenseWithImage`.
- Domain-license logic risk is lower now that most checks use core info types.
- Not 100% safe yet (explicitly accepted until library changes):
   - `CopyrightAndLicenseApi.HandleGetCCImage()` still uses `CreativeCommonsLicense.FromToken(...)` to obtain license images for token-based preview.
   - `BookCopyrightAndLicense.UpdateBookLicenseIcon()` and `CopyrightAndLicenseApi` still require `ILicenseWithImage` to render/save license icons.
   - `CopyrightAndLicenseApi` image POST path must still construct WinForms `Metadata` (not `MetadataCore`) before calling image-save/edit flows typed as `Metadata`.

---

## Definition of done
- Bloom domain/business code no longer depends on WinForms ClearShare types.
- Remaining WinForms dependencies are strictly UI-framework concerns (if any), not ClearShare model dependencies.
- All license/metadata workflows are build-clean and behavior-compatible.
