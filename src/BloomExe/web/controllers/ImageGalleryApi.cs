using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using Bloom.ImageProcessing;
using Bloom.MiscUI;
using Bloom.Utils;
using SIL.Core.ClearShare;
using SIL.IO;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageToolbox;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Api for the image gallery (image chooser dialog) — local collections, file picker,
    /// remote search results, and saving chosen images into the current book.
    /// </summary>
    public class ImageGalleryApi
    {
        public EditingView View { get; set; }

        // Shared HttpClient for downloading remote images; reused to avoid socket exhaustion.
        private static readonly HttpClient s_httpClient = new();

        /// <summary>
        /// The path most recently returned by HandlePickLocalImageFile. HandleLocalFilePreview
        /// only serves this exact file, preventing arbitrary local-file access via the endpoint.
        /// </summary>
        private string _lastPickedLocalImagePath;

        /// <summary>
        /// The root folder where SIL image collections (including Art of Reading) are installed.
        /// </summary>
        private static string LocalCollectionsBaseFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SIL",
                "ImageCollections"
            );

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterAsyncEndpointHandler(
                "imageGallery/imageGalleryResult",
                HandleImageGalleryResult,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "imageGallery/pickLocalImageFile",
                HandlePickLocalImageFile,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "imageGallery/localFilePreview",
                HandleLocalFilePreview,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "imageGallery/local-collections/collections",
                HandleLocalCollections,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "imageGallery/local-collections/search",
                HandleLocalCollectionsSearch,
                false
            );
            apiHandler.RegisterEndpointHandler(
                "imageGallery/local-collections/collection-image",
                HandleLocalCollectionImage,
                false
            );
        }

        /// <summary>
        /// Saves an image chosen in the image gallery to the book folder and returns
        /// the resulting src and metadata as JSON for the JS caller to apply.
        /// Accepts either a local file path (localPath) or a remote URL (imageUrl).
        /// Gallery-provided license/credits/creator override the source EXIF, except for
        /// images from official collections (e.g. Art of Reading) whose EXIF is authoritative.
        /// </summary>
        private async Task HandleImageGalleryResult(ApiRequest request)
        {
            var data = (DynamicJson)DynamicJson.Parse(request.RequiredPostJson());
            data.TryGetValue("localPath", out string localPath);
            data.TryGetValue("imageUrl", out string imageUrl);
            data.TryGetValue("credits", out string credits);
            data.TryGetValue("license", out string license);
            data.TryGetValue("licenseUrl", out string licenseUrl);
            data.TryGetValue("creator", out string galleryCreator);
            string sourceFilePath;
            bool isTempFile = false;

            if (!string.IsNullOrEmpty(localPath))
            {
                sourceFilePath = localPath;
            }
            else if (!string.IsNullOrEmpty(imageUrl))
            {
                string extension;
                try
                {
                    extension = Path.GetExtension(new Uri(imageUrl).LocalPath);
                }
                catch
                {
                    extension = ".jpg";
                }
                if (string.IsNullOrEmpty(extension))
                    extension = ".jpg";

                sourceFilePath = Path.Combine(
                    Path.GetTempPath(),
                    Guid.NewGuid().ToString() + extension
                );
                using (var response = await s_httpClient.GetAsync(imageUrl))
                {
                    response.EnsureSuccessStatusCode();
                    using var fileStream = RobustFile.Create(sourceFilePath);
                    await response.Content.CopyToAsync(fileStream);
                }
                isTempFile = true;
            }
            else
            {
                request.Failed(
                    HttpStatusCode.BadRequest,
                    "imageGalleryResult requires localPath or imageUrl"
                );
                return;
            }

            try
            {
                // GIF files must be copied byte-for-byte to preserve animation.
                // PalasoImage / ProcessAndSaveImageIntoFolder will strip the animation frames.
                if (
                    Path.GetExtension(sourceFilePath)
                        .Equals(".gif", StringComparison.OrdinalIgnoreCase)
                )
                {
                    var baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
                    var destName = ImageUtils.GetUnusedFilename(
                        View.Model.CurrentBook.FolderPath,
                        baseName,
                        ".gif",
                        "gif"
                    );
                    RobustFile.Copy(
                        sourceFilePath,
                        Path.Combine(View.Model.CurrentBook.FolderPath, destName)
                    );
                    request.ReplyWithJson(
                        new
                        {
                            src = UrlPathString.CreateFromUnencodedString(destName).UrlEncoded,
                            copyright = "",
                            license = "",
                            creator = "",
                        }
                    );
                    return;
                }

                using (var palasoImage = PalasoImage.FromFileRobustly(sourceFilePath))
                {
                    var info = PageEditingModel.ChangePicture(
                        View.Model.CurrentBook.FolderPath,
                        "",
                        UrlPathString.CreateFromUnencodedString(""),
                        palasoImage
                    );

                    // Metadata.Write (used inside ChangePicture) writes from the
                    // source-file-locked TagLib object, so existing EXIF tags like
                    // "Picassa" Artist can survive. Use SaveImageMetadataIfNeeded on a
                    // fresh load of the destination file so the replacement is complete.
                    var licenseInfo = BuildLicenseInfoFromGallery(license, licenseUrl);

                    // Priority rules:
                    // Copyright: EXIF is more specific (often includes year); use it when present,
                    //   fall back to gallery-provided collection-level credits.
                    // Creator: per-image artist from the collection index trumps EXIF, which in
                    //   turn trumps the absence of any data.
                    var effectiveCopyright = !string.IsNullOrEmpty(info.copyright)
                        ? info.copyright
                        : credits ?? "";
                    var effectiveCreator = !string.IsNullOrEmpty(galleryCreator)
                        ? galleryCreator
                        : info.creator ?? "";

                    bool hasGalleryMeta =
                        !string.IsNullOrEmpty(effectiveCreator)
                        || !string.IsNullOrEmpty(effectiveCopyright)
                        || licenseInfo != null;

                    if (hasGalleryMeta)
                    {
                        var galleryMetadata = new Metadata();
                        if (!string.IsNullOrEmpty(effectiveCreator))
                            galleryMetadata.Creator = effectiveCreator;
                        if (!string.IsNullOrEmpty(effectiveCopyright))
                            galleryMetadata.CopyrightNotice = effectiveCopyright;
                        if (licenseInfo != null)
                            galleryMetadata.License = licenseInfo;

                        var destFileName = Uri.UnescapeDataString(info.src);
                        ImageUtils.SaveImageMetadataIfNeeded(
                            galleryMetadata,
                            View.Model.CurrentBook.FolderPath,
                            destFileName
                        );
                    }

                    request.ReplyWithJson(
                        new
                        {
                            src = info.src,
                            copyright = effectiveCopyright,
                            creator = effectiveCreator,
                            license = licenseInfo != null ? licenseInfo.ToString() : info.license,
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                request.Failed(HttpStatusCode.InternalServerError, ex.Message);
            }
            finally
            {
                if (isTempFile && RobustFile.Exists(sourceFilePath))
                    RobustFile.Delete(sourceFilePath);
            }
        }

        /// <summary>
        /// Builds a libpalaso ILicenseInfo from the gallery-provided license string and/or URL.
        /// CC license URLs (creativecommons.org) are parsed into a proper CreativeCommonsLicense;
        /// well-known CC license strings (e.g. "CC-BY-SA") are similarly mapped even without a URL;
        /// everything else becomes a CustomLicense so the text is preserved.
        /// Returns null when no info is given.
        /// </summary>
        private static LicenseInfo BuildLicenseInfoFromGallery(string license, string licenseUrl)
        {
            // Only invoke the CC parser for actual creativecommons.org URLs.
            // FromLicenseUrl misparses unrelated URLs (e.g. pixabay.com/service/license/)
            // instead of throwing, so we guard before calling it.
            if (!string.IsNullOrEmpty(licenseUrl) && licenseUrl.Contains("creativecommons.org"))
            {
                try
                {
                    return CreativeCommonsLicense.FromLicenseUrl(licenseUrl);
                }
                catch
                {
                    // Malformed CC URL — fall through to the named string.
                }
            }
            if (!string.IsNullOrEmpty(license))
            {
                // Try to interpret the string as a standard CC license code so we get a
                // proper CreativeCommonsLicense (with type/version) rather than CustomLicense.
                var ccUrl = TryGetCcUrlFromString(license);
                if (ccUrl != null)
                {
                    try
                    {
                        return CreativeCommonsLicense.FromLicenseUrl(ccUrl);
                    }
                    catch
                    { /* fall through to CustomLicense */
                    }
                }
                return new CustomLicense { RightsStatement = license };
            }
            return null;
        }

        /// <summary>
        /// Maps a CC license string such as "CC-BY-SA" or "CC-BY-SA 3.0" to a canonical
        /// creativecommons.org URL.  Returns null if the string is not a recognised CC code.
        /// Always uses the latest 4.0 URL so Bloom's license picker shows the right type;
        /// the exact version is not critical here — it can be corrected by the author.
        /// </summary>
        private static string TryGetCcUrlFromString(string license)
        {
            // Normalise: upper-case, collapse spaces/underscores to hyphens, strip trailing version
            var key = Regex.Replace(
                license.Trim().ToUpperInvariant().Replace(' ', '-').Replace('_', '-'),
                @"-\d+\.\d+$",
                ""
            );
            switch (key)
            {
                case "CC-BY":
                    return "https://creativecommons.org/licenses/by/4.0/";
                case "CC-BY-SA":
                    return "https://creativecommons.org/licenses/by-sa/4.0/";
                case "CC-BY-ND":
                    return "https://creativecommons.org/licenses/by-nd/4.0/";
                case "CC-BY-NC":
                    return "https://creativecommons.org/licenses/by-nc/4.0/";
                case "CC-BY-NC-SA":
                    return "https://creativecommons.org/licenses/by-nc-sa/4.0/";
                case "CC-BY-NC-ND":
                    return "https://creativecommons.org/licenses/by-nc-nd/4.0/";
                case "CC0":
                    return "https://creativecommons.org/publicdomain/zero/1.0/";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Opens a native file-picker dialog and returns the selected path as JSON.
        /// Pass gifOnly:true to restrict the filter to GIF files.
        /// </summary>
        private void HandlePickLocalImageFile(ApiRequest request)
        {
            dynamic data = DynamicJson.Parse(request.RequiredPostJson());
            ((DynamicJson)data).TryGetValue("gifOnly", out bool gifOnly);

            var filter = gifOnly
                ? "GIF images|*.gif"
                : "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.svg";

            string selectedPath = "";
            View.Invoke(
                (Action)(
                    () =>
                    {
                        using (
                            var dlg = new BloomOpenFileDialog
                            {
                                InitialDirectory = Environment.GetFolderPath(
                                    Environment.SpecialFolder.MyPictures
                                ),
                                Filter = filter,
                            }
                        )
                        {
                            View.SetModalState(true);
                            try
                            {
                                using (LegacyDpiDialogLauncher.EnterLegacyDpiScope())
                                {
                                    if (dlg.ShowDialog() == DialogResult.OK)
                                        selectedPath = dlg.FileName;
                                }
                            }
                            finally
                            {
                                View.SetModalState(false);
                            }
                        }
                    }
                )
            );

            _lastPickedLocalImagePath = selectedPath;
            var previewUrl = string.IsNullOrEmpty(selectedPath)
                ? ""
                : "/bloom/api/imageGallery/localFilePreview?path="
                    + Uri.EscapeDataString(selectedPath);
            request.ReplyWithJson(new { filePath = selectedPath, previewUrl });
        }

        /// <summary>
        /// Serves a single local image file for preview purposes.
        /// The "path" query parameter is the absolute OS path to the file.
        /// Only the path most recently returned by HandlePickLocalImageFile is allowed.
        /// </summary>
        private void HandleLocalFilePreview(ApiRequest request)
        {
            var path = request.RequiredParam("path");
            var fullPath = Path.GetFullPath(path);

            if (fullPath != _lastPickedLocalImagePath)
            {
                request.Failed(HttpStatusCode.Forbidden, "File not authorized for preview");
                return;
            }

            if (!RobustFile.Exists(fullPath))
            {
                request.Failed(HttpStatusCode.NotFound, "File not found");
                return;
            }

            request.ReplyWithImage(fullPath);
        }

        /// <summary>
        /// Returns the list of Art of Reading image collections installed on this machine,
        /// together with the keyword-search languages they support.
        /// </summary>
        private void HandleLocalCollections(ApiRequest request)
        {
            var baseFolder = LocalCollectionsBaseFolder;
            if (!Directory.Exists(baseFolder))
            {
                request.ReplyWithJson(
                    new { collections = Array.Empty<object>(), languages = new[] { "en" } }
                );
                return;
            }

            var collectionNames = Directory
                .GetDirectories(baseFolder)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToArray();

            var languages = GetLocalCollectionsLanguages(baseFolder, collectionNames);
            var collections = collectionNames
                .Select(name =>
                {
                    var (licenseUrl, credits) = GetCollectionMetadata(name, baseFolder);
                    return (object)
                        new
                        {
                            name,
                            licenseUrl,
                            credits,
                        };
                })
                .ToArray();
            request.ReplyWithJson(new { collections, languages });
        }

        /// <summary>
        /// Returns the license URL and credit text for a named local image collection.
        /// Priority: metadata.json (if present) → InstallerLicense.rtf (if present) → empty strings.
        /// </summary>
        private static (string licenseUrl, string credits) GetCollectionMetadata(
            string name,
            string baseFolder
        )
        {
            var collectionFolder = Path.Combine(baseFolder, name);

            var metaPath = Path.Combine(collectionFolder, "metadata.json");
            if (RobustFile.Exists(metaPath))
            {
                try
                {
                    var meta = (DynamicJson)DynamicJson.Parse(RobustFile.ReadAllText(metaPath));
                    meta.TryGetValue("licenseUrl", out string fileLicenseUrl);
                    meta.TryGetValue("credits", out string fileCredits);
                    if (!string.IsNullOrEmpty(fileLicenseUrl) || !string.IsNullOrEmpty(fileCredits))
                        return (fileLicenseUrl ?? "", fileCredits ?? "");
                }
                catch
                {
                    // Malformed metadata.json — fall through to RTF.
                }
            }

            return GetInstallerLicenseMetadata(collectionFolder);
        }

        /// <summary>
        /// Parses InstallerLicense.rtf (a standard SIL image-collection license file) to extract
        /// the CC license URL and the copyright-holder/grantor name.
        /// Returns empty strings if the file is absent or cannot be parsed.
        /// </summary>
        internal static (string licenseUrl, string credits) GetInstallerLicenseMetadata(
            string collectionFolder
        )
        {
            var rtfPath = Path.Combine(collectionFolder, "InstallerLicense.rtf");
            if (!RobustFile.Exists(rtfPath))
                return ("", "");

            string rtf;
            try
            {
                rtf = RobustFile.ReadAllText(rtfPath);
            }
            catch
            {
                return ("", "");
            }

            // Extract the CC license URL from the HYPERLINK directive.
            // The RTF contains e.g. "HYPERLINK https://creativecommons.org/licenses/by-sa/4.0/legalcode"
            string licenseUrl = "";
            var hyperlinkMatch = Regex.Match(
                rtf,
                @"HYPERLINK\s+(https://creativecommons\.org/licenses/[^\s]+)"
            );
            if (hyperlinkMatch.Success)
            {
                licenseUrl = hyperlinkMatch.Groups[1].Value;
                // Strip the /legalcode suffix to get the canonical license URL.
                licenseUrl = Regex.Replace(licenseUrl, @"/legalcode/?$", "/");
                if (!licenseUrl.EndsWith('/'))
                    licenseUrl += "/";
            }

            // Strip RTF control words and braces to get readable plain text, then find
            // the grantor — the name that appears before "grants you use of these images".
            string credits = "";
            var plain = Regex.Replace(rtf, @"\\[a-zA-Z]+\d*\s?|\\'[0-9a-fA-F]{2}|\\\*|[{}]", " ");
            plain = Regex.Replace(plain, @"\s+", " ");
            var grantorMatch = Regex.Match(plain, @"((?:[A-Z][^\s]*\s+){1,6})grants you use");
            if (grantorMatch.Success)
                credits = grantorMatch.Groups[1].Value.Trim();

            return (licenseUrl, credits);
        }

        /// <summary>
        /// Reads the first available index.txt header to discover which keyword-language
        /// columns the collection provides (e.g. "en", "es").
        /// </summary>
        private static string[] GetLocalCollectionsLanguages(
            string baseFolder,
            string[] collections
        )
        {
            foreach (var collection in collections)
            {
                var indexPath = Path.Combine(baseFolder, collection, "index.txt");
                if (!RobustFile.Exists(indexPath))
                    continue;
                var firstLine = RobustFile.ReadAllLines(indexPath).FirstOrDefault();
                if (firstLine == null)
                    continue;
                // Language-code columns are exactly 2 characters; skip "filename", "subfolder", "country"
                var langCodes = firstLine.Split('\t').Where(col => col.Length == 2).ToArray();
                if (langCodes.Length > 0)
                    return langCodes;
            }
            return new[] { "en" };
        }

        /// <summary>
        /// Searches an Art of Reading collection's index.txt for images whose keyword list
        /// (in the requested language) contains the search term.
        /// Returns an array of {url, localPath} objects — url is a root-relative Bloom API URL
        /// for thumbnail display; localPath is the absolute OS path so the caller can copy the
        /// file directly without an extra HTTP round-trip.
        /// </summary>
        private void HandleLocalCollectionsSearch(ApiRequest request)
        {
            var collection = request.RequiredParam("collection");
            var lang = request.RequiredParam("lang");
            var term = request.RequiredParam("term").Trim().ToLowerInvariant();

            var safeBase = Path.GetFullPath(LocalCollectionsBaseFolder);
            var indexPath = Path.GetFullPath(
                Path.Combine(LocalCollectionsBaseFolder, collection, "index.txt")
            );
            var imagesBaseForGuard = Path.GetFullPath(
                Path.Combine(LocalCollectionsBaseFolder, collection, "images")
            );
            if (
                !indexPath.StartsWith(safeBase, StringComparison.OrdinalIgnoreCase)
                || !imagesBaseForGuard.StartsWith(safeBase, StringComparison.OrdinalIgnoreCase)
            )
            {
                request.Failed(HttpStatusCode.Forbidden, "Invalid collection path");
                return;
            }

            if (!RobustFile.Exists(indexPath))
            {
                request.ReplyWithJson(Array.Empty<object>());
                return;
            }

            var lines = RobustFile.ReadAllLines(indexPath);
            if (lines.Length == 0)
            {
                request.ReplyWithJson(Array.Empty<object>());
                return;
            }

            var headers = lines[0].Split('\t');
            var filenameIdx = Array.IndexOf(headers, "filename");
            var subfolderIdx = Array.IndexOf(headers, "subfolder");
            if (subfolderIdx < 0)
                subfolderIdx = Array.IndexOf(headers, "country");
            var langIdx = Array.IndexOf(headers, lang);
            var artistIdx = Array.IndexOf(headers, "artist");

            if (filenameIdx < 0 || langIdx < 0)
            {
                request.ReplyWithJson(Array.Empty<object>());
                return;
            }

            const string imageEndpoint =
                "/bloom/api/imageGallery/local-collections/collection-image";
            var imagesBase = imagesBaseForGuard;
            var results = new List<object>();

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var cols = line.Split('\t');
                if (cols.Length <= langIdx)
                    continue;

                var tags = cols[langIdx].Split(',').Select(t => t.Trim().ToLowerInvariant());
                if (!tags.Contains(term))
                    continue;

                var filename = filenameIdx < cols.Length ? cols[filenameIdx].Trim() : "";
                var subfolder =
                    subfolderIdx >= 0 && subfolderIdx < cols.Length
                        ? cols[subfolderIdx].Trim()
                        : "";
                var artist =
                    artistIdx >= 0 && artistIdx < cols.Length ? cols[artistIdx].Trim() : "";

                // Resolve the actual file path, handling AOR's optional one-level
                // subsubfolder nesting (index subfolder may not be the direct parent).
                var imagePath = FindAorImagePath(imagesBase, subfolder, filename);
                if (imagePath == null)
                    continue;

                // Relative path from images/ to the file, forward-slash separated, for the URL
                var relPath = imagePath[imagesBase.Length..]
                    .TrimStart(Path.DirectorySeparatorChar)
                    .Replace(Path.DirectorySeparatorChar, '/');

                // Read per-image EXIF so the image chooser can show accurate copyright
                // before the user confirms. MetadataFromFile reads only metadata chunks
                // (not pixel data), so it is fast enough to call per search result.
                string exifCopyright = "";
                string exifCreator = "";
                try
                {
                    var meta = RobustFileIO.MetadataFromFile(imagePath);
                    if (meta?.ExceptionCaughtWhileLoading == null)
                    {
                        exifCopyright = meta?.CopyrightNotice?.Trim() ?? "";
                        exifCreator = meta?.Creator?.Trim() ?? "";
                    }
                }
                catch
                {
                    // Ignore metadata read failures; the gallery falls back gracefully.
                }

                // For creator: index artist column is authoritative; fall back to EXIF.
                var creator = !string.IsNullOrEmpty(artist) ? artist : exifCreator;

                results.Add(
                    new
                    {
                        url = $"{imageEndpoint}?collection={Uri.EscapeDataString(collection)}&file={Uri.EscapeDataString(relPath)}",
                        localPath = imagePath,
                        creator,
                        copyright = exifCopyright,
                    }
                );
            }

            request.ReplyWithJson(results.ToArray());
        }

        /// <summary>
        /// Resolves the actual path of an AOR image on disk.
        /// The index "subfolder" column may omit a further nesting level, so if the direct
        /// path does not exist we search one level of subdirectories (mirroring the Node.js
        /// storeInMapsIfFileExists logic).
        /// Returns null if the file cannot be found.
        /// </summary>
        private static string FindAorImagePath(string imagesBase, string subfolder, string filename)
        {
            // Try the direct path first
            var directPath = string.IsNullOrEmpty(subfolder)
                ? Path.Combine(imagesBase, filename)
                : Path.Combine(imagesBase, subfolder, filename);

            if (RobustFile.Exists(directPath))
                return directPath;

            if (string.IsNullOrEmpty(subfolder))
                return null;

            // Direct path failed; search subdirectories of the subfolder one level deep
            var subfolderDir = Path.Combine(imagesBase, subfolder);
            if (!Directory.Exists(subfolderDir))
                return null;

            foreach (var subdir in Directory.GetDirectories(subfolderDir))
            {
                var candidate = Path.Combine(subdir, filename);
                if (RobustFile.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// Serves a single Art of Reading image from the local image collections folder.
        /// The "file" query parameter is a subfolder-relative path such as "Animals/dog.png".
        /// </summary>
        private void HandleLocalCollectionImage(ApiRequest request)
        {
            var collection = request.RequiredParam("collection");
            var file = request.RequiredParam("file");

            // Normalise separators and guard against directory traversal
            var normalizedFile = file.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var imagePath = Path.GetFullPath(
                Path.Combine(LocalCollectionsBaseFolder, collection, "images", normalizedFile)
            );
            var safeBase = Path.GetFullPath(LocalCollectionsBaseFolder);

            if (!imagePath.StartsWith(safeBase, StringComparison.OrdinalIgnoreCase))
            {
                request.Failed(HttpStatusCode.Forbidden, "Invalid image path");
                return;
            }

            if (!RobustFile.Exists(imagePath))
            {
                request.Failed(HttpStatusCode.NotFound, "Image not found");
                return;
            }

            request.ReplyWithImage(imagePath);
        }
    }
}
