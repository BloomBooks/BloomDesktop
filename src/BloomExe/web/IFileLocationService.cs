using System;

namespace Bloom.web
{
    /// <summary>
    /// Service for locating and managing file paths in Bloom.
    /// Encapsulates BloomFileLocator and in-memory file management.
    /// Phase 6.2 Implementation.
    /// </summary>
    public interface IFileLocationService
    {
        /// <summary>
        /// Get the full path to a file in the browser root directory.
        /// </summary>
        /// <param name="relativePath">Relative path from browser root</param>
        /// <returns>Full file path, or null if not found</returns>
        string GetBrowserFile(string relativePath);

        /// <summary>
        /// Get the full path to a distributed file (from DistFiles/).
        /// </summary>
        /// <param name="filename">Filename to locate</param>
        /// <returns>Full file path, or null if not found</returns>
        string GetDistributedFile(string filename);

        /// <summary>
        /// Get the full path to a file in the current book folder.
        /// </summary>
        /// <param name="filename">Filename in book folder</param>
        /// <returns>Full file path, or null if not found or no current book</returns>
        string GetBookFile(string filename);

        /// <summary>
        /// Try to get an in-memory HTML file by its path.
        /// </summary>
        /// <param name="path">Path/key for the in-memory file</param>
        /// <param name="content">The HTML content if found and not expired</param>
        /// <returns>True if file found and not expired, false otherwise</returns>
        bool TryGetInMemoryFile(string path, out string content);

        /// <summary>
        /// Add or update an in-memory HTML file.
        /// </summary>
        /// <param name="path">Path/key for the in-memory file</param>
        /// <param name="content">HTML content</param>
        /// <param name="expirationTime">When the file expires (null for no expiration)</param>
        void AddInMemoryFile(string path, string content, DateTime? expirationTime = null);

        /// <summary>
        /// Remove an in-memory HTML file.
        /// </summary>
        /// <param name="path">Path/key for the in-memory file</param>
        /// <returns>True if file was removed, false if it didn't exist</returns>
        bool RemoveInMemoryFile(string path);

        /// <summary>
        /// Clean up expired in-memory files.
        /// </summary>
        /// <returns>Number of files removed</returns>
        int CleanupExpiredInMemoryFiles();

        /// <summary>
        /// Locate a file using BloomFileLocator's comprehensive search.
        /// </summary>
        /// <param name="filename">Filename to locate</param>
        /// <returns>Full file path, or null if not found</returns>
        string LocateFile(string filename);
    }
}
