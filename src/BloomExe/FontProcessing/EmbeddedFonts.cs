using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SIL.IO;

namespace Bloom.FontProcessing
{
    /// <summary>
    /// Supports fonts that Bloom stores as files so that they travel with a book instead of having
    /// to be installed on the reader's machine. Bloom makes these copies itself: when a book is
    /// brought up to date, each font it uses that Bloom does not already serve, and whose license
    /// allows embedding, is copied into a "fonts" folder beside the book folder, so one copy serves
    /// the whole collection.
    ///
    /// A book folder root may also hold font files. That is how a book carries its fonts when it
    /// leaves the collection (an upload, a single-book BloomPack, a .bloomSource, a BloomPUB, an
    /// ePUB) and how a downloaded book arrives. When such a book is brought up to date inside a
    /// collection, its root fonts move up into the collection fonts folder.
    ///
    /// Because @font-face declarations define the CSS font-family name, the family name (and the
    /// bold/italic variant) come from the filename rather than from the font file. The license and
    /// the other metadata come from the file itself.
    ///
    /// Filename convention (case-insensitive, separator may be '-' or space):
    ///   Foo.ttf / Foo-Regular.ttf  -> family "Foo", normal
    ///   Foo-Bold.ttf               -> family "Foo", bold
    ///   Foo-Italic.ttf             -> family "Foo", italic
    ///   Foo-BoldItalic.ttf         -> family "Foo", bold italic
    ///
    /// A family must have a normal file: FontFileFinder.GetFileForFont and FontMetadata both work
    /// from the normal file, so a family with only variants (say, just Foo-Bold.ttf) is ignored.
    /// </summary>
    public static class EmbeddedFonts
    {
        /// <summary>
        /// The name of the folder, beside the book folders of a collection, that holds the font
        /// files Bloom stored for that collection.
        /// </summary>
        public const string kFontsFolderName = "fonts";

        // Bloom copies the installed font file as it is, and these are the two formats an installed
        // font has. They are also a subset of FontMetadata.fontFileTypesBloomKnows.
        private static readonly string[] kEmbeddableExtensions = { ".ttf", ".otf" };

        /// <summary>
        /// The folder that holds the fonts stored for the collection that contains the given book.
        /// This is worked out from the book folder rather than from CollectionSettings, because a
        /// staged copy of a book in a temporary folder keeps the real collection's settings object
        /// and must not see the real collection's fonts.
        /// </summary>
        public static string GetCollectionFontsFolder(string bookFolderPath)
        {
            if (string.IsNullOrEmpty(bookFolderPath))
                return null;
            var collectionFolder = Path.GetDirectoryName(
                bookFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            );
            if (string.IsNullOrEmpty(collectionFolder))
                return null;
            return Path.Combine(collectionFolder, kFontsFolderName);
        }

        /// <summary>
        /// True if the file is one of the font formats Bloom stores as a copy.
        /// </summary>
        public static bool IsFontFileWeStore(string path)
        {
            return kEmbeddableExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
        }

        /// <summary>
        /// Scan the root of the book folder for font files and group them by family name, assigning
        /// each file to the Normal/Bold/Italic/BoldItalic slot indicated by its filename.
        /// </summary>
        /// <returns>A dictionary of family name -> FontGroup of absolute file paths. Families with no
        /// normal file are left out. Empty if the folder is null/missing or contains no font
        /// files.</returns>
        public static Dictionary<string, FontGroup> GetEmbeddedFontGroups(string bookFolderPath)
        {
            return ScanFolderForFontGroups(bookFolderPath);
        }

        /// <summary>
        /// Scan the fonts folder of the collection that contains the given book, and group the font
        /// files it holds by family name.
        /// </summary>
        public static Dictionary<string, FontGroup> GetCollectionFontGroups(string bookFolderPath)
        {
            return ScanFolderForFontGroups(GetCollectionFontsFolder(bookFolderPath));
        }

