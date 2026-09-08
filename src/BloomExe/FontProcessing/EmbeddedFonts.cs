using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Bloom.FontProcessing
{
    /// <summary>
    /// Supports fonts that are embedded in a book by the user dropping a .woff2 (or .woff) file
    /// directly into the book folder. Unlike system or Bloom-served fonts, these travel with the
    /// book: they are offered in the font choosers, embedded in BloomPubs, and uploaded to the
    /// Bloom library.
    ///
    /// Because @font-face declarations define the CSS font-family name, we can take the family
    /// name (and the bold/italic variant) straight from the filename and never have to parse the
    /// WOFF2 itself. This matters on Windows, where GlyphTypeface cannot read WOFF/WOFF2 metadata.
    ///
    /// Filename convention (case-insensitive, separator may be '-' or space):
    ///   Foo.woff2 / Foo-Regular.woff2  -> family "Foo", normal
    ///   Foo-Bold.woff2                 -> family "Foo", bold
    ///   Foo-Italic.woff2               -> family "Foo", italic
    ///   Foo-BoldItalic.woff2           -> family "Foo", bold italic
    ///
    /// A family must have a normal file: FontFileFinder.GetFileForFont and FontMetadata both work
    /// from the normal file, so a family with only variants (say, just Foo-Bold.woff2) is ignored.
    /// </summary>
    public static class EmbeddedFonts
    {
        // We only support web font formats for embedding (see the feature plan). These are also a
        // subset of FontMetadata.fontFileTypesBloomKnows.
        private static readonly string[] kEmbeddableExtensions = { ".woff2", ".woff" };

        /// <summary>
        /// Scan the root of the book folder for embeddable font files and group them by family name,
        /// assigning each file to the Normal/Bold/Italic/BoldItalic slot indicated by its filename.
        /// </summary>
        /// <returns>A dictionary of family name -> FontGroup of absolute file paths. Families with no
        /// normal file are left out. Empty if the folder is null/missing or contains no embeddable
        /// fonts.</returns>
        public static Dictionary<string, FontGroup> GetEmbeddedFontGroups(string bookFolderPath)
        {
            var result = new Dictionary<string, FontGroup>();
            if (string.IsNullOrEmpty(bookFolderPath) || !Directory.Exists(bookFolderPath))
                return result;

            // Only the book folder root; fonts in subfolders are not included in uploads/BloomPubs.
            foreach (var path in Directory.GetFiles(bookFolderPath))
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (!kEmbeddableExtensions.Contains(extension))
                    continue;

                ParseFamilyAndVariant(
                    Path.GetFileNameWithoutExtension(path),
                    out var family,
                    out var variant
                );
                if (string.IsNullOrEmpty(family))
                    continue;

                if (!result.TryGetValue(family, out var group))
                {
                    group = new FontGroup();
                    result[family] = group;
                }
                AssignToSlot(group, variant, path);
            }
            // A family with no normal file cannot be used: both FontFileFinder.GetFileForFont and
            // the FontMetadata constructor work from the normal file.
            foreach (
                var family in result
                    .Where(kvp => kvp.Value.Normal == null)
                    .Select(kvp => kvp.Key)
                    .ToList()
            )
                result.Remove(family);
            return result;
        }

        /// <summary>
        /// Build the @font-face declarations for the fonts embedded in the given book folder. The
        /// src urls are just the file names, which resolve relative to defaultLangStyles.css (where
        /// these are written) since both the css and the font files live in the book folder root.
        /// This is what lets the embedded font render in the editing view, in the preview, and in a
        /// book uploaded to the library. The BloomPUB and ePUB publishing code writes its own
        /// declarations into fonts.css, because there the font files land in a different folder.
        /// </summary>
        public static string GetFontFaceDeclarations(string bookFolderPath)
        {
            var builder = new System.Text.StringBuilder();
            foreach (var kvp in GetEmbeddedFontGroups(bookFolderPath))
            {
                var group = kvp.Value;
                AppendFontFace(builder, kvp.Key, "400", "normal", group.Normal);
                AppendFontFace(builder, kvp.Key, "700", "normal", group.Bold);
                AppendFontFace(builder, kvp.Key, "400", "italic", group.Italic);
                AppendFontFace(builder, kvp.Key, "700", "italic", group.BoldItalic);
            }
            return builder.ToString();
        }

        private static void AppendFontFace(
            System.Text.StringBuilder builder,
            string family,
            string weight,
            string style,
            string path
        )
        {
            if (path == null)
                return;
            var fileName = Path.GetFileName(path);
            var format = Path.GetExtension(path).ToLowerInvariant() == ".woff" ? "woff" : "woff2";
            builder.AppendLine(
                $"@font-face {{font-family:'{EscapeForCssString(family)}'; font-weight:{weight}; font-style:{style}; src:url('{EscapeForCssString(fileName)}') format('{format}');}}"
            );
        }

        /// <summary>
        /// Escape the two characters that would break out of a single-quoted CSS string. A font
        /// family name and a file name both come from a file the user put in the book folder, so
        /// either may contain an apostrophe.
        /// </summary>
        private static string EscapeForCssString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("'", "\\'");
        }

        /// <summary>
        /// Build a FontMetadata for an embedded font family without reading the font file (which
        /// GlyphTypeface cannot do for WOFF2). Per the feature decision, embedded fonts are treated
        /// as suitable for embedding.
        /// </summary>
        public static FontMetadata MakeEmbeddedFontMetadata(string family, FontGroup group)
        {
            return new FontMetadata(family, group, FontMetadata.kSourceBook);
        }

        private enum Variant
        {
            Normal,
            Bold,
            Italic,
            BoldItalic,
        }

        private static void AssignToSlot(FontGroup group, Variant variant, string path)
        {
            switch (variant)
            {
                case Variant.Bold:
                    group.Bold = path;
                    break;
                case Variant.Italic:
                    group.Italic = path;
                    break;
                case Variant.BoldItalic:
                    group.BoldItalic = path;
                    break;
                default:
                    group.Normal = path;
                    break;
            }
        }

        /// <summary>
        /// Split a filename (without extension) into a family name and a variant, using a trailing
        /// "-Suffix" or " Suffix" where the suffix names a known variant. If there is no recognized
        /// suffix, the whole name is the family and the variant is Normal.
        /// </summary>
        private static void ParseFamilyAndVariant(
            string baseName,
            out string family,
            out Variant variant
        )
        {
            family = baseName?.Trim();
            variant = Variant.Normal;
            if (string.IsNullOrEmpty(family))
                return;

            var separatorIndex = family.LastIndexOfAny(new[] { '-', ' ' });
            if (separatorIndex <= 0 || separatorIndex >= family.Length - 1)
                return;

            var suffix = family.Substring(separatorIndex + 1);
            // Normalize so "Bold Italic", "BoldItalic", and "bold-italic" all compare equal.
            var normalized = suffix.Replace(" ", "").Replace("-", "").ToLowerInvariant();
            switch (normalized)
            {
                case "regular":
                case "normal":
                    variant = Variant.Normal;
                    break;
                case "bold":
                    variant = Variant.Bold;
                    break;
                case "italic":
                    variant = Variant.Italic;
                    break;
                case "bolditalic":
                case "italicbold":
                    variant = Variant.BoldItalic;
                    break;
                default:
                    // Not a recognized variant suffix; treat the whole name as the family.
                    return;
            }
            family = family.Substring(0, separatorIndex).Trim();
        }
    }
}
