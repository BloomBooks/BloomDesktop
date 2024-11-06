using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.IO;
using Bloom.Api;
using SIL.IO;
using System.Threading;
using System.Diagnostics;
using SIL.Reporting;

#if __MonoCS__
using SharpFont; // Linux only (interface to libfreetype.so.6)
#else
using System.Windows.Media; // not implemented in Mono
#endif

namespace Bloom.FontProcessing
{
    /// <summary>
    /// This class handles the problem of finding what files contain the definition of the various faces of a particular font.
    /// So far the best approach involves a scan of the whole font directory, so it is much more efficient to build a dictionary
    /// once, or at least once per operation that uses it.
    /// The dictionary and method could be static, but then, we would miss the chance to find any new fonts added since
    /// the last operation that needed this information.
    /// </summary>
    class FontFileFinder : IFontFinder
    {
        private Dictionary<string, FontGroup> FontNameToFiles { get; set; }

        private static FontFileFinder _instance = null;

        /// <summary>
        /// Creates or gets an instance of the class
        /// </summary>
        /// <param name="isReuseAllowed">If false, always constructs a new instance. If true, then it operates like a singleton.
        /// If you're paranoid about the available fonts changing under you, you can pass false.
        /// Otherwise, you can pass true if you're happy with re-using whatever fonts were available the first time this ran.
        /// </param>
        public static FontFileFinder GetInstance(bool isReuseAllowed)
        {
            if (isReuseAllowed)
            {
                if (_instance == null)
                    _instance = new FontFileFinder();

                return _instance;
            }
            else
                return new FontFileFinder();
        }

        /// <summary>
        /// This is really hard. We somehow need to figure out what font file(s) are used for a particular font.
        /// http://stackoverflow.com/questions/16769758/get-a-font-filename-based-on-the-font-handle-hfont
        /// has some ideas; the result would be Windows-specific.
        ///
        /// 'AFontName' -> AFontNameRegular.ttf
        /// 'AFontName Bold' -> AFontNameBold.ttf
        /// 'AFontName Italic' -> AFontNameItalic.ttf
        /// 'AFontName Bold Italic'-> AFontNameBoldItalic.ttf
        /// If the italic variants do not exist, then the result would look like:
        /// 'BFont' -> BFontRegular.ttf
        /// 'BFont Bold' -> BFontBold.ttf
        /// 'BFont Italic' -> BFontRegular.ttf
        /// 'BFont Bold Italic' -> BFontBold.ttf
        /// </summary>
        /// <returns>file path (possibly null) that contains data for the specified font name, style and weight,
        /// and which (as far as we can tell) we are allowed to embed.
        /// If a file specific to the requested style or weight is not found, we fall back to the closest match.
        /// If nothing is found, we return null.
        /// </returns>
        /// <remarks>
        /// Once variable-weight fonts are supported, or multiple weights (SemiBold, etc), then this needs to be
        /// reworked.  The Debug.Asserts and Log message are meant to be reminders of this.
        /// </remarks>
        public string GetFileForFont(string fontName, string fontStyle, string fontWeight)
        {
            var group = GetGroupForFont(fontName);
            if (group != null)
            {
                Debug.Assert(
                    fontStyle == "italic" || fontStyle == "normal",
                    $"Unexpected fontStyle: {fontStyle}"
                );
                Debug.Assert(
                    fontWeight == "400" || fontWeight == "700",
                    $"Unexpected fontWeight: {fontWeight}"
                );
                if (
                    (fontStyle != "italic" && fontStyle != "normal")
                    || (fontWeight != "400" && fontWeight != "700")
                )
                {
                    Logger.WriteEvent(
                        $"Unexpected font settings: name=\"{fontName}\", weight=\"{fontWeight}\", style=\"{fontStyle}\""
                    );
                }

                if (fontStyle == "italic" && fontWeight == "700")
                {
                    if (!string.IsNullOrEmpty(group.BoldItalic))
                        return group.BoldItalic;
                    else if (!string.IsNullOrEmpty(group.Bold))
                        return group.Bold;
                    else if (!string.IsNullOrEmpty(group.Italic))
                        return group.Italic;
                    else if (!string.IsNullOrEmpty(group.Normal))
                        return group.Normal;
                }
                if (fontStyle == "italic" && fontWeight == "400")
                {
                    if (!string.IsNullOrEmpty(group.Italic))
                        return group.Italic;
                    else if (!string.IsNullOrEmpty(group.Normal))
                        return group.Normal;
                }
                if (fontStyle == "normal" && fontWeight == "700")
                {
                    if (!string.IsNullOrEmpty(group.Bold))
                        return group.Bold;
                    else if (!string.IsNullOrEmpty(group.Normal))
                        return group.Normal;
                }
                if (fontStyle == "normal" && fontWeight == "400")
                {
                    if (!string.IsNullOrEmpty(group.Normal))
                        return group.Normal;
                }
            }
            return null;
        }

