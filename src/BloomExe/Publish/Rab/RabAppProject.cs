using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SIL.IO;

namespace Bloom.Publish.Rab
{
    public class RabAppSettings
    {
        public string AppName { get; set; }
        public string MainColor { get; set; }
        public string PackageName { get; set; }
        public string IconPath { get; set; }
        public string Copyright { get; set; }
    }

    public class RabBookPublishInfo
    {
        public string BookId { get; set; }
        public string FolderPath { get; set; }
        public string Title { get; set; }
        public string BloomPubPath { get; set; }
    }

    public class RabAppProject
    {
        public const string DefaultPrimaryColor = "#3F51B5";
        private const string kIconPathMetadataName = "bloom-rab-icon-path";
        private readonly XDocument _document;
        private XDocument _contentsDocument;

        private RabAppProject(string filePath, XDocument document)
        {
            FilePath = filePath;
            _document = document;
        }

        public string FilePath { get; }

        private string ProjectDataFolderPath =>
            Path.Combine(
                Path.GetDirectoryName(FilePath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(FilePath) + "_data"
            );

        private string ContentsFilePath =>
            Path.Combine(ProjectDataFolderPath, "contents", "contents.xml");

        private string BooksRootPath => Path.Combine(ProjectDataFolderPath, "books");

        public string ProjectName =>
            GetSingleElementValue("project-name") ?? Path.GetFileNameWithoutExtension(FilePath);

        public string AppName =>
            _document
                .Root?.Elements("app-name")
                .FirstOrDefault(x => (string)x.Attribute("lang") == "default")
                ?.Value
            ?? GetSingleElementValue("project-name")
            ?? Path.GetFileNameWithoutExtension(FilePath);

        public string PackageName => GetSingleElementValue("package");

        public string KeystorePath => GetNestedElementValue("signing", "keystore");

        public string KeystorePassword => GetNestedElementValue("signing", "keystore-password");

        public string KeyAlias => GetNestedElementValue("signing", "alias");

        public string AliasPassword => GetNestedElementValue("signing", "alias-password");

        public string[] BookTitles =>
            _document
                .Root?.Element("books")
                ?.Elements("book")
                .Select(book => (string)book.Element("name"))
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToArray() ?? Array.Empty<string>();

        public RabAppSettings GetAppSettings()
        {
            return new RabAppSettings()
            {
                AppName = AppName,
                MainColor = GetPrimaryColor(),
                PackageName = PackageName ?? string.Empty,
                IconPath = GetMetadataValue(kIconPathMetadataName),
                Copyright = GetMetadataValue("copyright-text"),
            };
        }

        public static RabAppProject Load(string filePath)
        {
            return new RabAppProject(
                filePath,
                XDocument.Load(filePath, LoadOptions.PreserveWhitespace)
            );
        }

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

            if (!string.IsNullOrWhiteSpace(settings?.MainColor))
                SetPrimaryColor(settings.MainColor.Trim());

            SetMetadataValue("copyright-text", settings?.Copyright?.Trim());
            SetMetadataValue(kIconPathMetadataName, settings?.IconPath?.Trim());
        }

        public void SetBookEntries(IEnumerable<RabBookPublishInfo> books)
        {
            SetBookEntries(books.Select((book, index) => ($"B{index + 1:000}", book)));
        }

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
                bookEntries.Select(book => (book.BookElementId, book.Book.Title, "thumbnail.jpg"))
            );
        }

        public void ClearBookEntries()
        {
            var booksElement = _document.Root?.Element("books");
            if (booksElement == null)
                return;

            foreach (var existingBook in booksElement.Elements("book").ToList())
                existingBook.Remove();
        }

        public void SetTrackedContentsEntries(IEnumerable<RabBookPublishInfo> books)
        {
            SetContentsEntries(
                books.Select((book, index) => ($"B{index + 1:000}", book.Title, "thumbnail.jpg"))
            );
        }

        public void Save()
        {
            _document.Save(FilePath);

            if (_contentsDocument != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ContentsFilePath) ?? string.Empty);
                _contentsDocument.Save(ContentsFilePath);
            }
        }

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

        private string GetPrimaryColor()
        {
            return _document
                    .Root?.Elements("colors")
                    .FirstOrDefault(element => (string)element.Attribute("type") == "main")
                    ?.Elements("color")
                    .FirstOrDefault(element => (string)element.Attribute("name") == "PrimaryColor")
                    ?.Elements("color-mapping")
                    .FirstOrDefault(element => (string)element.Attribute("theme") == "Normal")
                    ?.Attribute("value")
                    ?.Value ?? DefaultPrimaryColor;
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

        private void SetPrimaryColor(string mainColor)
        {
            var root = _document.Root;
            if (root == null)
                return;

            var colorsElement = root.Elements("colors")
                .FirstOrDefault(element => (string)element.Attribute("type") == "main");
            if (colorsElement == null)
            {
                colorsElement = new XElement("colors", new XAttribute("type", "main"));
                var booksElement = root.Element("books");
                if (booksElement != null)
                    booksElement.AddBeforeSelf(colorsElement);
                else
                    root.Add(colorsElement);
            }

            var primaryColorElement = colorsElement
                .Elements("color")
                .FirstOrDefault(element => (string)element.Attribute("name") == "PrimaryColor");
            if (primaryColorElement == null)
            {
                primaryColorElement = new XElement("color", new XAttribute("name", "PrimaryColor"));
                colorsElement.Add(primaryColorElement);
            }

            var normalMapping = primaryColorElement
                .Elements("color-mapping")
                .FirstOrDefault(element => (string)element.Attribute("theme") == "Normal");
            if (normalMapping == null)
            {
                normalMapping = new XElement("color-mapping", new XAttribute("theme", "Normal"));
                primaryColorElement.Add(normalMapping);
            }

            normalMapping.SetAttributeValue("value", mainColor);
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
                    .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
                    .ToArray()
            ).Trim('_');
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "Bloom_App";

            return safeName + ".apk";
        }
    }
}