        /// <summary>
        /// All the font families Bloom has a file for on behalf of this book: the ones stored for
        /// the collection, plus the ones in the book folder root, which win if both have the family.
        /// </summary>
        public static Dictionary<string, StoredFontGroup> GetAvailableStoredFontGroups(
            string bookFolderPath
        )
        {
            var result = new Dictionary<string, StoredFontGroup>();
            foreach (var kvp in GetCollectionFontGroups(bookFolderPath))
                result[kvp.Key] = new StoredFontGroup(kvp.Value, FontMetadata.kSourceCollection);
            foreach (var kvp in GetEmbeddedFontGroups(bookFolderPath))
                result[kvp.Key] = new StoredFontGroup(kvp.Value, FontMetadata.kSourceBook);
            return result;
        }

        /// <summary>
        /// Reduce the result of GetAvailableStoredFontGroups to the plain FontGroups that
        /// FontFileFinder.AddEmbeddedFonts wants.
        /// </summary>
        public static Dictionary<string, FontGroup> ToFontGroups(
            IDictionary<string, StoredFontGroup> stored
        )
        {
            return stored.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Group);
        }

        private static Dictionary<string, FontGroup> ScanFolderForFontGroups(string folderPath)
        {
            var result = new Dictionary<string, FontGroup>();
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return result;

            // Only the root of the folder; fonts in subfolders are not included.
            foreach (var path in Directory.GetFiles(folderPath))
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
        /// Build the @font-face declarations for the fonts Bloom stored for this book. A font in the
        /// book folder root gets a bare file name, which resolves relative to defaultLangStyles.css
        /// (where these are written) because both live in the book folder root. A font stored for
        /// the collection gets "../fonts/", which resolves to the collection fonts folder. This is
        /// what lets a stored font render in the editing view, in the preview, and in a book
        /// uploaded to the library. The BloomPUB and ePUB publishing code writes its own
        /// declarations into fonts.css, because there the font files land in a different folder.
        /// </summary>
        public static string GetFontFaceDeclarations(string bookFolderPath)
        {
            var builder = new System.Text.StringBuilder();
            foreach (var kvp in GetAvailableStoredFontGroups(bookFolderPath))
            {
                var urlPrefix =
                    kvp.Value.Source == FontMetadata.kSourceCollection
                        ? "../" + kFontsFolderName + "/"
                        : "";
                AppendFontFaces(builder, kvp.Key, kvp.Value.Group, urlPrefix);
            }
            return builder.ToString();
        }

        /// <summary>
        /// Build the @font-face declarations for the named families, whose files are in the root of
        /// the given book folder, so their src urls are bare file names. Used for a book that is
        /// leaving the collection, where the fonts have just been copied into the book folder.
        /// </summary>
        public static string GetFontFaceDeclarationsForBookRootFonts(
            string bookFolderPath,
            ICollection<string> families
        )
        {
            var builder = new System.Text.StringBuilder();
            foreach (var kvp in GetEmbeddedFontGroups(bookFolderPath))
            {
                if (!families.Contains(kvp.Key))
                    continue;
                AppendFontFaces(builder, kvp.Key, kvp.Value, "");
            }
            return builder.ToString();
        }

        private static void AppendFontFaces(
            System.Text.StringBuilder builder,
            string family,
            FontGroup group,
            string urlPrefix
        )
        {
            AppendFontFace(builder, family, "400", "normal", group.Normal, urlPrefix);
            AppendFontFace(builder, family, "700", "normal", group.Bold, urlPrefix);
            AppendFontFace(builder, family, "400", "italic", group.Italic, urlPrefix);
            AppendFontFace(builder, family, "700", "italic", group.BoldItalic, urlPrefix);
        }

        private static void AppendFontFace(
            System.Text.StringBuilder builder,
            string family,
            string weight,
            string style,
            string path,
            string urlPrefix
        )
        {
            if (path == null)
                return;
            var fileName = Path.GetFileName(path);
            var format =
                Path.GetExtension(path).ToLowerInvariant() == ".otf" ? "opentype" : "truetype";
            builder.AppendLine(
                $"@font-face {{font-family:'{EscapeForCssString(family)}'; font-weight:{weight}; font-style:{style}; src:url('{urlPrefix}{EscapeForCssString(fileName)}') format('{format}');}}"
            );
        }

        /// <summary>
        /// Escape the two characters that would break out of a single-quoted CSS string. A font
        /// family name and a file name both come from a font file, so either may contain an
        /// apostrophe.
        /// </summary>
        private static string EscapeForCssString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("'", "\\'");
        }

