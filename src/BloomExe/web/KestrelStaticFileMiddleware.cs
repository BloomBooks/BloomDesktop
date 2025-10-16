// Copyright (c) 2024 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Bloom.ImageProcessing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SIL.IO;

namespace Bloom.web
{
    /// <summary>
    /// Middleware for serving static files in the Bloom Kestrel server.
    /// Handles:
    /// - In-memory files (simulated pages, previews)
    /// - Browser root files (UI resources)
    /// - Book folder files (user content)
    /// - Distribution files (system resources)
    /// - Windows path mapping (C$/ → C:\)
    /// - Image processing (thumbnails, caching)
    /// - CSS processing (font injection)
    /// </summary>
    public class KestrelStaticFileMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IFileLocationService _fileLocationService;
        private readonly RuntimeImageProcessor _imageProcessor;
        private readonly ILogger<KestrelStaticFileMiddleware> _logger;

        public KestrelStaticFileMiddleware(
            RequestDelegate next,
            IFileLocationService fileLocationService,
            RuntimeImageProcessor imageProcessor,
            ILogger<KestrelStaticFileMiddleware> logger
        )
        {
            _next = next;
            _fileLocationService = fileLocationService;
            _imageProcessor = imageProcessor;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "";

            // 1. Check in-memory files first (highest priority)
            if (_fileLocationService.TryGetInMemoryFile(path, out var inMemoryContent))
            {
                await ServeInMemoryFile(context, inMemoryContent);
                return;
            }

            // 2. Handle Windows path mapping: /C$/path → C:\path
            if (path.Contains("/C$/", StringComparison.OrdinalIgnoreCase))
            {
                var windowsPath = ConvertToWindowsPath(path);
                if (!string.IsNullOrEmpty(windowsPath) && RobustFile.Exists(windowsPath))
                {
                    await ServePhysicalFile(context, windowsPath);
                    return;
                }
            }

            // 3. Handle image files with processing
            if (IsImageFile(path))
            {
                var imagePath = await TryGetImagePath(path);
                if (!string.IsNullOrEmpty(imagePath) && RobustFile.Exists(imagePath))
                {
                    await ServeImageFile(context, imagePath);
                    return;
                }
            }

            // 4. Handle CSS files (may need font injection)
            if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                var cssPath = await TryGetCssPath(path);
                if (!string.IsNullOrEmpty(cssPath) && RobustFile.Exists(cssPath))
                {
                    await ServeCssFile(context, cssPath);
                    return;
                }
            }

            // 5. Try to locate file using file location service
            var filePath = _fileLocationService.LocateFile(path);
            if (!string.IsNullOrEmpty(filePath) && RobustFile.Exists(filePath))
            {
                await ServePhysicalFile(context, filePath);
                return;
            }

