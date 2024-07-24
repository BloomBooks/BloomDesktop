using System;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Collection;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Linq;

namespace Bloom.Api
{
    /// <summary>
    /// Supports branding (e.g. logos, CC License) needed by projects.
    /// Currently we don't allow the image server to see these requests, which always occur in xmatter.
    /// Instead, as part of the process of bringing xmatter up to date, we change the image src attributes
    /// to point to the svg or png file which we copy into the book folder.
    /// This process (in XMatterHelper.CleanupBrandingImages()) allows the books to look right when
    /// opened in a browser and also in BloomReader. (It would also help with making Epubs, though that
    /// code is already written to handle branding.)
    /// Keeping this class active (a) because most of its logic is used by CleanupBrandingImages(),
    /// and (b) as a safety net, in case there's some way an api/branding url still gets presented
    /// to the image server.
    /// </summary>
    public class BrandingSettings
    {
        public const string kBrandingImageUrlPart = "branding/image";

        /// <summary>
        /// Find the requested branding image file for the given branding, looking for a .png file if the .svg file does not exist.
        /// </summary>
        /// <remarks>
        /// This method is used by EpubMaker as well as here in BrandingApi.
        /// </remarks>
        /* JDH Sep 2020 commenting out because I found this to be unused by anything
         public static string FindBrandingImageFileIfPossible(string branding, string filename, Layout layout)
        {
            string path;
            if (layout.SizeAndOrientation.IsLandScape)
            {
                // we will first try to find a landscape-specific image
                var ext = Path.GetExtension(filename);
                var filenameNoExt = Path.ChangeExtension(filename, null);
                var landscapeFileName = Path.ChangeExtension(filenameNoExt + "-landscape", ext);
                path = BloomFileLocator.GetOptionalBrandingFile(branding, landscapeFileName);
                if (!string.IsNullOrEmpty(path))
                    return path;
                path = BloomFileLocator.GetOptionalBrandingFile(branding, Path.ChangeExtension(landscapeFileName, "png"));
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
            // Note: in Bloom 3.7, our Firefox, when making PDFs, would render svg's as blurry. This was fixed in Bloom 3.8 with
            // a new Firefox. So SVGs are requested by the html...
            path = BloomFileLocator.GetOptionalBrandingFile(branding, filename);

            // ... but if there is no SVG, we can actually send back a PNG instead, and that works fine:
            if(string.IsNullOrEmpty(path))
                path = BloomFileLocator.GetOptionalBrandingFile(branding, Path.ChangeExtension(filename, "png"));

            // ... and if there is no PNG, look for a "jpg":
            if (string.IsNullOrEmpty(path))
                path = BloomFileLocator.GetOptionalBrandingFile(branding, Path.ChangeExtension(filename, "jpg"));

            return path;
        }
        */

        public class PresetItem
        {
            [JsonProperty("data-book")]
            public string DataBook;

            [JsonProperty("lang")]
            public string Lang;

            [JsonProperty("content")]
            public string Content;

            [JsonProperty("condition")]
            public string Condition; // one of always (override), ifEmpty (default), ifAllCopyrightEmpty
        }

        public class Settings
        {
            [JsonProperty("presets")]
            public PresetItem[] Presets;

            [JsonProperty("appearance")]
            public ExpandoObject Appearance;

            public string GetXmatterToUse()
            {
                var x = this.Presets.FirstOrDefault(p => p.DataBook == "xmatter");
                return x?.Content;
            }
        }

        /// <summary>
        /// extract the base and flavor parts of a Branding name
        /// </summary>
        /// <param name="fullBrandingName">the full key</param>
        /// <param name="folderName">the name before any branding; this will match the folder holding all the files.</param>
        /// <param name="flavor">a name or empty string</param>
        /// <param name="subUnitName">a name (normally a country) or empty string</param>
        public static void ParseBrandingKey(
            String fullBrandingName,
            out String folderName,
            out String flavor,
            out String subUnitName
        )
        {
            // A Branding may optionally have a suffix of the form "[FLAVOR]" where flavor is typically
            // a language name. This is used to select different logo files without having to create
            // a completely separate branding folder (complete with summary, stylesheets, etc) for each
            // language in a project that is publishing in a situation with multiple major languages.
            var parts = fullBrandingName.Split('[');
            folderName = parts[0];
            flavor = parts.Length > 1 ? parts[1].Replace("]", "") : "";

            // A Branding may optionally have a suffix of the form "(SUBUNIT)" where subUnitName is typically
            // a country name. This is useful when the project wants different codes, but wants *exactly*
            // the same branding.
            parts = folderName.Split('(');
            folderName = parts[0];
            subUnitName = parts.Length > 1 ? parts[1].Replace(")", "") : "";
        }

