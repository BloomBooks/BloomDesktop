using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.Publish.Rab
{
    /// <summary>
    /// Represents the subset of Reading App Builder app settings that Bloom edits directly.
    /// </summary>
    public class RabAppSettings
    {
        public string AppName { get; set; }
        public string ColorScheme { get; set; }
        public string PackageName { get; set; }
        public string IconPath { get; set; }
        public string Copyright { get; set; }
        public string About { get; set; }
    }

    /// <summary>
    /// Describes one BloomPUB export that should be wired into the Reading App Builder project.
    /// </summary>
    public class RabBookPublishInfo
    {
        public string BookId { get; set; }
        public string FolderPath { get; set; }
        public string Title { get; set; }
        public string BloomPubPath { get; set; }
        public string ThumbnailFileName { get; set; }

        [JsonIgnore]
        internal List<RabAppFontDefinition> EmbeddedFonts { get; set; }
    }

    /// <summary>
    /// Wraps a Reading App Builder .appDef file and its companion contents.xml so Bloom can keep them in sync.
    /// </summary>
    public class RabAppProject
    {
        public const string DefaultPrimaryColor = "#3F51B5";
        public const string DefaultColorScheme = "Indigo";
        public const string DefaultAboutFileName = "about.txt";
        private static readonly (int Size, string RelativePath)[] kLauncherIconFiles =
        {
            (36, @"drawable-ldpi\ic_launcher.png"),
            (48, @"drawable-mdpi\ic_launcher.png"),
            (72, @"drawable-hdpi\ic_launcher.png"),
            (96, @"drawable-xhdpi\ic_launcher.png"),
            (144, @"drawable-xxhdpi\ic_launcher.png"),
            (192, @"drawable-xxxhdpi\ic_launcher.png"),
            (512, @"drawable-web\ic_launcher.png"),
        };
        private const string kIconPathMetadataName = "bloom-rab-icon-path";
        private readonly XDocument _document;
        private XDocument _contentsDocument;

        private RabAppProject(string filePath, XDocument document)
        {
            FilePath = filePath;
            _document = document;
        }

        /// <summary>
        /// Gets the path to the underlying .appDef file.
        /// </summary>
        public string FilePath { get; }

        private string ProjectDataFolderPath =>
            Path.Combine(
                Path.GetDirectoryName(FilePath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(FilePath) + "_data"
            );

        private string ContentsFilePath =>
            Path.Combine(ProjectDataFolderPath, "contents", "contents.xml");

        private string BooksRootPath => Path.Combine(ProjectDataFolderPath, "books");

        /// <summary>
        /// Gets the project name stored in the .appDef, falling back to the file name when needed.
        /// </summary>
        public string ProjectName =>
            GetSingleElementValue("project-name") ?? Path.GetFileNameWithoutExtension(FilePath);

        /// <summary>
        /// Gets the effective default-language app name stored in the .appDef.
        /// </summary>
        public string AppName =>
            _document
                .Root?.Elements("app-name")
                .FirstOrDefault(x => (string)x.Attribute("lang") == "default")
                ?.Value
            ?? GetSingleElementValue("project-name")
            ?? Path.GetFileNameWithoutExtension(FilePath);

        /// <summary>
        /// Gets the configured Android package name.
        /// </summary>
        public string PackageName => GetSingleElementValue("package");

        /// <summary>
        /// Gets the keystore path stored in the signing section.
        /// </summary>
        public string KeystorePath => GetNestedElementValue("signing", "keystore");

        /// <summary>
        /// Gets the keystore password stored in the signing section.
        /// </summary>
        public string KeystorePassword => GetNestedElementValue("signing", "keystore-password");

        /// <summary>
        /// Gets the signing key alias stored in the project.
        /// </summary>
        public string KeyAlias => GetNestedElementValue("signing", "alias");

        /// <summary>
        /// Gets the signing key alias password stored in the project.
        /// </summary>
        public string AliasPassword => GetNestedElementValue("signing", "alias-password");

        /// <summary>
        /// Gets the current book titles listed in the .appDef.
        /// </summary>
        public string[] BookTitles =>
            _document
                .Root?.Element("books")
                ?.Elements("book")
                .Select(book => (string)book.Element("name"))
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToArray() ?? Array.Empty<string>();

        /// <summary>
        /// Reads the editable app settings from the .appDef.
        /// </summary>
        public RabAppSettings GetAppSettings()
        {
            return new RabAppSettings()
            {
                AppName = AppName,
                ColorScheme = GetColorSchemeName(),
                PackageName = PackageName ?? string.Empty,
                IconPath = GetLauncherIconPath() ?? GetMetadataValue(kIconPathMetadataName),
                Copyright = GetMetadataValue("copyright-text"),
                About = string.Empty,
            };
        }

        /// <summary>
        /// Loads a Reading App Builder project from disk while preserving existing XML whitespace.
        /// </summary>
        public static RabAppProject Load(string filePath)
        {
            return new RabAppProject(
                filePath,
                XDocument.Load(filePath, LoadOptions.PreserveWhitespace)
            );
        }

        /// <summary>
        /// Applies Bloom-managed app settings to the .appDef.
        /// </summary>
        public void SetAppSettings(RabAppSettings settings)
        {
            var root = _document.Root;
            if (root == null)
                throw new ApplicationException("RAB project file is missing its root element.");

            var appName = string.IsNullOrWhiteSpace(settings?.AppName)
                ? AppName
                : settings.AppName.Trim();
            SetDefaultAppName(appName);
            SetApkFileName(appName);

            if (!string.IsNullOrWhiteSpace(settings?.PackageName))
                SetPackageName(settings.PackageName.Trim());

            if (!string.IsNullOrWhiteSpace(settings?.ColorScheme))
                SetColorSchemeName(settings.ColorScheme.Trim());

            SetAboutFileName(DefaultAboutFileName);
            SetMetadataValue(kIconPathMetadataName, settings?.IconPath?.Trim());
            SetMetadataValue("copyright-text", settings?.Copyright?.Trim());
        }

        /// <summary>
        /// Replaces the launcher icon entries in the .appDef with the supplied generated icons.
        /// </summary>
        public void SetLauncherIcons(
            IEnumerable<(string RelativePath, int Width, int Height)> icons
        )
        {
            var root = _document.Root;
            if (root == null)
                throw new ApplicationException("RAB project file is missing its root element.");

            var imagesElement = root.Elements("images")
                .FirstOrDefault(element => (string)element.Attribute("type") == "launcher");
            if (imagesElement == null)
            {
                imagesElement = new XElement("images", new XAttribute("type", "launcher"));
                var adaptiveIconElement = root.Element("adaptive-icon");
                if (adaptiveIconElement != null)
                    adaptiveIconElement.AddBeforeSelf(imagesElement);
                else
                    root.Add(imagesElement);
            }

            imagesElement.RemoveNodes();
            foreach (var (relativePath, width, height) in icons)
            {
                imagesElement.Add(
                    new XElement(
                        "image",
                        new XAttribute("width", width),
                        new XAttribute("height", height),
                        relativePath
                    )
                );
            }
        }

        /// <summary>
        /// Sets the adaptive icon foreground image reference in the .appDef.
        /// </summary>
        public void SetAdaptiveIconForegroundImage(string fileName)
        {
            var root = _document.Root;
            if (root == null)
                throw new ApplicationException("RAB project file is missing its root element.");

            var adaptiveIconElement = root.Element("adaptive-icon");
            if (adaptiveIconElement == null)
            {
                adaptiveIconElement = new XElement("adaptive-icon");
                root.Add(adaptiveIconElement);
            }

            var foregroundElement = adaptiveIconElement.Element("foreground");
            if (foregroundElement == null)
            {
                foregroundElement = new XElement("foreground");
                adaptiveIconElement.Add(foregroundElement);
            }

            var imageElement = foregroundElement.Element("image");
            if (imageElement == null)
            {
                imageElement = new XElement("image");
                foregroundElement.Add(imageElement);
            }

            imageElement.Value = fileName;
        }

        /// <summary>
        /// Rebuilds the book list using generated sequential book ids.
        /// </summary>
        public void SetBookEntries(IEnumerable<RabBookPublishInfo> books)
        {
            SetBookEntries(books.Select((book, index) => ($"B{index + 1:000}", book)));
        }

        internal void SynchronizeFonts(IEnumerable<RabAppFontDefinition> fonts)
        {
            var root = _document.Root;
            if (root == null)
                throw new ApplicationException("RAB project file is missing its root element.");

            var desiredFonts = (fonts ?? Array.Empty<RabAppFontDefinition>())
                .Where(font =>
                    font != null
                    && !string.IsNullOrWhiteSpace(font.DisplayName)
                    && !string.IsNullOrWhiteSpace(font.FileName)
                )
                .GroupBy(
                    font =>
                        string.Join(
                            "\u001f",
                            font.DisplayName ?? string.Empty,
                            font.Weight ?? string.Empty,
                            font.Style ?? string.Empty,
                            font.FileName ?? string.Empty,
                            font.Format ?? string.Empty
                        ),
                    StringComparer.OrdinalIgnoreCase
                )
                .Select(group => group.First())
                .OrderBy(font => font.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(font => GetFontSortOrder(font.Weight, font.Style))
                .ThenBy(font => font.FontName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var fontsElement = GetOrCreateFontsElement(root);
            var existingFontElements = fontsElement
                .Elements("font")
                .Select(element => new ExistingFontElement(element))
                .ToList();
            var existingFontHandling = fontsElement.Element("font-handling");
            var referencedFamilies = GetReferencedFontFamilies();
            var usedFamilyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var updatedFontElements = new List<XElement>();

            foreach (var font in desiredFonts)
            {
                var familyId = ResolveFamilyId(font, existingFontElements);
                usedFamilyIds.Add(familyId);
                updatedFontElements.Add(CreateFontElement(familyId, font));
            }

            foreach (var existingFont in existingFontElements)
            {
                if (!referencedFamilies.Contains(existingFont.FamilyId))
                    continue;

                if (usedFamilyIds.Contains(existingFont.FamilyId))
                    continue;

                updatedFontElements.Add(new XElement(existingFont.Element));
                usedFamilyIds.Add(existingFont.FamilyId);
            }

            fontsElement.RemoveNodes();
            fontsElement.Add(
                existingFontHandling != null
                    ? new XElement(existingFontHandling)
                    : CreateDefaultFontHandlingElement()
            );
            foreach (var fontElement in updatedFontElements)
                fontsElement.Add(fontElement);
        }

        /// <summary>
        /// Rebuilds the .appDef and contents.xml book entries using the supplied imported book ids.
        /// </summary>
        public void SetBookEntries(
            IEnumerable<(string BookElementId, RabBookPublishInfo Book)> books
        )
        {
            // Update both the .appDef book list and contents.xml so RAB's catalog and home screen stay in sync.
            var bookEntries = books.ToList();
            var root = _document.Root;
            if (root == null)
                throw new ApplicationException("RAB project file is missing its root element.");

            var booksElement = root.Element("books");
            if (booksElement == null)
            {
                booksElement = new XElement("books", new XAttribute("id", "C01"));
                root.Add(booksElement);
            }

            if (booksElement.Attribute("id") == null)
                booksElement.SetAttributeValue("id", "C01");

            if (booksElement.Element("book-collection-name") == null)
                booksElement.AddFirst(new XElement("book-collection-name", "Main Collection"));

            EnsureApkFilename();

            ClearBookEntries();

            for (var index = 0; index < bookEntries.Count; index++)
            {
                var (bookElementId, book) = bookEntries[index];
                booksElement.Add(
                    new XElement(
                        "book",
                        new XAttribute("id", bookElementId),
                        new XAttribute("type", "bloom-player"),
                        new XAttribute("bloom", "true"),
                        new XAttribute("format", "html"),
                        new XElement("name", book.Title),
                        new XElement("font-choice", new XAttribute("type", "book-collection")),
                        new XElement("filename", "index.htm"),
                        new XElement("source", book.BloomPubPath),
                        new XElement(
                            "features",
                            new XAttribute("type", "book"),
                            new XElement(
                                "feature",
                                new XAttribute("name", "show-chapter-numbers"),
                                new XAttribute("value", "false")
                            )
                        )
                    )
                );
            }

            SetContentsEntries(
                bookEntries.Select(book =>
                    (book.BookElementId, book.Book.Title, GetThumbnailFileName(book.Book))
                )
            );
        }

        /// <summary>
        /// Removes all book entries from the .appDef.
        /// </summary>
        public void ClearBookEntries()
        {
            var booksElement = _document.Root?.Element("books");
            if (booksElement == null)
                return;

            foreach (var existingBook in booksElement.Elements("book").ToList())
                existingBook.Remove();
        }

        /// <summary>
        /// Rebuilds contents.xml entries for the tracked books without changing the .appDef book ids.
        /// </summary>
        public void SetTrackedContentsEntries(IEnumerable<RabBookPublishInfo> books)
        {
            SetContentsEntries(
                books.Select(
                    (book, index) => ($"B{index + 1:000}", book.Title, GetThumbnailFileName(book))
                )
            );
        }

        /// <summary>
        /// Saves the .appDef and any loaded contents.xml changes back to disk.
        /// </summary>
        public void Save()
        {
            _document.Save(FilePath);

            if (_contentsDocument != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ContentsFilePath) ?? string.Empty);
                _contentsDocument.Save(ContentsFilePath);
            }
        }

        /// <summary>
        /// Deletes unpacked generated book data so Reading App Builder imports fresh BloomPUB payloads.
        /// </summary>
        public void DeleteGeneratedBookData()
        {
            // RAB can reuse unpacked generated book payloads from a prior build, so clear them before importing new tracked books.
            if (Directory.Exists(BooksRootPath))
                RobustIO.DeleteDirectoryAndContents(BooksRootPath);
        }

        private string GetSingleElementValue(string name)
        {
            return _document.Root?.Element(name)?.Value;
        }

        /// <summary>
        /// Returns true if the project already has at least one enabled interface (app UI)
        /// language — for example one the user selected in Reading App Builder. Bloom checks this
        /// so it does not overwrite an existing choice when defaulting the interface language.
        /// A writing-system counts as enabled unless it explicitly says enabled="false", matching
        /// how Reading App Builder reads the attribute.
        /// </summary>
        internal bool HasEnabledInterfaceLanguage()
        {
            var writingSystems = _document
                .Root?.Element("interface-languages")
                ?.Element("writing-systems");
            if (writingSystems == null)
                return false;

            return writingSystems
                .Elements("writing-system")
                .Any(element =>
                    !string.Equals(
                        (string)element.Attribute("enabled"),
                        "false",
                        StringComparison.OrdinalIgnoreCase
                    )
                );
        }

        /// <summary>
        /// Enables the given language as the app's interface (UI) language, adding it to
        /// &lt;interface-languages&gt; if it is not already present. Reading App Builder requires at
        /// least one enabled interface language to build (BL-16467).
        /// </summary>
        internal void SetInterfaceLanguage(string code, string englishName, bool isRightToLeft)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Interface language code is required.", nameof(code));

            var root = _document.Root;
            if (root == null)
                throw new ApplicationException("RAB project file is missing its root element.");

            var interfaceLanguages = GetOrCreateInterfaceLanguagesElement(root);

            var writingSystems = interfaceLanguages.Element("writing-systems");
            if (writingSystems == null)
            {
                writingSystems = new XElement("writing-systems");
                interfaceLanguages.Add(writingSystems);
            }

            // Replace any existing entry for this language so the method is idempotent across
            // repeated prepare/build runs.
            writingSystems
                .Elements("writing-system")
                .Where(element =>
                    string.Equals(
                        (string)element.Attribute("code"),
                        code,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToList()
                .ForEach(element => element.Remove());

            writingSystems.Add(
                new XElement(
                    "writing-system",
                    new XAttribute("code", code),
                    new XAttribute("type", "interface"),
                    new XAttribute("enabled", "true"),
                    new XElement(
                        "display-names",
                        new XElement("form", new XAttribute("lang", "en"), englishName)
                    ),
                    new XElement("font-family", "system"),
                    new XElement(
                        "trait",
                        new XAttribute("name", "text-direction"),
                        new XAttribute("value", isRightToLeft ? "RTL" : "LTR")
                    )
                )
            );
        }

        private XElement GetOrCreateInterfaceLanguagesElement(XElement root)
        {
            var interfaceLanguages = root.Element("interface-languages");
            if (interfaceLanguages != null)
                return interfaceLanguages;

            interfaceLanguages = new XElement("interface-languages");
            var insertBefore =
                root.Element("translation-mappings")
                ?? root.Element("fonts")
                ?? root.Element("color-scheme")
                ?? root.Element("books");
            if (insertBefore != null)
                insertBefore.AddBeforeSelf(interfaceLanguages);
            else
                root.Add(interfaceLanguages);

            return interfaceLanguages;
        }

        private XElement GetOrCreateFontsElement(XElement root)
        {
            var fontsElement = root.Element("fonts");
            if (fontsElement != null)
                return fontsElement;

            fontsElement = new XElement("fonts");
            var insertBefore =
                root.Element("color-scheme")
                ?? root.Element("color-themes")
                ?? root.Element("colors")
                ?? root.Element("books");
            if (insertBefore != null)
                insertBefore.AddBeforeSelf(fontsElement);
            else
                root.Add(fontsElement);

            return fontsElement;
        }

        private HashSet<string> GetReferencedFontFamilies()
        {
            return _document
                .Descendants("text-font")
                .Select(element => (string)element.Attribute("family"))
                .Where(family => !string.IsNullOrWhiteSpace(family))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static XElement CreateDefaultFontHandlingElement()
        {
            return new XElement(
                "font-handling",
                new XElement("viewer", new XAttribute("type", "default"))
            );
        }

        private static string ResolveFamilyId(
            RabAppFontDefinition font,
            IEnumerable<ExistingFontElement> existingFonts
        )
        {
            foreach (var existingFont in existingFonts)
            {
                if (
                    string.Equals(
                        existingFont.DisplayName,
                        font.DisplayName,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        existingFont.FamilyId,
                        font.DisplayName,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        existingFont.BaseFontName,
                        font.DisplayName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return existingFont.FamilyId;
                }
            }

            return font.FamilyName ?? font.DisplayName;
        }

        private static XElement CreateFontElement(string familyId, RabAppFontDefinition font)
        {
            return new XElement(
                "font",
                new XAttribute("family", familyId),
                new XElement("font-name", font.FontName),
                new XElement("display-name", font.DisplayName),
                new XElement(
                    "filename",
                    new XAttribute("format", font.Format ?? "opentype"),
                    font.FileName
                ),
                new XElement(
                    "style-decl",
                    new XAttribute("property", "font-weight"),
                    new XAttribute("value", font.Weight ?? "normal")
                ),
                new XElement(
                    "style-decl",
                    new XAttribute("property", "font-style"),
                    new XAttribute("value", font.Style ?? "normal")
                )
            );
        }

        private static int GetFontSortOrder(string weight, string style)
        {
            var isBold = string.Equals(weight, "bold", StringComparison.OrdinalIgnoreCase);
            var isItalic = string.Equals(style, "italic", StringComparison.OrdinalIgnoreCase);
            if (!isBold && !isItalic)
                return 0;

            if (isBold && !isItalic)
                return 1;

            if (!isBold && isItalic)
                return 2;

            return 3;
        }

        private static string GetBaseFontName(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
                return string.Empty;

            foreach (var suffix in new[] { " Bold Italic", " Bold", " Italic" })
            {
                if (fontName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return fontName.Substring(0, fontName.Length - suffix.Length);
            }

            return fontName;
        }

        private class ExistingFontElement
        {
            public ExistingFontElement(XElement element)
            {
                Element = element;
                FamilyId = (string)element.Attribute("family") ?? string.Empty;
                DisplayName = (string)element.Element("display-name") ?? string.Empty;
                BaseFontName = GetBaseFontName(
                    (string)element.Element("font-name") ?? string.Empty
                );
            }

            public XElement Element { get; }
            public string FamilyId { get; }
            public string DisplayName { get; }
            public string BaseFontName { get; }
        }

        private string GetMetadataValue(string name)
        {
            return GetBooksMetadataElement()
                ?.Elements("meta")
                .FirstOrDefault(element => (string)element.Attribute("name") == name)
                ?.Attribute("content")
                ?.Value;
        }

        private string GetNestedElementValue(string parentName, string childName)
        {
            return _document.Root?.Element(parentName)?.Element(childName)?.Value;
        }

        private string GetColorSchemeName()
        {
            return _document.Root?.Element("color-scheme")?.Attribute("name")?.Value
                ?? DefaultColorScheme;
        }

        private string GetLauncherIconPath()
        {
            var adaptiveIconPath = GetAdaptiveForegroundIconPath();
            if (!string.IsNullOrWhiteSpace(adaptiveIconPath))
                return adaptiveIconPath;

            var launcherImagesElement = _document
                .Root?.Elements("images")
                .FirstOrDefault(element => (string)element.Attribute("type") == "launcher");
            if (launcherImagesElement != null)
            {
                foreach (var (size, relativePath) in kLauncherIconFiles.Reverse())
                {
                    var imageElement = launcherImagesElement
                        .Elements("image")
                        .FirstOrDefault(element => (int?)element.Attribute("width") == size);
                    var configuredPath = imageElement?.Value;
                    if (string.IsNullOrWhiteSpace(configuredPath))
                        configuredPath = relativePath;

                    var fullPath = Path.Combine(
                        ProjectDataFolderPath,
                        "images",
                        configuredPath.Replace('\\', Path.DirectorySeparatorChar)
                    );
                    if (RobustFile.Exists(fullPath))
                        return fullPath;
                }
            }

            return null;
        }

        private string GetAdaptiveForegroundIconPath()
        {
            var adaptiveForegroundFileName = _document
                .Root?.Element("adaptive-icon")
                ?.Element("foreground")
                ?.Element("image")
                ?.Value;
            if (string.IsNullOrWhiteSpace(adaptiveForegroundFileName))
                return null;

            var fullPath = Path.Combine(
                ProjectDataFolderPath,
                "images",
                "mipmap-xxxhdpi",
                adaptiveForegroundFileName
            );
            return RobustFile.Exists(fullPath) ? fullPath : null;
        }

        private void EnsureApkFilename()
        {
            var root = _document.Root;
            if (root == null)
                return;

            var apkFilenameElement = root.Element("apk-filename");
            if (apkFilenameElement == null)
            {
                apkFilenameElement = new XElement(
                    "apk-filename",
                    new XAttribute("append-version", "true")
                );
                var appNameElement = root.Elements("app-name")
                    .FirstOrDefault(x => (string)x.Attribute("lang") == "default");
                if (appNameElement != null)
                    appNameElement.AddAfterSelf(apkFilenameElement);
                else
                    root.AddFirst(apkFilenameElement);
            }

            if (apkFilenameElement.Attribute("append-version") == null)
                apkFilenameElement.SetAttributeValue("append-version", "true");

            if (string.IsNullOrWhiteSpace(apkFilenameElement.Value))
                apkFilenameElement.Value = GetApkFileName(AppName);
        }

        private void SetApkFileName(string appName)
        {
            EnsureApkFilename();
            var apkFilenameElement = _document.Root?.Element("apk-filename");
            if (apkFilenameElement != null)
                apkFilenameElement.Value = GetApkFileName(appName);
        }

        private void SetDefaultAppName(string appName)
        {
            var root = _document.Root;
            if (root == null)
                return;

            var appNameElement = root.Elements("app-name")
                .FirstOrDefault(element => (string)element.Attribute("lang") == "default");
            if (appNameElement == null)
            {
                appNameElement = new XElement("app-name", new XAttribute("lang", "default"));
                var projectNameElement = root.Element("project-name");
                if (projectNameElement != null)
                    projectNameElement.AddAfterSelf(appNameElement);
                else
                    root.AddFirst(appNameElement);
            }

            appNameElement.Value = appName;
        }

        private void SetPackageName(string packageName)
        {
            var root = _document.Root;
            if (root == null)
                return;

            var packageElement = root.Element("package");
            if (packageElement == null)
            {
                packageElement = new XElement("package");
                var appNameElement = root.Elements("app-name")
                    .FirstOrDefault(element => (string)element.Attribute("lang") == "default");
                if (appNameElement != null)
                    appNameElement.AddAfterSelf(packageElement);
                else
                    root.AddFirst(packageElement);
            }

            packageElement.Value = packageName;
        }

        private void SetColorSchemeName(string colorScheme)
        {
            var root = _document.Root;
            if (root == null)
                return;

            var colorSchemeElement = root.Element("color-scheme");
            if (colorSchemeElement == null)
            {
                colorSchemeElement = new XElement("color-scheme");
                var fontsElement = root.Element("fonts");
                if (fontsElement != null)
                    fontsElement.AddAfterSelf(colorSchemeElement);
                else
                    root.Add(colorSchemeElement);
            }

            colorSchemeElement.SetAttributeValue("name", colorScheme);
        }

        private void SetAboutFileName(string fileName)
        {
            var root = _document.Root;
            if (root == null)
                return;

            var aboutElement = root.Element("about");
            if (aboutElement == null)
            {
                aboutElement = new XElement("about");
                var deepLinkingElement = root.Element("deep-linking");
                if (deepLinkingElement != null)
                    deepLinkingElement.AddBeforeSelf(aboutElement);
                else
                    root.Add(aboutElement);
            }

            aboutElement.SetAttributeValue("enabled", "true");

            var filenameElement = aboutElement.Element("filename");
            if (filenameElement == null)
            {
                filenameElement = new XElement("filename");
                aboutElement.Add(filenameElement);
            }

            filenameElement.Value = fileName;
        }

        private XElement GetBooksMetadataElement()
        {
            return _document.Root?.Element("books")?.Element("metadata");
        }

        private void SetMetadataValue(string name, string value)
        {
            var root = _document.Root;
            if (root == null)
                return;

            var booksElement = root.Element("books");
            if (booksElement == null)
            {
                booksElement = new XElement("books", new XAttribute("id", "C01"));
                root.Add(booksElement);
            }

            var metadataElement = booksElement.Element("metadata");
            if (metadataElement == null)
            {
                metadataElement = new XElement("metadata");
                var collectionNameElement = booksElement.Element("book-collection-name");
                if (collectionNameElement != null)
                    collectionNameElement.AddAfterSelf(metadataElement);
                else
                    booksElement.AddFirst(metadataElement);
            }

            var metaElement = metadataElement
                .Elements("meta")
                .FirstOrDefault(element => (string)element.Attribute("name") == name);

            if (string.IsNullOrWhiteSpace(value))
            {
                metaElement?.Remove();
                return;
            }

            if (metaElement == null)
            {
                metaElement = new XElement("meta", new XAttribute("name", name));
                metadataElement.Add(metaElement);
            }

            metaElement.SetAttributeValue("content", value);
        }

        private void SetContentsEntries(
            IEnumerable<(string BookElementId, string Title, string ThumbnailFileName)> books
        )
        {
            var bookEntries = books.ToList();
            var contents = LoadOrCreateContentsDocument();
            var root = contents.Root;
            if (root == null)
                throw new ApplicationException("RAB contents file is missing its root element.");

            var featuresElement = GetOrCreateElement(root, "features");
            SetFeatureValue(featuresElement, "show-titles", "true");
            SetFeatureValue(featuresElement, "show-subtitles", "true");
            SetFeatureValue(featuresElement, "show-text-size-button", "true");
            SetFeatureValue(featuresElement, "launch-action", "contents");
            SetFeatureValue(featuresElement, "navigation-type", "up");
            SetFeatureValue(featuresElement, "title-type", "app-name");

            var contentsItemsElement = GetOrCreateElement(root, "contents-items");
            contentsItemsElement.RemoveNodes();

            for (var index = 0; index < bookEntries.Count; index++)
            {
                var book = bookEntries[index];
                contentsItemsElement.Add(
                    new XElement(
                        "contents-item",
                        new XAttribute("id", (index + 1).ToString()),
                        new XElement("title", new XAttribute("lang", "default"), book.Title),
                        new XElement("image-filename", book.ThumbnailFileName),
                        new XElement(
                            "link",
                            new XAttribute("type", "reference"),
                            new XAttribute("target", book.BookElementId)
                        ),
                        new XElement(
                            "features",
                            new XElement(
                                "feature",
                                new XAttribute("name", "show-reference"),
                                new XAttribute("value", "false")
                            )
                        )
                    )
                );
            }

            var contentsScreensElement = GetOrCreateElement(root, "contents-screens");
            contentsScreensElement.RemoveNodes();
            contentsScreensElement.Add(
                new XElement(
                    "contents-screen",
                    new XAttribute("id", "1"),
                    new XElement("title", new XAttribute("lang", "default"), "Home"),
                    new XElement(
                        "items",
                        bookEntries.Select(
                            (book, index) =>
                                new XElement("item", new XAttribute("id", (index + 1).ToString()))
                        )
                    )
                )
            );
        }

        private XDocument LoadOrCreateContentsDocument()
        {
            if (_contentsDocument != null)
                return _contentsDocument;

            _contentsDocument = RobustFile.Exists(ContentsFilePath)
                ? XDocument.Load(ContentsFilePath, LoadOptions.PreserveWhitespace)
                : new XDocument(new XElement("contents"));
            return _contentsDocument;
        }

        private static XElement GetOrCreateElement(XElement parent, string name)
        {
            var element = parent.Element(name);
            if (element != null)
                return element;

            element = new XElement(name);
            parent.Add(element);
            return element;
        }

        private static void SetFeatureValue(XElement featuresElement, string name, string value)
        {
            var featureElement = featuresElement
                .Elements("feature")
                .FirstOrDefault(element => (string)element.Attribute("name") == name);
            if (featureElement == null)
            {
                featureElement = new XElement("feature");
                featuresElement.Add(featureElement);
            }

            featureElement.SetAttributeValue("name", name);
            featureElement.SetAttributeValue("value", value);
        }

        private static string GetApkFileName(string appName)
        {
            var safeName = new string(
                (appName ?? string.Empty)
                    .Select(ch =>
                        (ch >= 'A' && ch <= 'Z')
                        || (ch >= 'a' && ch <= 'z')
                        || (ch >= '0' && ch <= '9')
                            ? ch
                            : '_'
                    )
                    .ToArray()
            ).Trim('_');
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "Bloom_App";

            return safeName + ".apk";
        }

        private static string GetThumbnailFileName(RabBookPublishInfo book)
        {
            return !string.IsNullOrWhiteSpace(book?.ThumbnailFileName)
                ? book.ThumbnailFileName
                : "thumbnail.jpg";
        }
    }
}
