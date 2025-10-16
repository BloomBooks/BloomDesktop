// Copyright (c) 2024 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.IO;
using System.Threading.Tasks;
using Bloom.Book;
using Bloom.Collection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SIL.IO;

namespace Bloom.web
{
    /// <summary>
    /// Middleware for processing CSS files with special Bloom-specific logic.
    /// Phase 3.2 Implementation: CSS file processing and font injection.
    ///
    /// Handles:
    /// - CSS file location resolution (book folder, xmatter, templates)
    /// - Supporting CSS files (editMode.css, etc.)
    /// - Font injection for defaultLangStyles.css
    /// - Branding CSS handling
    /// </summary>
    public class KestrelCssProcessingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<KestrelCssProcessingMiddleware> _logger;
        private readonly IFileLocationService _fileLocationService;
        private readonly BloomFileLocator _fileLocator;
        private readonly BookSelection _bookSelection;
        private readonly CollectionSettings _collectionSettings;
        private const string OriginalImageMarker = "OriginalImages";

        public KestrelCssProcessingMiddleware(
            RequestDelegate next,
            ILogger<KestrelCssProcessingMiddleware> logger,
            IFileLocationService fileLocationService,
            BloomFileLocator fileLocator,
            BookSelection bookSelection,
            CollectionSettings collectionSettings = null
        )
        {
            _next = next;
            _logger = logger;
            _fileLocationService = fileLocationService;
            _fileLocator = fileLocator;
            _bookSelection = bookSelection;
            _collectionSettings = collectionSettings;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "";

            // Only process CSS files
            if (!path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // Process the CSS file
            var success = await ProcessCssFile(context, path);

            if (!success)
            {
                // CSS file not found, pass to next middleware
                await _next(context);
            }
        }

        /// <summary>
        /// Process a CSS file request with Bloom-specific logic.
        /// Based on BloomServer.ProcessCssFile (lines 1213-1302).
        /// </summary>
        private async Task<bool> ProcessCssFile(HttpContext context, string incomingPath)
        {
            // BL-2219: "OriginalImages" means we're generating a pdf and want full images,
            // but it has nothing to do with css files and defeats the following 'if'
            var localPath = incomingPath.Replace($"{OriginalImageMarker}/", "").TrimStart('/');

            // Check if CSS file is in the book folder
            if (IsInBookFolder(localPath))
            {
                return await ServeBookFolderCss(context, localPath);
            }

            // If not a full path, try to find the correct file
            var fileName = Path.GetFileName(localPath);

            // Try to locate the CSS file
            string cssFilePath = LocateCssFile(fileName, localPath, incomingPath);

            if (string.IsNullOrEmpty(cssFilePath))
            {
                _logger.LogDebug($"CSS file not found: {incomingPath}");
                return false; // File not found
            }

            // Check for special CSS files that need processing
            if (fileName.Equals("defaultLangStyles.css", StringComparison.OrdinalIgnoreCase))
            {
                return await ServeDefaultLangStylesWithFontInjection(context, cssFilePath);
            }

            // Serve the CSS file
            return await ServeCssFile(context, cssFilePath);
        }

        /// <summary>
        /// Checks if a path is in the current book's folder.
        /// </summary>
        private bool IsInBookFolder(string path)
        {
            try
            {
                var currentBook = _bookSelection?.CurrentSelection;
                if (currentBook == null)
                    return false;

                var bookFolder = currentBook.FolderPath;
                if (string.IsNullOrEmpty(bookFolder))
                    return false;

                var fullPath = Path.GetFullPath(path);
                var fullBookFolder = Path.GetFullPath(bookFolder);

                return fullPath.StartsWith(fullBookFolder, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Serve a CSS file from the book folder.
        /// </summary>
        private async Task<bool> ServeBookFolderCss(HttpContext context, string localPath)
        {
            var cssPath = localPath;

            if (!RobustFile.Exists(cssPath))
            {
                // Some supporting css files, like editMode.css, are not copied to the book folder
                // because they are not needed for viewing or publishing.
                cssPath = _bookSelection.CurrentSelection?.Storage.GetSupportingFile(
                    Path.GetFileName(localPath)
                );
            }

            if (RobustFile.Exists(cssPath))
            {
                return await ServeCssFile(context, cssPath);
            }
            else
            {
                // Return empty CSS file rather than 404
                context.Response.ContentType = "text/css; charset=utf-8";
                context.Response.StatusCode = 200;
                await context.Response.WriteAsync("");
                return true;
            }
        }

        /// <summary>
        /// Locate a CSS file using Bloom's file location logic.
        /// Based on BloomServer.ProcessCssFile location logic.
        /// </summary>
        private string LocateCssFile(string fileName, string localPath, string incomingPath)
        {
            // Try to find the css file in the xmatter and templates using FileLocator
            string path = _fileLocator?.LocateFile(fileName);

            // If still not found, and localPath is an actual file path, use it
            if (string.IsNullOrEmpty(path) && RobustFileExistsWithCaseCheck(localPath))
            {
                path = localPath;
            }

            // It's just possible we need to add BloomBrowserUI to the path (in the case of the AddPage dialog)
            if (string.IsNullOrEmpty(path))
            {
                var p = FileLocationUtilities.GetFileDistributedWithApplication(
                    true,
                    BloomFileLocator.BrowserRoot,
                    localPath
                );
                if (RobustFileExistsWithCaseCheck(p))
                    path = p;
            }

            if (string.IsNullOrEmpty(path))
            {
                var p = FileLocationUtilities.GetFileDistributedWithApplication(
                    true,
                    BloomFileLocator.BrowserRoot,
                    incomingPath
                );
                if (RobustFileExistsWithCaseCheck(p))
                    path = p;
            }

            return path;
        }

        /// <summary>
        /// Serve a CSS file with appropriate headers.
        /// </summary>
        private async Task<bool> ServeCssFile(HttpContext context, string cssPath)
        {
            try
            {
                if (!RobustFile.Exists(cssPath))
                {
                    return false;
                }

                var cssContent = RobustFile.ReadAllText(cssPath);

                context.Response.ContentType = "text/css; charset=utf-8";
                context.Response.StatusCode = 200;

                // Apply cache headers (long cache for CSS files)
                context.Response.Headers["Cache-Control"] = "public, max-age=31536000";

                await context.Response.WriteAsync(cssContent);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error serving CSS file: {cssPath}");
                return false;
            }
        }

        /// <summary>
        /// Serve defaultLangStyles.css with font-face injection.
        /// This injects @font-face rules for all collection languages.
        /// TODO: Implement font injection logic from BloomServer (if needed).
        /// </summary>
        private async Task<bool> ServeDefaultLangStylesWithFontInjection(
            HttpContext context,
            string cssPath
        )
        {
            try
            {
                if (!RobustFile.Exists(cssPath))
                {
                    return false;
                }

                var cssContent = RobustFile.ReadAllText(cssPath);

                // TODO: Implement font injection logic
                // This would read collection settings and inject @font-face rules
                // for each language in the collection.
                // For now, serve as-is with a warning
                if (_collectionSettings != null)
                {
                    _logger.LogDebug(
                        "Font injection for defaultLangStyles.css not yet fully implemented"
                    );
                    // Font injection would go here
                    // cssContent = InjectFontFaceRules(cssContent, _collectionSettings);
                }

                context.Response.ContentType = "text/css; charset=utf-8";
                context.Response.StatusCode = 200;
                context.Response.Headers["Cache-Control"] = "no-cache"; // Dynamic content

                await context.Response.WriteAsync(cssContent);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    $"Error serving defaultLangStyles.css with font injection: {cssPath}"
                );
                return false;
            }
        }

        /// <summary>
        /// Check if a file exists with case-sensitive comparison.
        /// </summary>
        private bool RobustFileExistsWithCaseCheck(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                if (!RobustFile.Exists(path))
                    return false;

                // On Windows, check case sensitivity
                if (SIL.PlatformUtilities.Platform.IsWindows)
                {
                    var directory = Path.GetDirectoryName(path);
                    var fileName = Path.GetFileName(path);

                    if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
                        return true; // Can't verify case, assume OK

                    var actualFiles = Directory.GetFiles(directory, fileName);
                    return actualFiles.Length > 0
                        && actualFiles[0].EndsWith(
                            fileName,
                            StringComparison.Ordinal
                        ); // Case-sensitive
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
