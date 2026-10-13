using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using Bloom.ImageProcessing;
using Bloom.MiscUI;
using Bloom.Utils;
using SIL.Core.ClearShare;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageToolbox;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Api for the image gallery (image chooser dialog) — local collections, file picker,
    /// remote search results, and saving chosen images into the current book.
    /// </summary>
    public class ImageGalleryApi : IDisposable
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
        /// A temporary JPEG standing in for _lastPickedLocalImagePath when the browser could
        /// not display the original itself (see MakeBrowserSafePreview).
        /// Null when the original is served as-is. Deleted when another file is picked.
        /// </summary>
        private string _lastPickedLocalImagePreviewPath;

        /// <summary>
        /// The file _lastPickedLocalImagePreviewPath was made from, or null if we have not yet
        /// looked at any file. This is what the cache is keyed on, so that a stand-in can only
        /// ever be served for the file it was made from — rather than that being an invariant
        /// maintained by the order in which HandlePickLocalImageFile does things.
        /// It is also how we remember that a file needs no stand-in, which is why it is set
        /// even in that case: without it we would re-examine such a file on every request.
        /// </summary>
        private string _lastPickedLocalImagePreviewSourcePath;

        /// <summary>
        /// Guards the two fields above. The gallery uses the preview URL as both the
        /// thumbnail and the large preview, so two requests for it typically arrive at once;
        /// without this they would each spend seconds building a preview and one would leak.
        /// </summary>
        private readonly object _previewLock = new object();

        /// <summary>
        /// Chromium — and therefore WebView2 — flatly refuses to decode an image whose pixel
        /// count times 4 bytes/pixel overflows a signed 32-bit int, i.e. anything larger than
        /// about 536.9 megapixels. The &lt;img&gt; fires "error" a fraction of a second after
        /// the bytes arrive and nothing paints, which is why the image chooser showed an empty
        /// preview for a 30000x23756 scan (BL-16597). Decoding at a reduced size does not help:
        /// createImageBitmap() with resizeWidth and the WebCodecs ImageDecoder with desiredWidth
        /// both fail identically, because the limit is tested against the image's natural size
        /// before any scaling is applied.
        ///
        /// Well below that hard ceiling, handing the renderer a hundred-megapixel image still
        /// costs it seconds of decode time and gigabytes of RAM, all to fill a preview pane a
        /// few hundred pixels tall. So past this threshold we substitute a downscaled JPEG.
        /// (Size is only one of the two reasons for substituting one — see NeedsStandIn.)
        /// </summary>
        internal const long kMaxPreviewPixels = 40L * 1000 * 1000;

        /// <summary>
        /// Longest side, in pixels, of a generated preview. The gallery's preview pane caps the
        /// image at 420px tall, so this leaves plenty of room for high-DPI screens.
        /// </summary>
        internal const int kPreviewMaxDimension = 1600;

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
            // requiresSync: false deliberately. Building a stand-in for a very large image
            // decodes and re-encodes it, which takes on the order of seconds; if this ran under
            // the api handler's global lock it would stall every other synchronized request for
            // that whole time. Nothing here needs that lock: the only state shared between
            // requests is the preview cache, and _previewLock already guards it (and still
            // coalesces the gallery's near-simultaneous thumbnail and large-preview requests
            // into a single build).
            apiHandler.RegisterEndpointHandler(
                "imageGallery/localFilePreview",
                HandleLocalFilePreview,
                false,
                requiresSync: false
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
            // The image-gallery provider the picture came from ("pixabay", "openverse", a local
            // collection slug, or "local-disk"). Analytics only.
            data.TryGetValue("provider", out string provider);
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
                    TrackPictureChosenFromGallery(provider);
                    request.ReplyWithJson(
                        new
                        {
                            // destName is the name of the file we just copied into the book
                            // folder, so it is certainly not URL-encoded; see the same call in
                            // PageEditingModel.ChangePicture.
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

                    TrackPictureChosenFromGallery(provider);
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
        /// Report a picture the user chose in the image chooser. The provider id IS the source:
        /// opening a file from disk arrives here as "local-disk" and counts as its own route
        /// rather than as a search result, because the user bypassed every collection we offer,
        /// which is exactly the thing we want to be able to see separately.
        /// </summary>
        private void TrackPictureChosenFromGallery(string provider)
        {
            AnalyticsApi.TrackChangePicture(provider, View?.Model?.CurrentBook?.ID);
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

            // The previous pick's downscaled stand-in (if any) is now unreachable. Serving the
            // wrong image is prevented by the cache's key, not by this; the point here is just
            // to not leave the temp file lying around until the next pick.
            DeleteLastPickedImagePreview();
            _lastPickedLocalImagePath = selectedPath;

            var previewUrl = string.IsNullOrEmpty(selectedPath)
                ? ""
                : "/bloom/api/imageGallery/localFilePreview?path="
                    + Uri.EscapeDataString(selectedPath);

            // Report the *original* file's dimensions and byte count. The gallery displays
            // these, and without them it would fall back to measuring whatever the preview
            // turns out to be — which for a huge image is the downscaled stand-in, not the
            // file the user actually chose.
            var (width, height) = GetImageDimensions(selectedPath);
            long size = 0;
            if (!string.IsNullOrEmpty(selectedPath) && RobustFile.Exists(selectedPath))
            {
                try
                {
                    size = new FileInfo(selectedPath).Length;
                }
                catch (Exception e)
                {
                    Logger.WriteMinorEvent(
                        $"ImageGalleryApi could not read the size of {selectedPath}: {e.Message}"
                    );
                }
            }

            request.ReplyWithJson(
                new
                {
                    filePath = selectedPath,
                    previewUrl,
                    width,
                    height,
                    size,
                }
            );
        }

        /// <summary>
        /// Reads an image's pixel dimensions without decoding its pixels. Returns (0,0) if the
        /// dimensions can't be determined (e.g. an SVG, or a format WIC doesn't know).
        /// </summary>
        internal static (int width, int height) GetImageDimensions(string path)
        {
            if (string.IsNullOrEmpty(path) || !RobustFile.Exists(path))
                return (0, 0);
            try
            {
                var header = ReadImageHeader(path);
                // Report the dimensions as they will actually be seen, i.e. after the viewer
                // applies any EXIF rotation.
                return IsQuarterTurned(header.exifOrientation)
                    ? (header.height, header.width)
                    : (header.width, header.height);
            }
            catch (Exception e)
            {
                Logger.WriteMinorEvent(
                    $"ImageGalleryApi could not read the dimensions of {path}: {e.Message}"
                );
                return (0, 0);
            }
        }

        /// <summary>
        /// Reads an image's dimensions and EXIF orientation without decoding its pixels. WIC
        /// parses the header up front but reads everything else lazily, so all the values we
        /// want must be pulled out while the stream is still open — hence one method rather
        /// than handing a BitmapFrame back to the caller.
        /// </summary>
        private static (int width, int height, int exifOrientation) ReadImageHeader(string path)
        {
            using var stream = RobustFile.OpenRead(path);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None
            );
            var frame = decoder.Frames[0];
            return (
                frame.PixelWidth,
                frame.PixelHeight,
                ReadExifOrientation(frame.Metadata as BitmapMetadata)
            );
        }

        /// <summary>
        /// The EXIF orientation recorded in the given metadata, or 1 ("upright") if there is
        /// none. The chooser accepts TIFFs as well as JPEGs, and the tag lives at a different
        /// query path in each, so both are tried.
        /// </summary>
        private static int ReadExifOrientation(BitmapMetadata metadata)
        {
            if (metadata == null)
                return 1;
            // 274 (0x112) is the EXIF "Orientation" tag. In a JPEG it hangs off the APP1
            // marker segment; in a TIFF it is in the top-level IFD.
            foreach (var query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
            {
                try
                {
                    if (
                        metadata.GetQuery(query) is ushort orientation
                        && orientation >= 1
                        && orientation <= 8
                    )
                        return orientation;
                }
                catch (Exception)
                {
                    // Plenty of images carry no EXIF block at all, and asking a decoder for a
                    // query path its format doesn't have is how we find that out.
                }
            }
            return 1;
        }

        /// <summary>Whether the given EXIF orientation swaps width and height.</summary>
        private static bool IsQuarterTurned(int exifOrientation) => exifOrientation >= 5;

        /// <summary>
        /// The rotation/flip an EXIF orientation calls for, or null when none is needed.
        /// </summary>
        private static System.Windows.Media.Transform GetOrientationTransform(int exifOrientation)
        {
            switch (exifOrientation)
            {
                case 2:
                    return new System.Windows.Media.ScaleTransform(-1, 1);
                case 3:
                    return new System.Windows.Media.RotateTransform(180);
                case 4:
                    return new System.Windows.Media.ScaleTransform(1, -1);
                case 5:
                    var transpose = new System.Windows.Media.TransformGroup();
                    transpose.Children.Add(new System.Windows.Media.RotateTransform(90));
                    transpose.Children.Add(new System.Windows.Media.ScaleTransform(-1, 1));
                    return transpose;
                case 6:
                    return new System.Windows.Media.RotateTransform(90);
                case 7:
                    var transverse = new System.Windows.Media.TransformGroup();
                    transverse.Children.Add(new System.Windows.Media.RotateTransform(270));
                    transverse.Children.Add(new System.Windows.Media.ScaleTransform(-1, 1));
                    return transverse;
                case 8:
                    return new System.Windows.Media.RotateTransform(270);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Lays the image over a white background, so that anything see-through in it becomes
        /// white rather than whatever happens to be in the RGB channels underneath.
        ///
        /// A stand-in is encoded as JPEG, which has no alpha channel: without this, a large
        /// image with transparent areas — a line-art scan on transparency, say — previews with
        /// those areas solid black, because that is what is stored under a fully transparent
        /// pixel. White is what the chooser and the page behind it both use.
        ///
        /// Done unconditionally rather than only for images that carry transparency, because
        /// deciding that reliably means enumerating the pixel formats that can hold an alpha
        /// channel *and* checking indexed palettes for a transparent entry, whereas this is one
        /// pass over an image already capped at kPreviewMaxDimension — milliseconds against the
        /// seconds the decode itself takes. Pure pixel arithmetic, so unlike the WPF rendering
        /// stack it is safe on whatever thread the API server hands us.
        /// </summary>
        private static BitmapSource FlattenOntoWhite(BitmapSource source)
        {
            // Bgra32 is straight (non-premultiplied) alpha, which is what the blend below
            // assumes; Pbgra32 would already have the colour scaled by the alpha.
            var bgra = new FormatConvertedBitmap(
                source,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                0
            );
            int width = bgra.PixelWidth,
                height = bgra.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[(long)height * stride];
            bgra.CopyPixels(pixels, stride, 0);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte alpha = pixels[i + 3];
                if (alpha == 255)
                    continue;
                // result = colour*alpha + white*(1-alpha), in 0..255 integer arithmetic.
                int inverse = 255 - alpha;
                pixels[i] = (byte)((pixels[i] * alpha + 255 * inverse) / 255);
                pixels[i + 1] = (byte)((pixels[i + 1] * alpha + 255 * inverse) / 255);
                pixels[i + 2] = (byte)((pixels[i + 2] * alpha + 255 * inverse) / 255);
                pixels[i + 3] = 255;
            }
            var flattened = BitmapSource.Create(
                width,
                height,
                96,
                96,
                System.Windows.Media.PixelFormats.Bgra32,
                null,
                pixels,
                stride
            );
            flattened.Freeze();
            return flattened;
        }

        /// <summary>
        /// File types the browser can put in an &lt;img&gt;. The chooser also offers .tif/.tiff,
        /// which it cannot, so those get a stand-in (see NeedsStandIn). Matched on extension
        /// rather than on the actual bytes deliberately: ReplyWithImage derives the response's
        /// content type from the extension, so the extension is what the browser goes on.
        /// </summary>
        private static readonly HashSet<string> s_browserRenderableExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".gif",
            ".bmp",
            ".svg",
            ".webp",
        };

        /// <summary>
        /// Whether the browser would fail to display this file as it stands, so that we should
        /// serve it a re-encoded stand-in instead. Two separate reasons: the file is a format
        /// the browser cannot draw at all, or it is so large the browser refuses to decode it
        /// (see kMaxPreviewPixels).
        /// </summary>
        private static bool NeedsStandIn(string path, int width, int height)
        {
            if (!s_browserRenderableExtensions.Contains(Path.GetExtension(path)))
                return true;
            return (long)width * height > kMaxPreviewPixels;
        }

        /// <summary>
        /// If the browser could not display <paramref name="originalPath"/> as it stands — too
        /// many pixels, or a format it cannot draw — writes a JPEG stand-in to a temp file and
        /// returns its path. Otherwise returns null, meaning "serve the original".
        ///
        /// WIC's DecodePixelWidth/Height does the scaling as part of the decode, so a 713
        /// megapixel JPEG costs about 2.5 seconds and under 100MB rather than the ~2.6GB a
        /// full decode would need.
        /// </summary>
        internal static string MakeBrowserSafePreview(string originalPath)
        {
            try
            {
                var header = ReadImageHeader(originalPath);
                if (!NeedsStandIn(originalPath, header.width, header.height))
                    return null;

                // Constrain the longer side; WIC preserves the aspect ratio when only one of
                // DecodePixelWidth/DecodePixelHeight is set, and does the scaling as part of
                // the decode rather than decoding full size and shrinking afterwards. An image
                // that is already smaller than that is left at its own size — a stand-in made
                // only because of its format has no reason to be enlarged.
                // A stream rather than a UriSource, because Uri would treat a "#" in a
                // perfectly legal filename as the start of a fragment. CacheOption.OnLoad
                // makes EndInit() do the decoding, so the stream can be closed right after.
                var scaled = new BitmapImage();
                using (var stream = RobustFile.OpenRead(originalPath))
                {
                    scaled.BeginInit();
                    scaled.StreamSource = stream;
                    scaled.CacheOption = BitmapCacheOption.OnLoad;
                    scaled.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    if (Math.Max(header.width, header.height) > kPreviewMaxDimension)
                    {
                        if (header.width >= header.height)
                            scaled.DecodePixelWidth = kPreviewMaxDimension;
                        else
                            scaled.DecodePixelHeight = kPreviewMaxDimension;
                    }
                    scaled.EndInit();
                }
                scaled.Freeze();

                // Bake in any EXIF rotation. We re-encode without an EXIF block, so a viewer
                // has no orientation tag to apply and the pixels must already be upright.
                BitmapSource upright = scaled;
                var transform = GetOrientationTransform(header.exifOrientation);
                if (transform != null)
                {
                    var rotated = new TransformedBitmap(scaled, transform);
                    rotated.Freeze();
                    upright = rotated;
                }

                // A plain temp path rather than SIL's TempFile, whose Dispose would delete the
                // file we are about to start serving. DeleteLastPickedImagePreview cleans up.
                var previewPath = Path.Combine(
                    Path.GetTempPath(),
                    "BloomImagePreview-" + Guid.NewGuid() + ".jpg"
                );
                var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                encoder.Frames.Add(BitmapFrame.Create(FlattenOntoWhite(upright)));
                using (var output = RobustFile.Create(previewPath))
                    encoder.Save(output);

                Logger.WriteMinorEvent(
                    $"ImageGalleryApi made a {upright.PixelWidth}x{upright.PixelHeight} preview"
                        + $" for {Path.GetFileName(originalPath)}"
                        + $" ({header.width}x{header.height}, which the browser could not display)"
                );
                return previewPath;
            }
            catch (Exception e)
            {
                // Includes SVG and anything else WIC can't open. Falling back to the original
                // is right: those are the formats the browser handles fine anyway.
                Logger.WriteMinorEvent(
                    $"ImageGalleryApi could not make a downscaled preview for {originalPath}: {e.Message}"
                );
                return null;
            }
        }

        /// <summary>
        /// Empties the preview cache, deleting the downscaled stand-in if there is one.
        /// </summary>
        private void DeleteLastPickedImagePreview()
        {
            lock (_previewLock)
            {
                if (!string.IsNullOrEmpty(_lastPickedLocalImagePreviewPath))
                {
                    try
                    {
                        RobustFile.Delete(_lastPickedLocalImagePreviewPath);
                    }
                    catch (Exception e)
                    {
                        // Nothing to do about it; it's a temp file, and it is named
                        // recognisably enough to be swept up later.
                        Logger.WriteMinorEvent(
                            $"ImageGalleryApi could not delete {_lastPickedLocalImagePreviewPath}: {e.Message}"
                        );
                    }
                }
                _lastPickedLocalImagePreviewPath = null;
                _lastPickedLocalImagePreviewSourcePath = null;
            }
        }

        /// <summary>
        /// Picking a new file removes the previous file's stand-in, so at most one is ever
        /// left over — the one for the last file picked. This is where that one goes; Autofac
        /// disposes us with the project's lifetime scope.
        /// </summary>
        public void Dispose()
        {
            DeleteLastPickedImagePreview();
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

            // Images past a certain size don't render in the browser at all, so substitute a
            // downscaled copy (BL-16597). What we worked out about a file is remembered and
            // reused, since the gallery asks for this URL as both the thumbnail and the large
            // preview — including the "this one is fine as it is" answer, which costs a header
            // read to reach.
            string pathToServe;
            lock (_previewLock)
            {
                var answerIsAboutThisFile =
                    _lastPickedLocalImagePreviewSourcePath == fullPath
                    && (
                        _lastPickedLocalImagePreviewPath == null
                        || RobustFile.Exists(_lastPickedLocalImagePreviewPath)
                    );
                if (!answerIsAboutThisFile)
                {
                    DeleteLastPickedImagePreview();
                    _lastPickedLocalImagePreviewPath = MakeBrowserSafePreview(fullPath);
                    _lastPickedLocalImagePreviewSourcePath = fullPath;
                }
                pathToServe = _lastPickedLocalImagePreviewPath ?? fullPath;
            }

            request.ReplyWithImage(pathToServe);
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
