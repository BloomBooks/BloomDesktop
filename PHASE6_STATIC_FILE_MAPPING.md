# Bloom Static File Serving - Location Mapping

**Purpose**: Document all file serving locations and patterns needed for Kestrel migration Phase 6.

## Primary File Locations

### 1. Browser Root (`BloomFileLocator.BrowserRoot`)
**Purpose**: Main UI files for the Bloom application
**Location**: `DistFiles/browser/` or similar
**Contents**:
- JavaScript files (`.js`)
- CSS stylesheets (`.css`)
- HTML pages
- Images (`favicon.ico`, UI icons, etc.)
- Fonts

**Access Pattern**: `/path/to/file.ext`
**Example**: `/bookEdit/pageThumbnailList/pageThumbnailList.js`

### 2. Distribution Files (`DistFiles/`)
**Purpose**: Read-only application resources
**Subdirectories**:
- `fonts/` - System and custom fonts
- `icons/` - Application icons
- `localization/` - Translation files
- `ColorProfiles/` - ICC color profiles
- `ffmpeg/` - Video processing binaries
- `ghostscript/` - PDF processing tools

**Access Pattern**: Various, typically resolved through `BloomFileLocator`

### 3. Book Folders (`CurrentBook.FolderPath`)
**Purpose**: User-created book content
**Contents**:
- `*.htm` - Book pages
- `*.css` - Book-specific styles
- Images (`.png`, `.jpg`, `.svg`, etc.)
- Audio files (`.mp3`, `.wav`)
- Video files (`.mp4`)
- `meta.json` - Book metadata

**Access Pattern**: `/[bookName]/filename.ext`

### 4. Collection Folders
**Purpose**: Collection-specific resources
**Contents**:
- `*.bloomCollection` - Collection settings
- Custom XMatter templates
- Branding files

### 5. Template Books
**Purpose**: Templates for creating new books
**Location**: Multiple sources via `BloomFileLocator`
- Factory templates: `BloomFileLocator.FactoryCollectionsDirectory`
- User templates: Various locations

## Special URL Patterns

