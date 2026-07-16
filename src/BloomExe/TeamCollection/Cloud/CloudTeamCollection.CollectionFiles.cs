using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Amazon.S3.Model;
using Bloom.Collection;
using Bloom.MiscUI;
using Bloom.WebLibraryIntegration;
using Newtonsoft.Json.Linq;
using SIL.IO;

namespace Bloom.TeamCollection.Cloud
{
    public partial class CloudTeamCollection
    {
        // ------------------------------------------------------------------
        // Collection-level files (bloomCollection/customCollectionStyles.css/configuration.txt/
        // ReaderTools*.json via group "other"; Allowed Words/Sample Texts via their own groups)
        // ------------------------------------------------------------------

        public override void PutCollectionFiles(string[] names)
        {
            UploadCollectionFileGroup("other", names.ToList(), _localCollectionFolder);
        }

        protected override void CopyLocalFolderToRepo(string folderName)
        {
            var sourceDir = Path.Combine(_localCollectionFolder, folderName);
            if (!Directory.Exists(sourceDir))
                return;
            var groupKey = MapFolderNameToGroupKey(folderName);
            var relativeNames = Directory
                .EnumerateFiles(sourceDir)
                .Select(Path.GetFileName)
                .ToList();
            UploadCollectionFileGroup(groupKey, relativeNames, sourceDir);
        }

        private static string MapFolderNameToGroupKey(string folderName) =>
            folderName switch
            {
                "Allowed Words" => "allowed-words",
                "Sample Texts" => "sample-texts",
                _ => "other",
            };

        /// <summary>
        /// collection-files-start/finish, the two-phase protocol CONTRACTS.md describes as "like
        /// check-in" for one collection-file group. The exact response shape isn't spelled out in
        /// CONTRACTS.md beyond "finish bumps the group version atomically"; this assumes a
        /// checkin-start-like `{transactionId, s3}` / `{version}` shape -- flagged as a contract
        /// ambiguity in the task 05 final report.
        /// </summary>
        private void UploadCollectionFileGroup(
            string groupKey,
            List<string> relativeFileNames,
            string sourceFolder
        )
        {
            var files = new List<(string path, string sha256, long size)>();
            foreach (var name in relativeFileNames)
            {
                var fullPath = Path.IsPathRooted(name) ? name : Path.Combine(sourceFolder, name);
                if (!RobustFile.Exists(fullPath))
                    continue;
                var (sha256, size) = BookVersionManifest.ComputeFileHash(fullPath);
                files.Add((Path.GetFileName(fullPath), sha256, size));
            }
            if (files.Count == 0)
                return;

            var filesJson = new JArray(
                files.Select(f =>
                    (JToken)
                        new JObject
                        {
                            ["path"] = f.path,
                            ["sha256"] = f.sha256,
                            ["size"] = f.size,
                        }
                )
            );
            var expectedVersion = _cache.TryGetGroup(groupKey)?.Version ?? 0;
            var startResult = _client.CollectionFilesStart(
                _collectionId,
                groupKey,
                expectedVersion,
                filesJson
            );
            var transactionId = (string)startResult["transactionId"];
            var location = ParseS3Location(startResult);
            // Hand the hashes we just computed (for the start call's files json) to the transfer
            // so it doesn't hash every file a second time.
            var localGroupHashes = new Dictionary<string, BookVersionManifestEntry>();
            foreach (var f in files)
                localGroupHashes[BookVersionManifest.NormalizePath(f.path)] =
                    new BookVersionManifestEntry(f.sha256, f.size);
            _transfer.UploadChangedFiles(
                location,
                sourceFolder,
                files.Select(f => f.path),
                null,
                new BookVersionManifest(localGroupHashes),
                4,
                null,
                CancellationToken.None
            );
            var finishResult = _client.CollectionFilesFinish(transactionId);
            var newVersion = (long?)(finishResult?["version"]) ?? (expectedVersion + 1);
            _cache.RecordCollectionFilesFinish(groupKey, newVersion);
            _cache.Save();
        }

        protected override void CopyRepoCollectionFilesToLocalImpl(string destFolder)
        {
            _collectionLock.UnlockFor(() => DownloadCollectionFileGroup("other", destFolder));
            DownloadCollectionFileGroup("allowed-words", Path.Combine(destFolder, "Allowed Words"));
            DownloadCollectionFileGroup("sample-texts", Path.Combine(destFolder, "Sample Texts"));
        }