        /// <summary>
        /// Build a FontMetadata for a stored font family. The family name and the variants come
        /// from the filenames, but the license and the other metadata are read out of the font file
        /// itself, so a stored font whose license forbids embedding is rejected like any other.
        /// A file that has not changed is read once per run of Bloom.
        /// </summary>
        public static FontMetadata MakeEmbeddedFontMetadata(
            string family,
            FontGroup group,
            string source
        )
        {
            return StoredFontMetadataCache.GetMetadata(family, group, source);
        }

        /// <summary>
        /// Work out which of the fonts a book uses Bloom should store a copy of. Bloom stores a
        /// font only if it has no copy already, Bloom does not serve the family itself, the
        /// license allows embedding, and the font is installed on this computer.
        /// </summary>
        /// <param name="fontsUsed">the families the book names</param>
        /// <param name="alreadyStored">true if Bloom already has a file for the family</param>
        /// <param name="servedByBloom">true if Bloom ships the family, so it needs no copy</param>
        /// <param name="getMetadata">the metadata for an installed family, or null if unknown</param>
        /// <param name="getInstalledGroup">the files of an installed family, or null if not installed</param>
        /// <returns>each family to store, with the installed files to copy</returns>
        public static IEnumerable<KeyValuePair<string, FontGroup>> ChooseFontsToStore(
            IEnumerable<string> fontsUsed,
            Func<string, bool> alreadyStored,
            Func<string, bool> servedByBloom,
            Func<string, FontMetadata> getMetadata,
            Func<string, FontGroup> getInstalledGroup
        )
        {
            foreach (var family in fontsUsed)
            {
                if (string.IsNullOrEmpty(family))
                    continue;
                if (alreadyStored(family) || servedByBloom(family))
                    continue;
                var metadata = getMetadata(family);
                if (metadata == null || metadata.determinedSuitability != FontMetadata.kOK)
                    continue;
                var group = getInstalledGroup(family);
                if (group?.Normal == null)
                    continue;
                yield return new KeyValuePair<string, FontGroup>(family, group);
            }
        }

        /// <summary>
        /// Copy the files of an installed font family into the fonts folder of the collection that
        /// contains the given book, naming them by the convention this class reads back. A file
        /// that is already there with the same length is left alone.
        /// </summary>
        public static void StoreFontInCollection(
            string bookFolderPath,
            string family,
            FontGroup installedGroup
        )
        {
            var fontsFolder = GetCollectionFontsFolder(bookFolderPath);
            if (fontsFolder == null)
                return;
            Directory.CreateDirectory(fontsFolder);
            CopyFaceToFolder(installedGroup.Normal, fontsFolder, family, "");
            CopyFaceToFolder(installedGroup.Bold, fontsFolder, family, "-Bold");
            CopyFaceToFolder(installedGroup.Italic, fontsFolder, family, "-Italic");
            CopyFaceToFolder(installedGroup.BoldItalic, fontsFolder, family, "-BoldItalic");
        }

        private static void CopyFaceToFolder(
            string sourcePath,
            string destFolder,
            string family,
            string variantSuffix
        )
        {
            if (string.IsNullOrEmpty(sourcePath) || !RobustFile.Exists(sourcePath))
                return;
            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (!kEmbeddableExtensions.Contains(extension))
                return;
            var destPath = Path.Combine(destFolder, family + variantSuffix + extension);
            if (
                RobustFile.Exists(destPath)
                && new FileInfo(destPath).Length == new FileInfo(sourcePath).Length
            )
                return;
            RobustFile.Copy(sourcePath, destPath, true);
        }

