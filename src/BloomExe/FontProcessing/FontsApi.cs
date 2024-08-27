using System;
using System.Collections.Generic;
using System.Drawing.Text;
using Bloom.Api;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using SIL.Reporting;
using System.Linq;
using SIL.IO;
using System.IO;

namespace Bloom.FontProcessing
{
    public class FontsApi
    {
        public const string kApiUrlPart = "fonts/";
        private static ConcurrentDictionary<string, FontMetadata> _fontNameToMetadata =
            new ConcurrentDictionary<string, FontMetadata>();
        private static FontFileFinder _finder;

        public FontsApi() { }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "names", HandleNamesRequest, false);
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "metadata",
                HandleMetadataRequest,
                false
            );
        }

        /// <summary>
        /// Return a list of FontMetadata objects for all fonts that were available when GetAllFontMetadata() was run.
        /// If it hasn't been run yet, this will be an empty list.  GetAllFontMetadata() is presumably run at the
        /// beginning of the program.  If the user adds fonts while running Bloom, well, restarting Bloom isn't the
        /// most unexpected thing to do.
        /// </summary>
        public static IEnumerable<FontMetadata> AvailableFontMetadata =>
            GetFontMetadataSortedByName();

        public static IDictionary<string, FontMetadata> AvailableFontMetadataDictionary =>
            _fontNameToMetadata;

        public static IEnumerable<string> SortedListOfFontNames()
        {
            var list = new List<string>(NamesOfFontsThatBrowserCanRender());
            list.Sort();
            return list;
        }

        /// <summary>
        /// See https://jira.sil.org/browse/BL-802  and https://bugzilla.mozilla.org/show_bug.cgi?id=1108866
        /// Until that gets fixed, we're better off not listing those fonts that are just going to cause confusion
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<string> NamesOfFontsThatBrowserCanRender()
        {
            var serve = FontServe.GetInstance();
            var servedFontsNotInstalled = new HashSet<string>();
            foreach (var font in serve.FontsServed)
                servedFontsNotInstalled.Add(font.family);
            using (var installedFontCollection = new InstalledFontCollection())
            {
                var modifierTerms = new string[]
                {
                    "condensed",
                    "semilight",
                    "black",
                    "bold",
                    "medium",
                    "semibold",
                    "light",
                    "narrow"
                };

                foreach (var family in installedFontCollection.Families)
                {
                    var name = family.Name.ToLowerInvariant();
                    if (modifierTerms.Any(modifierTerm => name.Contains(" " + modifierTerm)))
                    {
                        continue;
                        // sorry, we just can't display that font, it will come out as some browser default font (at least on Windows, and at least up to Firefox 36)
                    }
                    // If one of the fonts Bloom is serving is also installed, don't repeat it later.
                    if (servedFontsNotInstalled.Contains(family.Name))
                        servedFontsNotInstalled.Remove(family.Name);

                    yield return family.Name;
                }
            }
            // Add any fonts that Bloom is serving that are not also installed on the local computer.
            foreach (var fontFamily in servedFontsNotInstalled)
                yield return fontFamily;
        }

        /// <summary>
        /// Return a list of FontMetadata objects for all fonts on the system.  This will be SLOW the
        /// first time it is called, but caches the result so that it will be fast in any later calls.
        /// </summary>
        /// <remarks>
        /// This method is actually called only once in Program.Main just before Run().  If something
        /// calls this a second time (or calls AvailableFontMetadata) before it finishes, then a partial
        /// list of fonts is returned.
        /// </remarks>
        public static IEnumerable<FontMetadata> GetAllFontMetadata()
        {
            // This return shouldn't be used but might be triggered by a test.
            if (_finder != null)
                return GetFontMetadataSortedByName();

            var starting = DateTime.Now;
            _finder = FontFileFinder.GetInstance(isReuseAllowed: true);
            foreach (var name in SortedListOfFontNames())
            {
                try
                {
                    var group = _finder.GetGroupForFont(name);
                    if (group == null || string.IsNullOrEmpty(group.Normal))
                        continue;
                    var meta = new FontMetadata(name, group);
                    lock (_fontNameToMetadata)
                    {
                        _fontNameToMetadata[name] = meta;
                        //System.Diagnostics.Debug.WriteLine(meta.ToString());
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Trying to get font data for \"{name}\" threw: {ex}"
                    );
                }
            }
            var list = GetFontMetadataSortedByName();
            var ending = DateTime.Now;
            System.Diagnostics.Debug.WriteLine(
                $"DEBUG: Collecting font metadata took {ending - starting}"
            );
            return list;
        }

        private void HandleNamesRequest(ApiRequest request)
        {
            var list = SortedListOfFontNames();
            request.ReplyWithJson(JsonConvert.SerializeObject(new { fonts = list }));
        }

        private void HandleMetadataRequest(ApiRequest request)
        {
            request.ReplyWithJson(JsonConvert.SerializeObject(AvailableFontMetadata));
        }

        private static IEnumerable<FontMetadata> GetFontMetadataSortedByName()
        {
            lock (_fontNameToMetadata)
            {
                var list = new List<FontMetadata>(_fontNameToMetadata.Values);
                list.Sort((a, b) => a.name.CompareTo(b.name));
                return list;
            }
        }

        internal static bool ProcessHostFontsRequest(IRequestInfo info, string localPath)
        {
            var idx = localPath.IndexOf("/host/fonts/");
            if (idx >= 0)
            {
                // If the request is for an existing file, return it.
                var path = FileLocationUtilities.GetFileDistributedWithApplication(
                    true,
                    localPath.Substring(idx + 6)
                );
                if (path != null && RobustFile.Exists(path))
                {
                    var contentType = BloomServer.GetContentType(Path.GetExtension(path));
                    info.ResponseContentType = contentType;
                    info.ReplyWithFileContent(path);
                    return true;
                }
                var serve = FontServe.GetInstance();
                var fontDesc = localPath.Substring(idx + 12);
                foreach (var fontInfo in serve.FontsServed)
                {
                    // Note that "Andika New Basic" requests will be answered by "Andika".  This
                    // code mimics what BloomReader does (in a somewhat generalized way) for handling
                    // Andika and Andika New Basic /host/fonts/ requests from bloom-player.  We need
                    // to handle bloom-player requests for BloomPub previews.
                    if (fontDesc.StartsWith(fontInfo.family) && fontInfo.family == "Andika")
                    {
                        // If the request is for a descriptive font name, do the best we can.
                        var file = fontInfo.files.normal;
                        if (fontDesc.Contains("Bold") && fontDesc.Contains("Italic"))
                            file = fontInfo.files.bolditalic;
                        else if (fontDesc.Contains("Bold"))
                            file = fontInfo.files.bold;
                        else if (fontDesc.Contains("Italic"))
                            file = fontInfo.files.italic;
                        if (String.IsNullOrEmpty(file))
                            file = fontInfo.files.normal; // must not have a requested variant.
                        path = FileLocationUtilities.GetFileDistributedWithApplication(
                            true,
                            $"fonts/{file}"
                        );
                        if (path != null && RobustFile.Exists(path))
                        {
                            var contentType = BloomServer.GetContentType(Path.GetExtension(path));
                            info.ResponseContentType = contentType;
                            info.ReplyWithFileContent(path);
                            return true;
                        }
                        break;
                    }
                }
            }
            Console.WriteLine(
                "FAILED FontsApi.ProcessHostFontsRequest(): localPath={0}",
                localPath
            );
            return false;
        }
    }
}
