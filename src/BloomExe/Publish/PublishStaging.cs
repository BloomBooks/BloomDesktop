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
    /// The Bloom that claims a staging folder creates a marker file beside it,
    /// %TEMP%\bps\&lt;name&gt;.bloom-staging, and keeps that file open until it disposes the
    /// folder. Creating the marker is what makes the claim exclusive, and the open marker is what
    /// tells every other Bloom that the folder is still in use, however long that is: a preview
    /// left open for days keeps its files. A Bloom that crashes or is killed releases its markers
    /// with the process, so its folders can be reclaimed by the next Bloom that exits.
    ///
    /// The marker is beside the folder rather than in it so that disposing a folder that cannot be
    /// deleted completely (a file still in use) cannot delete the marker and leave the folder
    /// where no cleanup would ever find it.
    ///
    /// The parent folder deliberately does not start with "bloom": TempFile.CleanupTempFolder,
    /// which the Bloom holding the single-instance token runs on exit, deletes everything in
    /// %TEMP% that does, including folders another running Bloom (one started with Ctrl held
    /// down, or an automation instance) is still using.
    /// </remarks>
    public static class PublishStaging
    {
        private const string kRootFolderName = "bps";

        private const string kOwnerMarkerExtension = ".bloom-staging";

        /// <summary>
        /// The folder that holds every staging folder.
        /// </summary>
        public static string RootPath => Path.Combine(Path.GetTempPath(), kRootFolderName);

        /// <summary>
        /// Make a new, empty staging folder that no other publication is using. Disposing the
        /// result deletes the folder and releases this Bloom's claim on it.
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
                // The name is ours only if we are the one that creates its marker, and we keep the
                // marker open to show the folder is in use.
                FileStream marker;
                try
                {
                    // robustfile-hook: allow FileStream
                    // FileMode.CreateNew is the exclusive claim, and FileShare.None is what other
                    // Blooms see as "in use"; RobustFile has no equivalent.
                    marker = new FileStream(
                        markerPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None
                    );
                }
                catch (IOException) when (RobustFile.Exists(markerPath))
                {
                    // Another process claimed this name first. Any other failure (a full disk,
                    // say) is not a collision, and trying more names would never succeed.
                    continue;
                }
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception)
                {
                    // Give the name back rather than leave a marker held open that nobody owns.
                    marker.Dispose();
                    RobustFile.Delete(markerPath);
                    throw;
                }
                return TemporaryFolder.TrackExisting(
                    path,
                    () => ReleaseClaim(marker, markerPath, path)
                );
            }
        }

        /// <summary>
        /// Called when a staging folder is disposed: close its marker and, if the folder really is
        /// gone, delete the marker too. If something kept the folder from being deleted, the
        /// marker stays, unheld, so the next cleanup can finish the job.
        /// </summary>
        private static void ReleaseClaim(FileStream marker, string markerPath, string folder)
        {
            marker.Dispose();
            if (!Directory.Exists(folder))
                RobustFile.Delete(markerPath);
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
        /// Delete the staging folders that no running Bloom is using, and their markers. Each
        /// publication normally deletes its own folder when it is finished, so these are left over
        /// from a Bloom that crashed or was killed, or from a folder something kept from being
        /// deleted. Folders without Bloom's marker belong to something else and are left alone.
        /// </summary>
        /// <remarks>
        /// This runs while Bloom is shutting down, so a filesystem error here must not escape:
        /// it would skip the rest of shutdown, including saving the user's settings. Anything
        /// this cannot delete is left for the next time a Bloom exits.
        /// </remarks>
        public static void DeleteAbandonedFolders()
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
                    var folder = markerPath.Substring(
                        0,
                        markerPath.Length - kOwnerMarkerExtension.Length
                    );
                    FileStream probe;
                    try
                    {
                        // robustfile-hook: allow FileStream
                        // Opening the marker exclusively fails while its owner holds it open.
                        probe = new FileStream(
                            markerPath,
                            FileMode.Open,
                            FileAccess.ReadWrite,
                            FileShare.None
                        );
                    }
                    catch (IOException)
                    {
                        continue; // a running Bloom is using this folder (or the marker just went)
                    }
                    using (probe)
                    {
                        // Hold the marker while we delete, so no other Bloom's cleanup works on the
                        // same folder at once. If something still has a file open this returns
                        // false, and we keep the marker so a later cleanup tries again.
                        if (
                            Directory.Exists(folder) && !RobustIO.DeleteDirectoryAndContents(folder)
                        )
                            continue;
                    }
                    RobustFile.Delete(markerPath);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.WriteLine(
                    $"Could not finish deleting abandoned publish staging folders: {e}"
                );
            }
        }
    }
}