        /// <summary>
        /// Move any font files in the root of the book folder up into the collection fonts folder,
        /// so the collection holds one copy for all its books. A family the collection already has
        /// is simply removed from the book folder.
        /// </summary>
        public static void MoveBookFontsToCollection(string bookFolderPath)
        {
            var bookGroups = GetEmbeddedFontGroups(bookFolderPath);
            if (bookGroups.Count == 0)
                return;
            var fontsFolder = GetCollectionFontsFolder(bookFolderPath);
            if (fontsFolder == null)
                return;
            var collectionGroups = GetCollectionFontGroups(bookFolderPath);
            foreach (var kvp in bookGroups)
            {
                var alreadyInCollection = collectionGroups.ContainsKey(kvp.Key);
                if (!alreadyInCollection)
                {
                    Directory.CreateDirectory(fontsFolder);
                    StoreFontInCollection(bookFolderPath, kvp.Key, kvp.Value);
                }
                foreach (var path in FacePaths(kvp.Value))
                    RobustFile.Delete(path);
            }
        }

        /// <summary>
        /// Delete from the collection fonts folder the files of every family that no book in the
        /// collection uses.
        /// </summary>
        public static void RemoveUnusedCollectionFonts(
            string collectionFolder,
            ISet<string> familiesInUse
        )
        {
            var fontsFolder = Path.Combine(collectionFolder, kFontsFolderName);
            if (!Directory.Exists(fontsFolder))
                return;
            foreach (var kvp in ScanFolderForFontGroups(fontsFolder))
            {
                if (familiesInUse.Contains(kvp.Key))
                    continue;
                foreach (var path in FacePaths(kvp.Value))
                    RobustFile.Delete(path);
            }
        }