        public HashSet<string> FontsWeCantInstall { get; private set; }

        public bool NoteFontsWeCantInstall { get; set; }

        public FontGroup GetGroupForFont(string fontName)
        {
            // Review Linux: very likely something here is not portable.
            if (FontNameToFiles == null)
                InitializeFontData();
            FontGroup result;
            FontNameToFiles.TryGetValue(fontName, out result);
            return result;
        }

        /// <summary>
        /// Note: There is some performance overhead to initializing this.
        /// </summary>
        private void InitializeFontData()
        {
            FontNameToFiles = new Dictionary<string, FontGroup>();
            FontsWeCantInstall = new HashSet<string>();
#if __MonoCS__
            using (var lib = new SharpFont.Library())
            {
                // Find all the font files in the standard system location (/usr/share/font) and $HOME/.local/share/fonts (if it exists)
                foreach (var fontFile in FindLinuxFonts())
                {
                    try
                    {
                        using (var face = new SharpFont.Face(lib, fontFile))
                        {
                            var embeddingTypes = face.GetFSTypeFlags();
                            if (
                                (embeddingTypes & EmbeddingTypes.RestrictedLicense)
                                    == EmbeddingTypes.RestrictedLicense
                                || (embeddingTypes & EmbeddingTypes.BitmapOnly)
                                    == EmbeddingTypes.BitmapOnly
                            )
                            {
                                // Our font UI allows any font on the computer, but gives the user indications that some are more
                                // useable in publishing Bloom books. The NoteFontsWeCantInstall prop is only true when we call this
                                // from BloomPubMaker so that it can note that certain fonts are unsuitable for embedding in ePUBs.
                                if (NoteFontsWeCantInstall)
                                {
                                    FontsWeCantInstall.Add(face.FamilyName);
                                    continue;
                                }
                            }
                            var name = face.FamilyName;
                            // If you care about bold, italic, etc, you can filter here.
                            FontGroup files;
                            if (!FontNameToFiles.TryGetValue(name, out files))
                            {
                                files = new FontGroup();
                                FontNameToFiles[name] = files;
                            }
                            files.Add(face, fontFile);
                        }
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
            }
#else
            foreach (var fontFile in FindWindowsFonts())
            {
                GlyphTypeface gtf;
                try
                {
                    gtf = new GlyphTypeface(new Uri("file:///" + fontFile));
                }
                catch (Exception)
                {
                    continue; // file is somehow corrupt or not really a font file? Just ignore it.
                }

                // Our font UI allows any font on the computer, but gives the user indications that some are more
                // useable in publishing Bloom books. The NoteFontsWeCantInstall prop is only true when we call this
                // from BloomPubMaker so that it can note that certain fonts are unsuitable for embedding in ePUBs.
                if (!FontIsEmbeddable(gtf.EmbeddingRights) && NoteFontsWeCantInstall)
                {
                    string name1 = GetFontNameFromFile(fontFile);
                    if (name1 != null)
                        FontsWeCantInstall.Add(name1);
                    continue; // not allowed to embed in ePUB
                }

                string name = GetFontNameFromFile(fontFile);
                if (name == null)
                    continue; // not sure how this can happen but I've seen it.
                // If you care about bold, italic, etc, you can filter here.
                FontGroup files;
                if (!FontNameToFiles.TryGetValue(name, out files))
                {
                    files = new FontGroup();
                    FontNameToFiles[name] = files;
                }
                files.Add(gtf, fontFile);
            }
            try
            {
                var serve = FontServe.GetInstance();
                if (serve.FontsServed.Count > 0)
                {
                    foreach (var fontInfo in serve.FontsServed)
                    {
                        // Store the actual paths of the fonts we'll be serving, overriding any installed font paths.
                        if (
                            !FontNameToFiles.TryGetValue(
                                fontInfo.family,
                                out FontGroup filesForFont
                            )
                        )
                            filesForFont = new FontGroup();
                        if (!String.IsNullOrEmpty(fontInfo.files.normal))
                        {
                            var pathNormal =
                                FileLocationUtilities.GetFileDistributedWithApplication(
                                    true,
                                    $"fonts/{fontInfo.files.normal}"
                                );
                            if (pathNormal != null && RobustFile.Exists(pathNormal))
                                filesForFont.Normal = pathNormal;
                        }
                        if (!String.IsNullOrEmpty(fontInfo.files.bold))
                        {
                            var pathBold = FileLocationUtilities.GetFileDistributedWithApplication(
                                true,
                                $"fonts/{fontInfo.files.bold}"
                            );
                            if (pathBold != null && RobustFile.Exists(pathBold))
                                filesForFont.Bold = pathBold;
                        }
                        if (!String.IsNullOrEmpty(fontInfo.files.italic))
                        {
                            var pathItalic =
                                FileLocationUtilities.GetFileDistributedWithApplication(
                                    true,
                                    $"fonts/{fontInfo.files.italic}"
                                );
                            if (pathItalic != null && RobustFile.Exists(pathItalic))
                                filesForFont.Italic = pathItalic;
                        }
                        if (!String.IsNullOrEmpty(fontInfo.files.bolditalic))
                        {
                            var pathBoldItalic =
                                FileLocationUtilities.GetFileDistributedWithApplication(
                                    true,
                                    $"fonts/{fontInfo.files.bolditalic}"
                                );
                            if (pathBoldItalic != null && RobustFile.Exists(pathBoldItalic))
                                filesForFont.BoldItalic = pathBoldItalic;
                        }
                        if (!String.IsNullOrEmpty(filesForFont.Normal))
                            FontNameToFiles[fontInfo.family] = filesForFont;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("DEBUG: Exception for processing font being served: {0}", e);
            }
#endif
        }

        private static string GetFontNameFromFile(string fontFile)
        {
            var fc = new PrivateFontCollection();
            try
            {
                fc.AddFontFile(fontFile);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            return fc.Families[0].Name;
        }

#if __MonoCS__
        IEnumerable<string> FindLinuxFonts()
        {
            var fontFiles = new List<string>();
            fontFiles.AddRange(FindFontsInFolder("/usr/share/fonts")); // primary system location for fonts shared by all users
            fontFiles.AddRange(FindFontsInFolder("/usr/local/share/fonts")); // secondary system location for fonts shared by all users
            fontFiles.AddRange(
                FindFontsInFolder(
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "fonts"
                    )
                )
            ); // $HOME/.local/share/fonts - used by font-manager
            fontFiles.AddRange(
                FindFontsInFolder(Environment.GetFolderPath(Environment.SpecialFolder.Fonts))
            ); // $HOME/.fonts (used for manually added user specific fonts)
            return fontFiles;
        }
#else
        IEnumerable<string> FindWindowsFonts()
        {
            var fontFiles = new List<string>();
            fontFiles.AddRange(
                FindFontsInFolder(Environment.GetFolderPath(Environment.SpecialFolder.Fonts))
            );
            // Starting with Windows 10 build 1809, it is possible to install fonts without administrator privilege in the
            // user directory %userprofile%\AppData\Local\Microsoft\Windows\Fonts.  For details, see https://github.com/matplotlib/matplotlib/issues/12954 and
            // https://social.technet.microsoft.com/Forums/Windows/en-US/4434bb03-953b-4f61-bfad-9876c1703dca/set-default-windows-font-install-location-back-to-cwindowsfonts-for-all-users-clicking?forum=win10itprogeneral.
            // For the effect on Bloom, see https://issues.bloomlibrary.org/youtrack/issue/BL-7043.
            fontFiles.AddRange(
                FindFontsInFolder(
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Microsoft",
                        "Windows",
                        "Fonts"
                    )
                )
            );
            return fontFiles;
        }

        private bool FontIsEmbeddable(FontEmbeddingRight rights)
        {
            switch (rights)
            {
                case FontEmbeddingRight.Editable:
                case FontEmbeddingRight.EditableButNoSubsetting:
                case FontEmbeddingRight.Installable:
                case FontEmbeddingRight.InstallableButNoSubsetting:
                case FontEmbeddingRight.PreviewAndPrint:
                case FontEmbeddingRight.PreviewAndPrintButNoSubsetting:
                    return true;
            }
            return false;
        }
#endif

        IEnumerable<string> FindFontsInFolder(string folder)
        {
            var fontFiles = new List<string>();
            if (Directory.Exists(folder))
            {
                foreach (var subfolder in Directory.EnumerateDirectories(folder))
                    fontFiles.AddRange(FindFontsInFolder(subfolder));
                foreach (var file in Directory.GetFiles(folder))
                {
                    var extension = Path.GetExtension(file).ToLowerInvariant();
                    if (FontMetadata.fontFileTypesBloomKnows.Contains(extension))
                    {
                        // ePUB only understands (and will embed) these types.
                        fontFiles.Add(file);
                    }
                    if (extension == ".compositefont" || extension == ".ttc")
                    {
                        // These will get marked as "unsuitable" in FontMetadata.cs, since Bloom
                        // does not understand them and can't embed them.
                        fontFiles.Add(file);
                    }
                    // All other files in the font folder we skip:
                }
            }
            return fontFiles;
        }
    }

    // its public interface (for purposes of test stubbing)
    public interface IFontFinder
    {
        string GetFileForFont(string fontName, string fontStyle, string fontWeight);
        bool NoteFontsWeCantInstall { get; set; }
        HashSet<string> FontsWeCantInstall { get; }
        FontGroup GetGroupForFont(string fontName);
    }
}