            // 6. File not found, pass to next middleware
            await _next(context);
        }

        #region File Serving Methods

        /// <summary>
        /// Serves an in-memory file (dynamically generated content).
        /// </summary>
        private async Task ServeInMemoryFile(HttpContext context, string content)
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.StatusCode = 200;

            // Add cache headers (no-cache for dynamic content)
            context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";

            await context.Response.WriteAsync(content);
        }

        /// <summary>
        /// Serves a physical file from the file system.
        /// </summary>
        private async Task ServePhysicalFile(HttpContext context, string filePath)
        {
            var contentType = GetContentType(filePath);
            context.Response.ContentType = contentType;
            context.Response.StatusCode = 200;

            // Apply cache headers based on file type
            ApplyCacheHeaders(context, filePath);

            // Set content length
            var fileInfo = new FileInfo(filePath);
            context.Response.ContentLength = fileInfo.Length;

            // Stream file to response
            await using var fileStream = RobustFile.OpenRead(filePath);
            await fileStream.CopyToAsync(context.Response.Body);
        }

        /// <summary>
        /// Serves an image file, potentially with processing/caching.
        /// </summary>
        private async Task ServeImageFile(HttpContext context, string imagePath)
        {
            // Check if this is a request for the original image (bypass processing)
            var path = context.Request.Path.Value ?? "";
            var isOriginalImage = path.Contains(
                "OriginalImages",
                StringComparison.OrdinalIgnoreCase
            );

            if (!isOriginalImage && _imageProcessor != null)
            {
                // Check if we should generate a thumbnail
                var generateThumbnail = context.Request.Query.ContainsKey(
                    "generateThumbnailIfNecessary"
                );

                if (generateThumbnail)
                {
                    // Get processed/cached image path
                    var processedPath = _imageProcessor.GetPathToAdjustedImage(imagePath);
                    if (!string.IsNullOrEmpty(processedPath) && RobustFile.Exists(processedPath))
                    {
                        imagePath = processedPath;
                    }
                }
            }

            // Serve the image file
            await ServePhysicalFile(context, imagePath);
        }

        /// <summary>
        /// Serves a CSS file, potentially with font injection.
        /// </summary>
        private async Task ServeCssFile(HttpContext context, string cssPath)
        {
            // Check if this is defaultLangStyles.css (needs font injection)
            var fileName = Path.GetFileName(cssPath);
            if (fileName.Equals("defaultLangStyles.css", StringComparison.OrdinalIgnoreCase))
            {
                // TODO: Implement font injection logic from BloomServer.ProcessCssFile()
                // For now, serve as regular file
                _logger.LogWarning("Font injection for defaultLangStyles.css not yet implemented");
            }

            await ServePhysicalFile(context, cssPath);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Converts a URL path with /C$/ to a Windows file path.
        /// Example: /C$/Users/file.txt → C:\Users\file.txt
        /// </summary>
        private string ConvertToWindowsPath(string urlPath)
        {
            try
            {
                // Decode URL encoding
                var decodedPath = WebUtility.UrlDecode(urlPath);

                // Find the drive letter pattern: /C$/
                var driveIndex = decodedPath.IndexOf("/C$/", StringComparison.OrdinalIgnoreCase);
                if (driveIndex == -1)
                    return null;

                // Extract drive letter (character before $)
                var driveLetter = decodedPath.Substring(driveIndex + 1, 1);

                // Get the path after /C$/
                var pathAfterDrive = decodedPath.Substring(driveIndex + 4);

                // Construct Windows path: C:\path
                var windowsPath = $"{driveLetter}:{pathAfterDrive.Replace('/', '\\')}";

                // Security check: prevent directory traversal
                if (windowsPath.Contains(".."))
                {
                    _logger.LogWarning($"Blocked potential directory traversal attempt: {urlPath}");
                    return null;
                }

                return windowsPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error converting URL path to Windows path: {urlPath}");
                return null;
            }
        }

        /// <summary>
        /// Checks if the path represents an image file.
        /// </summary>
        private bool IsImageFile(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".png"
                || extension == ".jpg"
                || extension == ".jpeg"
                || extension == ".gif"
                || extension == ".bmp"
                || extension == ".svg";
        }

        /// <summary>
        /// Tries to get the physical path for an image file.
        /// </summary>
        private async Task<string> TryGetImagePath(string path)
        {
            // Remove leading slash
            var relativePath = path.TrimStart('/');

            // Try different locations
            string imagePath;

            // 1. Try as book file
            imagePath = _fileLocationService.GetBookFile(relativePath);
            if (!string.IsNullOrEmpty(imagePath) && RobustFile.Exists(imagePath))
                return imagePath;

            // 2. Try as browser file
            imagePath = _fileLocationService.GetBrowserFile(relativePath);
            if (!string.IsNullOrEmpty(imagePath) && RobustFile.Exists(imagePath))
                return imagePath;

            // 3. Try as distributed file
            imagePath = _fileLocationService.GetDistributedFile(relativePath);
            if (!string.IsNullOrEmpty(imagePath) && RobustFile.Exists(imagePath))
                return imagePath;

            // 4. Try generic locate
            imagePath = _fileLocationService.LocateFile(relativePath);
            if (!string.IsNullOrEmpty(imagePath) && RobustFile.Exists(imagePath))
                return imagePath;

            return null;
        }

        /// <summary>
        /// Tries to get the physical path for a CSS file.
        /// </summary>
        private async Task<string> TryGetCssPath(string path)
        {
            // Remove leading slash
            var relativePath = path.TrimStart('/');

            // Try different locations
            string cssPath;

            // 1. Try as book file
            cssPath = _fileLocationService.GetBookFile(relativePath);
            if (!string.IsNullOrEmpty(cssPath) && RobustFile.Exists(cssPath))
                return cssPath;

            // 2. Try as browser file
            cssPath = _fileLocationService.GetBrowserFile(relativePath);
            if (!string.IsNullOrEmpty(cssPath) && RobustFile.Exists(cssPath))
                return cssPath;

            // 3. Try generic locate
            cssPath = _fileLocationService.LocateFile(relativePath);
            if (!string.IsNullOrEmpty(cssPath) && RobustFile.Exists(cssPath))
                return cssPath;

            return null;
        }

        /// <summary>
        /// Gets the MIME content type for a file based on its extension.
        /// </summary>
        private string GetContentType(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            return extension switch
            {
                // Text
                ".html" or ".htm" => "text/html; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".js" => "application/javascript; charset=utf-8",
                ".json" => "application/json; charset=utf-8",
                ".txt" => "text/plain; charset=utf-8",
                ".xml" => "text/xml; charset=utf-8",
                ".xhtml" => "application/xhtml+xml; charset=utf-8",
                // Images
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                // Audio/Video
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".ogg" => "audio/ogg",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                // Fonts
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                ".ttf" => "font/ttf",
                ".otf" => "font/otf",
                ".eot" => "application/vnd.ms-fontobject",
                // Documents
                ".pdf" => "application/pdf",
                ".zip" => "application/zip",
                // Default
                _ => "application/octet-stream",
            };
        }

        /// <summary>
        /// Applies appropriate cache headers based on file type.
        /// </summary>
        private void ApplyCacheHeaders(HttpContext context, string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            // Long cache for static assets (1 year)
            if (
                extension == ".js"
                || extension == ".css"
                || extension == ".woff"
                || extension == ".woff2"
                || extension == ".ttf"
                || extension == ".otf"
            )
            {
                context.Response.Headers["Cache-Control"] = "public, max-age=31536000";
            }
            // Medium cache for images (1 day)
            else if (
                extension == ".png"
                || extension == ".jpg"
                || extension == ".jpeg"
                || extension == ".gif"
                || extension == ".svg"
            )
            {
                context.Response.Headers["Cache-Control"] = "public, max-age=86400";
            }
            // Short cache for HTML/dynamic content (1 minute)
            else if (extension == ".html" || extension == ".htm")
            {
                context.Response.Headers["Cache-Control"] = "no-cache, max-age=60";
            }
            // No cache for unknown types
            else
            {
                context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            }
        }

        #endregion
    }
}
