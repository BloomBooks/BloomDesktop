using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Book;
using SIL.IO;

namespace Bloom.web
{
    /// <summary>
    /// Implementation of IFileLocationService that wraps BloomFileLocator
    /// and manages in-memory files.
    /// Phase 6.2 Implementation.
    /// </summary>
    public class FileLocationService : IFileLocationService
    {
        private readonly BloomFileLocator _fileLocator;
        private readonly BookSelection _bookSelection;
        private readonly Dictionary<string, InMemoryHtmlFile> _inMemoryFiles;
        private readonly object _inMemoryFilesLock = new object();

        public FileLocationService(BloomFileLocator fileLocator, BookSelection bookSelection)
        {
            _fileLocator = fileLocator;
            _bookSelection = bookSelection;
            _inMemoryFiles = new Dictionary<string, InMemoryHtmlFile>();
        }

        /// <inheritdoc/>
        public string GetBrowserFile(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return null;

            var browserRoot = BloomFileLocator.BrowserRoot;
            if (string.IsNullOrEmpty(browserRoot))
                return null;

            var fullPath = Path.Combine(browserRoot, relativePath.TrimStart('/', '\\'));
            return RobustFile.Exists(fullPath) ? fullPath : null;
        }

        /// <inheritdoc/>
        public string GetDistributedFile(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return null;

            // Use FileLocator for comprehensive search
            return _fileLocator?.LocateFile(filename);
        }

        /// <inheritdoc/>
        public string GetBookFile(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return null;

            var currentBook = _bookSelection?.CurrentSelection;
            if (currentBook == null)
                return null;

            var bookFolder = currentBook.FolderPath;
            if (string.IsNullOrEmpty(bookFolder))
                return null;

            var fullPath = Path.Combine(bookFolder, filename);
            return RobustFile.Exists(fullPath) ? fullPath : null;
        }

        /// <inheritdoc/>
        public bool TryGetInMemoryFile(string path, out string content)
        {
            content = null;

            if (string.IsNullOrEmpty(path))
                return false;

            lock (_inMemoryFilesLock)
            {
                if (!_inMemoryFiles.TryGetValue(path, out var file))
                    return false;

                // Check if expired
                if (file.ExpirationTime.HasValue && DateTime.Now > file.ExpirationTime.Value)
                {
                    _inMemoryFiles.Remove(path);
                    return false;
                }

                content = file.Content;
                return true;
            }
        }

        /// <inheritdoc/>
        public void AddInMemoryFile(string path, string content, DateTime? expirationTime = null)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));

            lock (_inMemoryFilesLock)
            {
                _inMemoryFiles[path] = new InMemoryHtmlFile
                {
                    Content = content ?? string.Empty,
                    ExpirationTime = expirationTime,
                };
            }
        }

        /// <inheritdoc/>
        public bool RemoveInMemoryFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            lock (_inMemoryFilesLock)
            {
                return _inMemoryFiles.Remove(path);
            }
        }

        /// <inheritdoc/>
        public int CleanupExpiredInMemoryFiles()
        {
            var now = DateTime.Now;
            var count = 0;

            lock (_inMemoryFilesLock)
            {
                var expiredKeys = _inMemoryFiles
                    .Where(kvp =>
                        kvp.Value.ExpirationTime.HasValue && now > kvp.Value.ExpirationTime.Value
                    )
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    _inMemoryFiles.Remove(key);
                    count++;
                }
            }

            return count;
        }

        /// <inheritdoc/>
        public string LocateFile(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return null;

            return _fileLocator?.LocateFile(filename);
        }
    }

    /// <summary>
    /// Represents an HTML file stored in memory with optional expiration.
    /// </summary>
    public class InMemoryHtmlFile
    {
        public string Content { get; set; }
        public DateTime? ExpirationTime { get; set; }
    }
}
