# Kestrel Migration - Phase 1 Analysis Results

## Overview
Completed comprehensive analysis of the existing `BloomServer` implementation and mapped out the architecture for Kestrel migration. This document serves as the checkpoint completion for Phase 1.

## Current Architecture Analysis

### Core Server Components
1. **BloomServer.cs** (2,271 lines)
   - **Pattern**: Singleton pattern with static instance `_theOneInstance`
   - **Port Discovery**: Tries ports 8089-8099 via `AttemptToOpenPort()`
   - **Threading Model**:
     - 1 listener thread (`_listenerThread`) accepting incoming requests
     - Pool of worker threads (`_workers` - ConcurrentDictionary)
     - Queue-based request distribution (`_queue`)
     - Recursive request tracking (`_threadsDoingRecursiveRequests`)
     - Thread blocking tracking (`_countBlockedThreads`)
   - **Key Methods**:
     - `EnsureListening()`: Starts server (lines 1301-1342)
     - `ProcessRequestAsync()`: Main routing logic (lines 431-700+)
     - `ProcessImageFileRequest()`: Image caching/serving (lines 976+)
     - `ProcessCssFile()`: CSS file serving with font injection (lines 1211+)

### Key Features to Migrate
1. **In-Memory HTML Files** (`_urlToSimulatedPageContent` dictionary)
   - Used for page thumbnails and editing
   - Static dictionary with GUIDs and markers
   - Cleaned up via idle tasks

2. **Image Processing**
   - `RuntimeImageProcessor` for caching/resizing
   - Thumbnail generation with recursive requests
   - `OriginalImageMarker` prefix for unprocessed images

3. **Special URL Handling**
   - Book preview (`book-preview/`)
   - Edit page content (`page-memsim-*.html`)
   - Writing system styles
   - Simulated file URLs

4. **API Handler Registration**
   - `BloomApiHandler` manages routing
   - Pattern-based endpoint registration
   - Synchronization options (`requiresSync`, `handleOnUiThread`)

## API Handlers Inventory (38+ endpoints)

### By Category
**Common/Application:**
- CommonApi (15+ endpoints: uiLanguages, currentUiLanguage, error logging, etc.)
- AppApi (app-level operations)
- CollectionApi (collection operations)
- WorkspaceApi (workspace state)

**Books & Pages:**
- BookCommandsApi
- BookMetadataApi
- BookSettingsApi
- PageTemplatesApi
- AddOrChangePageApi
- EditingViewApi

**Media & Content:**
- ImageApi
- AudioSegmentationApi
- MusicApi
- TalkingBookApi
- SignLanguageApi

**Styling & Fonts:**
- StylesAndFontsApi
- KeyboardingConfigApi

**Publishing & Export:**
- PublishApi
- LibraryPublishApi
- ExternalApi

**Settings & Configuration:**
- CollectionSettingsApi
- BrandingSettings
- SubscriptionSettingsEditorApi
- FeatureStatusApi

**Specialized:**
- AccessibilityCheckApi
- IndicatorInfoApi
- OrthographyConverter
- ProblemReportApi
- ProgressDialogApi
- LoggerApi
- RegistrationApi
- FileIOApi
- ExternalLinkController
- ServerHandlerForBloomPlayer

### Registration Pattern
```csharp
// Each API class has:
public class XxxApi {
    public void RegisterWithApiHandler(BloomApiHandler apiHandler) {
        apiHandler.RegisterEndpointHandler("api/path", HandlerMethod, handleOnUiThread, requiresSync);
    }
}
```

## Request Flow Architecture

### Current Request Processing (BloomServer.ProcessRequestAsync)

```
1. Root paths (/):  → Return Bloom UI HTML
2. Special paths:
   - /testconnection → "OK"
   - /test-dialog → Show dialog
   - /book-preview/* → Book preview serving
3. API Handler check (↓)
   - If /bloom/api/* → BloomApiHandler.ProcessRequestAsync()
4. Bloom Player handler (↓)
   - ServerHandlerForBloomPlayer
5. Image files (↓)
   - ProcessImageFileRequest()
   - Handle OriginalImageMarker
   - Cache/resize with RuntimeImageProcessor
6. CSS files (↓)
   - ProcessCssFile()
   - Inject font declarations
7. In-memory simulated pages (↓)
   - Check _urlToSimulatedPageContent dictionary
8. Current page content (↓)
   - EditingModel.GetEditPageIframeContents()
9. Static files (↓)
   - ProcessAnyFileContent()
   - Locate via BloomFileLocator
   - Handle source maps, special cases
10. Error → 404
```

## Threading Model Analysis