        /// <summary>
        /// branding folders can optionally contain a branding.json file which aligns with this Settings class
        /// </summary>
        /// <param name="brandingNameOrFolderPath"> Normally, the branding is just a name, which we look up in the official branding folder
        //but unit tests can instead provide a path to the folder.
        /// </param>
        public static Settings GetSettingsOrNull(string brandingNameOrFolderPath)
        {
            try
            {
                ParseBrandingKey(
                    brandingNameOrFolderPath,
                    out var brandingFolderName,
                    out var flavor,
                    out var subUnitName
                );

                // check to see if we have a special branding.json just for this flavor.
                // Note that we could instead add code that allows a single branding.json to
                // have rules that apply only on a flavor basis. As of 4.9, all we have is the
                // ability for a branding.json (and anything else) to use "{flavor}" anywhere in the
                // name of an image; this will often be enough to avoid making a new branding.json.
                // But if we needed to have different boilerplate text, well then we would need to
                // either use this here mechanism (separate json) or implement the ability to add
                // "flavor:" to the rules.
                string settingsPath = null;
                if (!string.IsNullOrEmpty(flavor))
                {
                    settingsPath = BloomFileLocator.GetOptionalBrandingFile(
                        brandingFolderName,
                        "branding[" + flavor + "].json"
                    );
                }

                // if not, fall bck to just "branding.json"
                if (string.IsNullOrEmpty(settingsPath))
                {
                    settingsPath = BloomFileLocator.GetOptionalBrandingFile(
                        brandingFolderName,
                        "branding.json"
                    );
                    if (string.IsNullOrEmpty(settingsPath))
                    {
                        // Is the branding missing? If not, it is guaranteed to have a branding.css.
                        var cssPath = BloomFileLocator.GetOptionalBrandingFile(
                            brandingFolderName,
                            "branding.css"
                        );
                        if (string.IsNullOrEmpty(cssPath))
                        {
                            // Branding has not yet shipped. We want the branding.json from the "Missing" branding
                            settingsPath = BloomFileLocator.GetOptionalBrandingFile(
                                "Missing",
                                "branding.json"
                            );
                        }
                    }
                }

                if (!string.IsNullOrEmpty(settingsPath))
                {
                    var content = RobustFile.ReadAllText(settingsPath);
                    if (string.IsNullOrEmpty(content))
                    {
                        NonFatalProblem.Report(
                            ModalIf.Beta,
                            PassiveIf.All,
#if DEBUG
                            // note:  That's the only place I've seen this happen.
                            $"The branding settings at '{settingsPath}' are empty. Sometimes the watch:branding:files command starts emitting empty files."
#else
                            $"The branding settings at '{settingsPath}' are empty. "
#endif
                        );
                        return null;
                    }
                    var settings = JsonConvert.DeserializeObject<Settings>(content);
                    if (settings == null)
                    {
                        NonFatalProblem.Report(
                            ModalIf.Beta,
                            PassiveIf.All,
                            "Trouble reading branding settings",
                            "branding.json of the branding "
                                + brandingNameOrFolderPath
                                + " may be corrupt. It had: "
                                + content
                        );
                        return null;
                    }

                    settings.Presets.ForEach(p =>
                    {
                        if (p.Content != null)
                        {
                            if (string.IsNullOrEmpty(flavor) && p.Content.Contains("{flavor"))
                            {
                                throw new ApplicationException(
                                    "The branding had variable {flavor} but the branding key did not specify one: "
                                        + brandingFolderName
                                );
                            }
                            p.Content = p.Content.Replace("{flavor}", flavor);
                        }
                    });
                    return settings;
                }
            }
            catch (Exception e)
            {
                NonFatalProblem.Report(
                    ModalIf.Beta,
                    PassiveIf.All,
                    "Trouble reading branding settings",
                    exception: e
                );
            }
            return null; // it is normal not to find the brandings. We hand out license keys before the brandings have been fully developed and shipped.
        }
    }
}
