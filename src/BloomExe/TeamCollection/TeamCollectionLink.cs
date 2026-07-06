using System;
using SIL.IO;

namespace Bloom.TeamCollection
{
    /// <summary>
    /// Parses and writes the content of TeamCollectionLink.txt, which can contain either:
    ///   - A folder path (legacy folder-backed TC), e.g. "C:\Dropbox\MyCollection - TC"
    ///   - A cloud URI, e.g. "cloud://sil.bloom/collection/&lt;collectionId&gt;" (GUID)
    ///
    /// The two forms are distinguished by whether the content starts with "cloud://sil.bloom/collection/".
    /// If the file is missing or empty this class treats the collection as not a TC.
    /// If the content is ambiguous or unparseable it throws <see cref="InvalidTeamCollectionLinkException"/>.
    /// </summary>
    public class TeamCollectionLink
    {
        /// <summary>URI scheme + authority prefix used by cloud-backed TCs.</summary>
        public const string CloudUriPrefix = "cloud://sil.bloom/collection/";

        /// <summary>The folder path; non-null only for folder-backed (legacy) TCs.</summary>
        public string RepoFolderPath { get; }

        /// <summary>The collection ID GUID; non-null only for cloud-backed TCs.</summary>
        public string CloudCollectionId { get; }

        /// <summary>True when this link points to a cloud-backed TC.</summary>
        public bool IsCloud => CloudCollectionId != null;

        /// <summary>True when this link points to a folder-backed TC.</summary>
        public bool IsFolder => RepoFolderPath != null;

        private TeamCollectionLink(string repoFolderPath, string cloudCollectionId)
        {
            RepoFolderPath = repoFolderPath;
            CloudCollectionId = cloudCollectionId;
        }

        /// <summary>
        /// Creates a <see cref="TeamCollectionLink"/> for a folder-backed TC.
        /// </summary>
        /// <param name="folderPath">Absolute path to the shared repo folder.</param>
        public static TeamCollectionLink ForFolder(string folderPath)
        {
            return new TeamCollectionLink(folderPath, null);
        }

        /// <summary>
        /// Creates a <see cref="TeamCollectionLink"/> for a cloud-backed TC.
        /// </summary>
        /// <param name="collectionId">The collection GUID that identifies this TC on the server.</param>
        public static TeamCollectionLink ForCloud(string collectionId)
        {
            return new TeamCollectionLink(null, collectionId);
        }

        /// <summary>
        /// Read and parse the TeamCollectionLink.txt file at <paramref name="linkFilePath"/>.
        /// Returns null if the file does not exist.
        /// Throws <see cref="InvalidTeamCollectionLinkException"/> when the content is
        /// present but cannot be classified as a valid folder path or cloud URI.
        /// </summary>
        /// <param name="linkFilePath">Full path to TeamCollectionLink.txt.</param>
        public static TeamCollectionLink FromFile(string linkFilePath)
        {
            if (!RobustFile.Exists(linkFilePath))
                return null;

            var raw = RobustFile.ReadAllText(linkFilePath).Trim();
            return Parse(raw);
        }

        /// <summary>
        /// Parse a raw string from TeamCollectionLink.txt.
        /// Returns null for empty/whitespace input (treated as "not a TC").
        /// Throws <see cref="InvalidTeamCollectionLinkException"/> when the content is present
        /// but cannot be classified as a valid folder path or cloud URI.
        /// </summary>
        /// <param name="rawContent">Trimmed text content of the link file.</param>
        public static TeamCollectionLink Parse(string rawContent)
        {
            if (string.IsNullOrWhiteSpace(rawContent))
                return null;

            if (rawContent.StartsWith(CloudUriPrefix, StringComparison.Ordinal))
            {
                var id = rawContent.Substring(CloudUriPrefix.Length).Trim();
                if (string.IsNullOrEmpty(id))
                    throw new InvalidTeamCollectionLinkException(
                        $"Cloud TC link missing collection ID: '{rawContent}'"
                    );

                // The ID must look like a non-empty string with no embedded whitespace.
                // We do not enforce GUID format here so tests can use short IDs.
                if (id.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0)
                    throw new InvalidTeamCollectionLinkException(
                        $"Cloud TC link has whitespace in collection ID: '{rawContent}'"
                    );

                return new TeamCollectionLink(null, id);
            }

            // Check for a "cloud://" URI with a different authority — that is unexpected
            // content we should flag rather than silently treat as a folder path.
            if (rawContent.StartsWith("cloud://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidTeamCollectionLinkException(
                    $"Unrecognized cloud TC link format: '{rawContent}'"
                );

            // Treat everything else as a folder path (legacy behavior).
            return new TeamCollectionLink(rawContent, null);
        }

        /// <summary>
        /// Serializes this link to the string form written into TeamCollectionLink.txt.
        /// </summary>
        public string ToFileContent()
        {
            if (IsCloud)
                return CloudUriPrefix + CloudCollectionId;
            return RepoFolderPath;
        }

        /// <summary>
        /// Write this link to the specified file, creating or overwriting it.
        /// </summary>
        /// <param name="linkFilePath">Full path to TeamCollectionLink.txt.</param>
        public void WriteToFile(string linkFilePath)
        {
            RobustFile.WriteAllText(linkFilePath, ToFileContent());
        }
    }

    /// <summary>
    /// Thrown by <see cref="TeamCollectionLink.Parse"/> or
    /// <see cref="TeamCollectionLink.FromFile"/> when the link-file content is present
    /// but cannot be interpreted as a valid folder path or cloud URI.
    /// </summary>
    public class InvalidTeamCollectionLinkException : Exception
    {
        /// <inheritdoc />
        public InvalidTeamCollectionLinkException(string message)
            : base(message) { }
    }
}