### Current Approach
- **Listener Thread**: Accepts connections, queues requests
- **Worker Pool**: Configurable pool (min 2, max based on processors)
- **Recursive Request Handling**: Thumbnail generation that triggers nested requests
- **Blocked Thread Tracking**: Detects when UI operations block server threads
- **Manual Thread Management**: SpinUpAWorker, thread pool tuning

### Challenges for Kestrel Migration
1. **Recursive request detection** - Currently uses `HttpListenerContext` query string
2. **UI thread marshaling** - `handleOnUiThread` needs `SynchronizationContext`
3. **Blocked thread detection** - `RegisterThreadBlocking()`/`RegisterThreadUnblocked()`
4. **Thread pool sizing** - Kestrel manages this, may need tuning

## Static Resources & Special Handlers

### Static Files
- **Browser Root**: `BloomFileLocator.BrowserRoot` (JS, CSS, images)
- **Distribution Files**: `DistFiles/` directory
- **Book Folders**: Dynamic, depends on current book
- **Special Cases**:
  - `favicon.ico`
  - `video-placeholder.svg`
  - `widget-placeholder.svg`
  - Source map files (`.map`)

### Content Type Mapping
Handled in `BloomServer.GetContentType()`:
- Text: `.css`, `.html`, `.htm`, `.txt`, `.xml`, `.xhtml`
- Images: `.gif`, `.jpg`, `.jpeg`, `.png`, `.svg`
- Media: `.mp3`, `.ogg`
- Fonts: `.woff`, `.woff2`
- Application: `.pdf`, `.js`

## Key Dependencies & Interfaces

### IRequestInfo Interface (see RequestInfo.cs)
Abstracts HTTP request/response:
- `LocalPathWithoutQuery`: URL path
- `RawUrl`: Full URL
- `HttpMethod`: GET, POST, etc.
- `RequestContentType`: Content-Type header
- `ResponseContentType`: Set response content type
- `GetQueryParameters()`: Parse query string
- `GetPostData()`: Parse POST body
- `WriteCompleteOutput()`: Send response
- `ReplyWithFileContent()`: Stream file
- `ReplyWithImage()`: Stream image
- `ReplyWithStreamContent()`: Stream generic content
- `WriteError()`: Send error response
- `WriteRedirect()`: Send redirect
- Methods for cookie/header manipulation

### IBloomServer Interface (minimal)
```csharp
public interface IBloomServer
{
    void RegisterThreadBlocking();
    void RegisterThreadUnblocked();
}
```

### Core Services
- `BookSelection`: Currently selected book
- `CollectionSettings`: Current collection configuration
- `RuntimeImageProcessor`: Image caching/processing
- `BloomFileLocator`: File location resolution
- `BloomApiHandler`: API routing

## Synchronization Requirements

### Handlers with `handleOnUiThread = true`
Need to marshal to UI thread before execution:
- Common operations modifying book state
- Settings dialog showing
- Page rethinking
- Various save operations

### Handlers with `requiresSync = false`
Can run concurrently (fewer blocking issues):
- Log event handlers
- Simple read-only operations
- Status checks

## Error Handling & Logging

### Missing Files
- Different reporting levels based on file type
- Special handling for:
  - Book folder files (silent)
  - Optional image markers
  - In-memory pages
  - Deleted books
  - Audio files (not yet recorded)

### Logging
- Uses `Logger.WriteEvent()`, `Logger.WriteMinorEvent()`
- Problem reporting via `NonFatalProblem.Report()`
- Analytics via `Analytics.ReportException()`

## Implementation Considerations

### Port Discovery
- Tries 10 ports starting at 8089, incrementing by 2 (for WebSocket)
- Handles `HttpListenerException` and `SocketException`
- Verifies listening with test connection

### In-Memory Pages
- Keys stored in dictionary with special markers
- Idle task queue for deferred cleanup
- Race condition handling for concurrent requests

### Recursive Requests
- Detected via `generateThumbnailIfNecessary=true` query parameter
- Separate thread pool accounting to prevent deadlock
- Extra workers spun up when recursive requests detected

### Cache Management
- `_useCache` flag (currently always true)
- `RuntimeImageProcessor.GetPathToAdjustedImage()`
- Separate handling for cover images vs regular images

## Identified Risks & Challenges

1. **UI Thread Marshaling**: WinForms + async Kestrel middleware combination
2. **Recursive Request Handling**: Thumbnail generation during page navigation
3. **Blocked Thread Detection**: Needs equivalent in Kestrel
4. **In-Memory Page Cleanup**: Race conditions during disposal
5. **Image Processing Thread Pool**: Tuning for concurrent requests
6. **WebSocket Integration**: Current separate server (BloomWebSocketServer.cs)
7. **Static Singleton Pattern**: Dependency on `_theOneInstance`