        /// <summary>
        /// Downloads every file currently in one collection-file group directly from S3 (listing the
        /// group's prefix, since CONTRACTS.md defines no manifest RPC for collection-file groups --
        /// only a version-bump counter via get_collection_state; see the task 05 final report's
        /// contract-gap note). Unlike book content, this reads "latest", not a pinned version -- an
        /// acknowledged deviation from the pinned-read invariant, forced by the missing manifest.
        /// </summary>
        private void DownloadCollectionFileGroup(string groupKey, string destFolder)
        {
            try
            {
                var location = GetCollectionDownloadLocation();
                var s3Client = CloudBookTransfer.BuildDefaultClient(location);
                var prefix = $"{location.Prefix}collectionFiles/{groupKey}/";

                // ListAllObjects handles pagination and AWSSDK v4's null-collection quirk (the
                // allowed-words/sample-texts groups most collections never populate list empty).
                var keys = s3Client
                    .ListAllObjects(
                        new ListObjectsV2Request { BucketName = location.Bucket, Prefix = prefix }
                    )
                    .Select(o => o.Key)
                    .ToList();

                if (keys.Count == 0)
                    return;

                Directory.CreateDirectory(destFolder);
                var keptFileNames = new HashSet<string>();
                foreach (var key in keys)
                {
                    var fileName = key.Substring(prefix.Length);
                    if (string.IsNullOrEmpty(fileName) || fileName.Contains("/"))
                        continue; // collection-file groups are flat per CONTRACTS.md's S3 layout.
                    keptFileNames.Add(fileName);
                    var destPath = Path.Combine(destFolder, fileName);
                    var tempPath = destPath + ".tmp";
                    using (
                        var response = s3Client
                            .GetObjectAsync(
                                new GetObjectRequest { BucketName = location.Bucket, Key = key }
                            )
                            .GetAwaiter()
                            .GetResult()
                    )
                    {
                        response
                            .WriteResponseStreamToFileAsync(tempPath, false, CancellationToken.None)
                            .GetAwaiter()
                            .GetResult();
                    }
                    if (RobustFile.Exists(destPath))
                        RobustFile.Delete(destPath);
                    RobustFile.Move(tempPath, destPath);
                }

                // Mirrors FolderTeamCollection's ExtractFolder "delete extras" behavior --
                // but see FilesEligibleForDeleteExtras: for the "other" group the destination
                // is the COLLECTION ROOT, which also holds files that are deliberately never
                // shared (TeamCollectionLink.txt itself, the repo cache, sync bookkeeping,
                // logs, PDFs...). A naive mirror-delete stripped TeamCollectionLink.txt on
                // every join/Receive, silently un-teaming the collection on next open (found
                // by the first two-instance smoke test, 7 Jul 2026).
                foreach (
                    var doomed in FilesEligibleForDeleteExtras(groupKey, destFolder, keptFileNames)
                )
                {
                    RobustFile.Delete(doomed);
                }
            }
            catch (Exception e)
            {
                NonFatalProblem.Report(
                    ModalIf.None,
                    PassiveIf.All,
                    $"Bloom could not download the '{groupKey}' collection files from the Team Collection.",
                    exception: e
                );
            }
        }

        /// <summary>
        /// Which local files the "delete extras" step of a collection-file-group download may
        /// remove: files present locally, absent from the server's group, AND belonging to the
        /// group's own domain. For allowed-words/sample-texts the destination folder belongs
        /// entirely to the group, so every file qualifies. For "other" the destination is the
        /// collection ROOT, so deletion is limited to the same shareable-file allowlist the
        /// upload side uses (<see cref="TeamCollection.RootLevelCollectionFilesIn"/>) — anything
        /// else there (TeamCollectionLink.txt, .bloom-cloud-repo-cache.json, log.txt,
        /// lastCollectionFileSyncData.txt, generated PDFs...) is local-only and untouchable.
        /// Internal static + pure so tests can pin the policy without an S3 server.
        /// </summary>
        internal static List<string> FilesEligibleForDeleteExtras(
            string groupKey,
            string destFolder,
            HashSet<string> keptFileNames
        )
        {
            var candidates = Directory
                .EnumerateFiles(destFolder)
                .Where(path => !keptFileNames.Contains(Path.GetFileName(path)));
            if (groupKey == "other")
            {
                var shareable = new HashSet<string>(RootLevelCollectionFilesIn(destFolder));
                candidates = candidates.Where(path => shareable.Contains(Path.GetFileName(path)));
            }
            return candidates.ToList();
        }

        protected override DateTime LastRepoCollectionFileModifyTime
        {
            get
            {
                EnsureCacheHydrated();
                var times = new[] { "other", "allowed-words", "sample-texts" }
                    .Select(g => _cache.TryGetGroup(g)?.UpdatedAt)
                    .Where(t => t.HasValue)
                    .Select(t => t.Value)
                    .ToList();
                return times.Count == 0 ? DateTime.MinValue : times.Max();
            }
        }

        /// <summary>
        /// Approximates the repo colorPalettes.json modify time with the "other" group's whole-group
        /// UpdatedAt, since CONTRACTS.md tracks collection-file freshness at group granularity, not
        /// per-file -- see <see cref="SyncColorPaletteFileWithRepo"/>'s note on the same limitation.
        /// </summary>
        protected override DateTime GetRepoColorPaletteTime()
        {
            EnsureCacheHydrated();
            return _cache.TryGetGroup("other")?.UpdatedAt ?? DateTime.MinValue;
        }

        /// <summary>
        /// Pushes local color palette additions up via add_palette_colors' union merge. CONTRACTS.md
        /// defines no RPC to read back a collection's full merged palette state (add_palette_colors
        /// is write-only), so this is push-only for now: local additions reach the repo, but a
        /// teammate's additions only reach us via the ordinary "other" group download (whole-file
        /// replace, not merge) in <see cref="CopyRepoCollectionFilesToLocalImpl"/> -- flagged as a
        /// contract gap (a `get_palette_colors` RPC is needed for a true two-way merge) in the task
        /// 05 final report.
        /// </summary>
        protected override void SyncColorPaletteFileWithRepo(string localFolder)
        {
            try
            {
                var colorPaletteFile = Path.Combine(localFolder, "colorPalettes.json");
                if (!RobustFile.Exists(colorPaletteFile))
                    return;
                var localPalettes = new Dictionary<string, string>();
                CollectionSettings.LoadColorPalettesFromJsonFile(localPalettes, colorPaletteFile);
                foreach (var kvp in localPalettes)
                {
                    var colors = kvp.Value?.Split(
                        new[] { ' ' },
                        StringSplitOptions.RemoveEmptyEntries
                    );
                    if (colors == null || colors.Length == 0)
                        continue;
                    _client.AddPaletteColors(_collectionId, kvp.Key, colors);
                }
            }
            catch (Exception e)
            {
                NonFatalProblem.ReportSentryOnly(e);
            }
        }
    }
}
