using System;
using System.IO;
using BloomTemp;
using SIL.IO;

namespace Bloom.Publish.BloomPub
{
    /// <summary>
    /// Where Bloom stages the files of a BloomPUB it is making, both for the preview in the
    /// Publish tab and for a real .bloompub. Each BloomPUB gets its own folder,
    /// %TEMP%\bps\&lt;five random characters&gt;, so that two BloomPUBs being made at once,
    /// whether by one Bloom or by several, never share (and delete) each other's files.
    /// </summary>
    /// <remarks>
    /// The parent folder deliberately does not start with "bloom": TempFile.CleanupTempFolder,
    /// which the Bloom holding the single-instance token runs on exit, deletes everything in
    /// %TEMP% that does, including folders another running Bloom (one started with Ctrl held
    /// down, or an automation instance) is still using. Instead, DeleteOldFolders removes only
    /// staging folders old enough that nothing can still be using them.
    /// </remarks>
    public static class BloomPubStaging
    {
        private const string kRootFolderName = "bps";

        // Created in each staging folder by the Bloom that claims it. Creating it is what makes the
        // claim exclusive, and DeleteOldFolders only deletes folders that have it, so it never
        // touches anything another program keeps under %TEMP%\bps.
        private const string kOwnerMarkerFileName = ".bloom-staging";

        // Long enough that no BloomPUB, even one for a large app build, can still be in progress.
        // A Publish-tab preview keeps its folder until the next preview, so a preview another Bloom
        // has left open for over a day can lose its files when this Bloom exits; reopening the
        // preview remakes them.
        private static readonly TimeSpan kMaxAge = TimeSpan.FromDays(1);

        /// <summary>
        /// The folder that holds every staging folder.
        /// </summary>
        public static string RootPath => Path.Combine(Path.GetTempPath(), kRootFolderName);

        /// <summary>
        /// Make a new staging folder that no other BloomPUB is using. It holds nothing but the
        /// marker file that claims it. Disposing the result deletes the folder.
        /// </summary>
        public static TemporaryFolder CreateFolder()
        {
            Directory.CreateDirectory(RootPath);
            while (true)
            {
                // GetRandomFileName gives random lowercase letters and digits (then a dot and an
                // extension); five of them allow over 33 million names.
                var path = Path.Combine(RootPath, Path.GetRandomFileName().Substring(0, 5));
                if (Directory.Exists(path))
                    continue;
                // CreateDirectory succeeds even if another process made the folder a moment ago,
                // so the folder is ours only if we are the one that creates the marker in it.
                Directory.CreateDirectory(path);
                try
                {
                    // robustfile-hook: allow FileStream
                    // FileMode.CreateNew is the exclusive claim; RobustFile has no equivalent.
                    new FileStream(
                        Path.Combine(path, kOwnerMarkerFileName),
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None
                    ).Dispose();
                }
                catch (IOException)
                {
                    continue; // another process claimed this name first
                }
                return TemporaryFolder.TrackExisting(path);
            }
        }

        /// <summary>
        /// True if the path (with either kind of slash) is inside a staging folder.
        /// </summary>
        public static bool IsInStagingFolder(string path)
        {
            return path.Replace('\\', '/')
                .StartsWith(
                    RootPath.Replace('\\', '/').TrimEnd('/') + "/",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        /// <summary>
        /// Delete the staging folders that were made more than a day ago. Each BloomPUB normally
        /// deletes its own folder when it is finished, so these are left over from a Bloom that
        /// crashed or was killed. Folders that cannot be deleted are left for next time.
        /// </summary>
        public static void DeleteOldFolders()
        {
            DeleteFoldersOlderThan(DateTime.UtcNow - kMaxAge);
        }

        /// <summary>
        /// Delete the staging folders created before the given (UTC) time. Folders without
        /// Bloom's marker file belong to something else and are left alone.
        /// </summary>
        internal static void DeleteFoldersOlderThan(DateTime cutoffUtc)
        {
            if (!Directory.Exists(RootPath))
                return;
            foreach (var folder in Directory.EnumerateDirectories(RootPath))
            {
                if (!RobustFile.Exists(Path.Combine(folder, kOwnerMarkerFileName)))
                    continue;
                if (Directory.GetCreationTimeUtc(folder) >= cutoffUtc)
                    continue;
                // If something still has a file open this returns false; we will try again the
                // next time Bloom exits.
                RobustIO.DeleteDirectoryAndContents(folder);
            }
        }
    }
}