## Files to Create (Phase 2-3)

### Core Files
- `KestrelBloomServer.cs` - ASP.NET Core host wrapper
- `KestrelRequestAdapter.cs` - IRequestInfo implementation for HttpContext
- `KestrelApiMiddleware.cs` - Request routing middleware

### Middleware Components
- `InMemoryPageMiddleware.cs` - In-memory page serving
- `ImageServingMiddleware.cs` - Image processing and caching
- `BookPreviewMiddleware.cs` - Book preview special paths
- `StaticFileMiddleware.cs` - Static file configuration (or use built-in)

### Configuration
- `ServiceCollectionExtensions.cs` - DI setup
- `appsettings.json` - Optional configuration

### Tests
- `KestrelServerBasicTests.cs` - Phase 2.1 checkpoint
- `KestrelMiddlewareTests.cs` - Phase 2.2 checkpoint
- `KestrelRequestInfoTests.cs` - Phase 3 checkpoint
- Additional test suites per plan

## Files to Modify

### Required Changes
- `RequestInfo.cs` - Update to work with HttpContext (keep IRequestInfo interface)
- `ApplicationContainer.cs` - Add Kestrel setup option
- `Program.cs` - Initialize appropriate server (feature flag)

### Maintain Compatibility
- `BloomApiHandler.cs` - No changes (routing system remains)
- `IRequestInfo.cs` - No changes (interface preserved)
- All API handler classes - No changes initially

## Success Criteria for Phase 1

- ✅ All 38+ API handler classes identified and cataloged
- ✅ Request routing flow documented
- ✅ Threading model understood
- ✅ Special cases mapped (in-memory, recursive, image processing, etc.)
- ✅ Risks and challenges identified
- ✅ Implementation approach clear
- ✅ Ready to proceed to Phase 2

## Appendix: API Handler Details

### Endpoints by File (count)

```
AccessibilityCheckApi.cs        1 endpoint (accessibility check)
AddOrChangePageApi.cs           3+ endpoints (page operations)
AppApi.cs                       5+ endpoints (app lifecycle)
AudioSegmentationApi.cs         2+ endpoints (audio processing)
BookCommandsApi.cs              5+ endpoints (book operations)
BookMetadataApi.cs              3+ endpoints (metadata)
BookSettingsApi.cs              3+ endpoints (settings)
BrandingSettings.cs             2+ endpoints (branding)
CollectionApi.cs                3+ endpoints (collection operations)
CollectionSettingsApi.cs        2+ endpoints (collection settings)
CommonApi.cs                    15+ endpoints (common utilities)
CopyrightAndLicenseApi.cs       2+ endpoints (copyright)
EditingViewApi.cs               5+ endpoints (editing)
ExternalApi.cs                  2+ endpoints (external)
ExternalLinkController.cs       1+ endpoints (links)
FeatureStatusApi.cs             2+ endpoints (features)
FileIOApi.cs                    3+ endpoints (file operations)
ImageApi.cs                     2+ endpoints (images)
IndicatorInfoApi.cs             1+ endpoints (indicators)
KeyboardingConfigApi.cs         2+ endpoints (keyboard)
LanguageChangeEventArgs.cs      (event class)
LibraryPublishApi.cs            3+ endpoints (library)
LoggerApi.cs                    2+ endpoints (logging)
MusicApi.cs                     2+ endpoints (music)
OrthographyConverter.cs         1+ endpoint (orthography)
PageTemplatesApi.cs             3+ endpoints (templates)
ProblemReportApi.cs             2+ endpoints (problem reporting)
ProgressDialogApi.cs            2+ endpoints (progress)
PublishApi.cs                   8+ endpoints (publishing)
RegistrationApi.cs              2+ endpoints (registration)
ServerHandlerForBloomPlayer.cs   1 endpoint (bloom player)
SignLanguageApi.cs              2+ endpoints (sign language)
StylesAndFontsApi.cs            3+ endpoints (fonts)
SubscriptionSettingsEditorApi.cs 2+ endpoints (subscriptions)
TalkingBookApi.cs               2+ endpoints (audio books)
ToolboxApi.cs                   2+ endpoints (toolbox)
WorkspaceApi.cs                 3+ endpoints (workspace)

Total: 100+ API endpoints
```

## Next Steps

→ Proceed to **Phase 2: Core Server Migration**
- Create `KestrelBloomServer.cs` with basic bootstrap and port discovery
- Implement lifecycle management (EnsureListening, Stop, Dispose)
- Write Phase 2.1 unit tests for port discovery and server lifecycle