### A. Windows Path Mapping
**Pattern**: `localhost/C$/path/to/file`
**Maps to**: `C:\path\to\file`
**Purpose**: Direct file system access for Windows paths
**Implementation**: URL decode and replace `/C$/` with `C:\`

### B. Original Image Marker
**Pattern**: Contains `OriginalImages` in path
**Purpose**: Serve original (unprocessed) images
**Implementation**: Skip thumbnail/image processing pipeline

### C. Simulated/In-Memory Files
**Pattern**: Registered via `MakeInMemoryHtmlFileInBookFolder()`
**Storage**: `Dictionary<string, InMemoryHtmlFile>`
**Purpose**: Serve dynamically generated HTML without disk I/O
**Lifetime**: Managed with expiration/removal
**Example Use Cases**:
- Temporary preview pages
- Generated thumbnails
- Transient UI pages

### D. Book Preview Paths
**Pattern**: `/book-preview/*`
**Special Files**:
- `/book-preview/index.htm` - Book preview HTML
- `/book-preview/defaultLangStyles.css` - Font injection
- `/book-preview/appearance.css` - Theme styles
- `/book-preview/video-placeholder.svg` - Video placeholders

**Implementation Notes**:
- `defaultLangStyles.css`: Dynamically inject `@font-face` rules for collection languages
- Files may be virtual/generated on-the-fly

### E. Image Processing Paths
**Purpose**: Serve processed images (thumbnails, compressed, etc.)
**Implementation**: `RuntimeImageProcessor` cache
**Special Markers**:
- `?generateThumbnailIfNecessary=true` - Recursive request for thumbnail
- Resolution/size parameters in query string

## Cache Headers

### Static Assets (Long Cache)
**Files**: `.js`, `.css`, fonts, icons
**Headers**:
- `Cache-Control: public, max-age=31536000` (1 year)
- `ETag`: File hash or modification time

### Dynamic Content (Short Cache)
**Files**: Book pages, generated content
**Headers**:
- `Cache-Control: no-cache` or `max-age=60`
- `ETag`: Content hash

### Images (Medium Cache)
**Files**: `.png`, `.jpg`, `.svg`
**Headers**:
- `Cache-Control: public, max-age=86400` (1 day)
- Process through `RuntimeImageProcessor` if needed

## MIME Type Mapping

**Common Types**:
- `.htm`, `.html` → `text/html`
- `.css` → `text/css`
- `.js` → `application/javascript`
- `.json` → `application/json`
- `.png` → `image/png`
- `.jpg`, `.jpeg` → `image/jpeg`
- `.svg` → `image/svg+xml`
- `.mp3` → `audio/mpeg`
- `.mp4` → `video/mp4`
- `.woff`, `.woff2` → `font/woff`, `font/woff2`
- `.ttf` → `font/ttf`
- `.ico` → `image/x-icon`

## File Resolution Order

1. **Check in-memory files** (`_inMemoryHtmlFiles` dictionary)
2. **Check book folder** (if current book context available)
3. **Check browser root** (`BloomFileLocator.BrowserRoot`)
4. **Check BloomFileLocator** (searches multiple locations)
5. **Return 404** if not found

## Special Processing

### CSS Files
**File**: `defaultLangStyles.css`
**Processing**:
1. Read base CSS
2. Inject `@font-face` rules for each collection language
3. Return modified CSS

**File**: `appearance.css`
**Processing**: May apply theme-specific modifications

### Image Files
**Processing Pipeline**:
1. Check `RuntimeImageProcessor` cache
2. If cached, serve from cache
3. If not cached and processing needed:
   - Load original image
   - Apply transformations (resize, compress, watermark, etc.)
   - Cache result
   - Serve processed image
4. If no processing needed, serve original

### HTML Files
**Processing**:
- May apply branding
- May inject scripts/styles
- May transform for preview/export

## CORS Headers

**For API Requests**:
- `Access-Control-Allow-Origin: *` (or specific origins)
- `Access-Control-Allow-Methods: GET, POST, OPTIONS`
- `Access-Control-Allow-Headers: Content-Type`

**Purpose**: Allow browser to access local server from file:// protocol or different origins

## Implementation Strategy for Kestrel

### Phase 6.1: Static File Middleware
1. **Create `KestrelStaticFileMiddleware.cs`**
   - Check in-memory files first
   - Delegate to ASP.NET Core Static Files middleware
   - Apply appropriate cache headers
   - Handle special file processing (CSS, images)

2. **Configure Physical File Providers**
   ```csharp
   var fileProvider = new PhysicalFileProvider(BloomFileLocator.BrowserRoot);
   app.UseStaticFiles(new StaticFileOptions
   {
       FileProvider = fileProvider,
       OnPrepareResponse = ctx => {
           // Set cache headers based on file type
       }
   });
   ```

3. **URL Rewriting Middleware**
   ```csharp
   app.UseRewriter(new RewriteOptions()
       .Add(context => {
           // Handle C$/ paths
           // Handle OriginalImages marker
           // Handle book-preview paths
       }));
   ```

### Phase 6.2: File Location Service
1. **Create `IFileLocationService` interface**
   ```csharp
   public interface IFileLocationService
   {
       string GetBrowserFile(string relativePath);
       string GetDistributedFile(string filename);
       string GetBookFile(string filename);
       bool TryGetInMemoryFile(string path, out InMemoryHtmlFile file);
       void AddInMemoryFile(string path, InMemoryHtmlFile file);
       void RemoveInMemoryFile(string path);
   }
   ```

2. **Implement service wrapping `BloomFileLocator`**
3. **Register in DI container**

## Testing Checklist

- [ ] Browser root files serve correctly
- [ ] Book folder files accessible
- [ ] In-memory files work
- [ ] Windows path mapping (`C$/`)
- [ ] Image processing pipeline
- [ ] CSS injection (`defaultLangStyles.css`)
- [ ] Book preview paths
- [ ] Cache headers applied correctly
- [ ] MIME types correct
- [ ] 404 for missing files
- [ ] Concurrent in-memory file access
- [ ] File removal/cleanup

## Current Bloom Server Reference

**File**: `src/BloomExe/web/BloomServer.cs`
**Key Methods**:
- `ProcessRequestAsync()` - Main request handler (lines 555-1000)
- `ProcessAnyFileRequest()` - File serving (lines 810-900)
- `GetFileLocationInfo()` - File resolution (lines 1250-1400)
- `ProcessCssFile()` - CSS processing (lines 1211-1302)
- `ProcessImageFileRequest()` - Image serving (lines 1009-1098)
- `MakeInMemoryHtmlFileInBookFolder()` - In-memory file creation (line 265)
- `RemoveInMemoryHtmlFile()` - In-memory file removal

**Data Structures**:
- `_inMemoryHtmlFiles`: `Dictionary<string, InMemoryHtmlFile>` (line 158)
- `InMemoryHtmlFile` class: Contains `Content` and `ExpirationTime`

## Notes

- BloomFileLocator is complex and searches multiple locations
- Image processing is CPU-intensive and should use caching
- In-memory files need proper cleanup to avoid memory leaks
- Windows path mapping needs security validation (prevent directory traversal)
- Book folder paths need current collection context
- Some paths are virtual/generated and don't exist on disk