        /// <summary>
        /// Copy into the root of the outgoing book folder the stored font files for each of the
        /// named families, so the book carries its fonts when it leaves the collection.
        /// </summary>
        /// <returns>The families whose files were copied in from the collection fonts folder. The
        /// caller has to write @font-face rules for these; the ones already in the book folder root
        /// have rules already.</returns>
        public static ISet<string> CopyStoredFontsIntoBook(
            string sourceBookFolder,
            string destBookFolder,
            ISet<string> familiesUsed
        )
        {
            var copiedFromCollection = new HashSet<string>();
            foreach (var kvp in GetAvailableStoredFontGroups(sourceBookFolder))
            {
                if (!familiesUsed.Contains(kvp.Key))
                    continue;
                foreach (var path in FacePaths(kvp.Value.Group))
                {
                    var destPath = Path.Combine(destBookFolder, Path.GetFileName(path));
                    if (
                        string.Equals(
                            Path.GetFullPath(path),
                            Path.GetFullPath(destPath),
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        continue;
                    RobustFile.Copy(path, destPath, true);
                }
                if (kvp.Value.Source == FontMetadata.kSourceCollection)
                    copiedFromCollection.Add(kvp.Key);
            }
            return copiedFromCollection;
        }

        /// <summary>
        /// Copy into the root of an outgoing book folder the font files Bloom stored for it, and
        /// write the @font-face rules that point at them by bare file name. The rules that pointed
        /// into the collection fonts folder are removed, so each family keeps one rule per set of
        /// descriptors.
        /// </summary>
        public static void AddStoredFontsToOutgoingBook(
            string sourceBookFolder,
            string destBookFolder,
            ISet<string> familiesUsed
        )
        {
            var copiedFromCollection = CopyStoredFontsIntoBook(
                sourceBookFolder,
                destBookFolder,
                familiesUsed
            );
            if (copiedFromCollection.Count == 0)
                return;
            var cssPath = Path.Combine(destBookFolder, "defaultLangStyles.css");
            if (!RobustFile.Exists(cssPath))
                return;
            // The rules the book already has point at "../fonts/", which is no longer where these
            // files are. Two rules for one family and one set of descriptors are fragile, so the
            // old rules go before the bare-name ones are written.
            var staleUrls = new HashSet<string>();
            foreach (var kvp in GetEmbeddedFontGroups(destBookFolder))
            {
                if (!copiedFromCollection.Contains(kvp.Key))
                    continue;
                foreach (var path in FacePaths(kvp.Value))
                {
                    staleUrls.Add(
                        "src:url('../"
                            + kFontsFolderName
                            + "/"
                            + EscapeForCssString(Path.GetFileName(path))
                            + "')"
                    );
                }
            }
            var kept = RobustFile
                .ReadAllLines(cssPath)
                .Where(line => !line.Contains("@font-face") || !staleUrls.Any(line.Contains))
                .ToArray();
            // A rule earlier in the file wins, so the bare-name rules go at the top.
            RobustFile.WriteAllText(
                cssPath,
                GetFontFaceDeclarationsForBookRootFonts(destBookFolder, copiedFromCollection)
                    + string.Join(Environment.NewLine, kept)
                    + Environment.NewLine
            );
        }

        /// <summary>
        /// Copy a book folder to a temporary place and give the copy its own font files, for a book
        /// that is about to be zipped up and leave the collection.
        /// </summary>
        /// <returns>The path of the copy, or null if the book uses none of the fonts stored for the
        /// collection and so can be zipped where it stands.</returns>
        public static string CopyBookWithItsFonts(string bookFolderPath, string tempParentFolder)
        {
            var familiesUsed = new HashSet<string>(FontsUsedInBook.GetFontsUsed(bookFolderPath));
            if (!GetCollectionFontGroups(bookFolderPath).Keys.Any(familiesUsed.Contains))
                return null;
            var destFolder = Path.Combine(tempParentFolder, Path.GetFileName(bookFolderPath));
            Book.BookStorage.CopyDirectory(bookFolderPath, destFolder);
            AddStoredFontsToOutgoingBook(bookFolderPath, destFolder, familiesUsed);
            return destFolder;
        }

        /// <summary>
        /// Delete from the root of a book folder the font files of every family the book no longer
        /// uses, and drop the @font-face rules that pointed at them. Used on a staged copy, where
        /// the languages left out of the publication have already been stripped.
        /// </summary>
        public static void RemoveUnusedBookFonts(string bookFolderPath, ISet<string> familiesInUse)
        {
            var removedFileNames = new List<string>();
            foreach (var kvp in GetEmbeddedFontGroups(bookFolderPath))
            {
                if (familiesInUse.Contains(kvp.Key))
                    continue;
                foreach (var path in FacePaths(kvp.Value))
                {
                    removedFileNames.Add(Path.GetFileName(path));
                    RobustFile.Delete(path);
                }
            }
            if (removedFileNames.Count == 0)
                return;
            var cssPath = Path.Combine(bookFolderPath, "defaultLangStyles.css");
            if (!RobustFile.Exists(cssPath))
                return;
            var kept = RobustFile
                .ReadAllLines(cssPath)
                .Where(line =>
                    !line.Contains("@font-face")
                    || !removedFileNames.Any(name =>
                        line.Contains("src:url('" + EscapeForCssString(name) + "')")
                    )
                )
                .ToArray();
            RobustFile.WriteAllLines(cssPath, kept);
        }

        private static IEnumerable<string> FacePaths(FontGroup group)
        {
            foreach (var path in new[] { group.Normal, group.Bold, group.Italic, group.BoldItalic })
            {
                if (!string.IsNullOrEmpty(path))
                    yield return path;
            }
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

    /// <summary>
    /// A font family Bloom has files for, together with where those files live.
    /// </summary>
    public class StoredFontGroup
    {
        public FontGroup Group { get; }

        /// <summary>
        /// FontMetadata.kSourceCollection or FontMetadata.kSourceBook.
        /// </summary>
        public string Source { get; }

        public StoredFontGroup(FontGroup group, string source)
        {
            Group = group;
            Source = source;
        }
    }
}
