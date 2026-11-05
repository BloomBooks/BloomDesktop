using System;
using System.Diagnostics;
using System.IO;
using BloomTemp;
using SIL.IO;

namespace Bloom.Publish
{
    /// <summary>
    /// Where Bloom stages the files of a BloomPUB or ePUB it is making, both for a preview in the
    /// Publish tab and for the real thing. Each one gets its own folder,
    /// %TEMP%\bps\&lt;five random characters&gt; ("bps" for Bloom publish staging), so that two
    /// publications being made at once, whether by one Bloom or by several, never share (and
    /// delete) each other's files.
    /// </summary>
    /// <remarks>
    /// The parent folder deliberately does not start with "bloom": TempFile.CleanupTempFolder,
    /// which the Bloom holding the single-instance token runs on exit, deletes everything in
    /// %TEMP% that does, including folders another running Bloom (one started with Ctrl held
    /// down, or an automation instance) is still using. Instead, DeleteOldFolders removes only
    /// staging folders old enough that nothing can still be using them.
    /// </remarks>
    public static class PublishStaging
    {
        private const string kRootFolderName = "bps";

        // The Bloom that claims a staging folder first creates an empty marker file beside it,
        // %TEMP%\bps\<name>.bloom-staging. Creating it is what makes the claim exclusive.
        // DeleteOldFolders only deletes folders that have one, so it never touches anything
        // another program keeps under %TEMP%\bps, and dates a folder by its marker. The marker is
        // beside the folder rather than in it so that disposing a folder that cannot be deleted
        // completely (a file still in use) cannot delete the marker and leave the folder where
        // no cleanup would ever find it. Markers are not deleted with their folders; the
        // cleanup removes them a day later.
        private const string kOwnerMarkerExtension = ".bloom-staging";

        // Long enough that no publication, even a large app build, can still be in progress.
        // A Publish-tab preview keeps its folder until the next preview, so a preview another Bloom
        // has left open for over a day can lose its files when this Bloom exits; reopening the
        // preview remakes them.
        private static readonly TimeSpan kMaxAge = TimeSpan.FromDays(1);

        /// <summary>
        /// The folder that holds every staging folder.
        /// </summary>
        public static string RootPath => Path.Combine(Path.GetTempPath(), kRootFolderName);

        /// <summary>
        /// Make a new, empty staging folder that no other publication is using. Disposing the
        /// result deletes the folder.
        /// </summary>
        public static TemporaryFolder CreateFolder()
        {
            Directory.CreateDirectory(RootPath);
            while (true)
            {
                // GetRandomFileName gives random lowercase letters and digits (then a dot and an
                // extension); five of them allow over 33 million names.
                var path = Path.Combine(RootPath, Path.GetRandomFileName().Substring(0, 5));
                var markerPath = path + kOwnerMarkerExtension;
                if (Directory.Exists(path) || RobustFile.Exists(markerPath))
                    continue;
                // The name is ours only if we are the one that creates its marker.
                try
                {
                    // robustfile-hook: allow FileStream
                    // FileMode.CreateNew is the exclusive claim; RobustFile has no equivalent.
                    new FileStream(
                        markerPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None
                    ).Dispose();
                }
                catch (IOException) when (RobustFile.Exists(markerPath))
                {
                    // Another process claimed this name first. Any other failure (a full disk,
                    // say) is not a collision, and trying more names would never succeed.
                    continue;
                }
                Directory.CreateDirectory(path);
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
        /// Delete the staging folders that were made more than a day ago. Each publication normally
        /// deletes its own folder when it is finished, so these are left over from a Bloom that
        /// crashed or was killed. Folders that cannot be deleted are left for next time.
        /// </summary>
        public static void DeleteOldFolders()
        {
            DeleteFoldersOlderThan(DateTime.UtcNow - kMaxAge);
        }

        /// <summary>
        /// Delete the staging folders claimed before the given (UTC) time, and their markers.
        /// Folders without Bloom's marker belong to something else and are left alone.
        /// </summary>
        /// <remarks>
        /// This runs while Bloom is shutting down, so a filesystem error here must not escape:
        /// it would skip the rest of shutdown, including saving the user's settings. Anything
        /// this cannot delete is left for the next time a Bloom exits.
        /// </remarks>
        internal static void DeleteFoldersOlderThan(DateTime cutoffUtc)
        {
            if (!Directory.Exists(RootPath))
                return;
            try
            {
                foreach (
                    var markerPath in Directory.EnumerateFiles(
                        RootPath,
                        "*" + kOwnerMarkerExtension
                    )
                )
                {
                    if (new FileInfo(markerPath).CreationTimeUtc >= cutoffUtc)
                        continue;
                    var folder = markerPath.Substring(
                        0,
                        markerPath.Length - kOwnerMarkerExtension.Length
                    );
                    // If something still has a file open this returns false, and we keep the
                    // marker so a later cleanup tries again.
                    if (Directory.Exists(folder) && !RobustIO.DeleteDirectoryAndContents(folder))
                        continue;
                    RobustFile.Delete(markerPath);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.WriteLine($"Could not finish deleting old publish staging folders: {e}");
            }
        }
    }
}
