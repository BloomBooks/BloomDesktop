using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Bloom.Collection;
using Bloom.Publish.Rab;
using BloomTests.TeamCollection;
using BloomTests.TestDoubles.CollectionTab;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Publish.Rab
{
    public class RabAppProjectTests
    {
        private const string kSampleAppDef =
            @"<?xml version='1.0' encoding='utf-8'?>
<app-definition type='RAB' program-version='13.4'>
  <project-name>Sample Project</project-name>
  <app-name lang='default'>Sample Project</app-name>
  <package>org.sil.sample</package>
  <version code='1' name='1.0'/>
  <books id='C01'>
    <book-collection-name>Main Collection</book-collection-name>
    <metadata>
      <meta name='copyright-text' content='copyright'/>
    </metadata>
    <book id='B001' type='bloom-player' bloom='true' format='html'>
      <name>Old Book</name>
      <font-choice type='book-collection'/>
      <filename>Old.htm</filename>
      <source>C:\old.bloompub</source>
    </book>
  </books>
</app-definition>";

        private static string GetRabTestDataPath(
            string fileName,
            [CallerFilePath] string currentFilePath = ""
        )
        {
            return Path.Combine(Path.GetDirectoryName(currentFilePath), "TestData", fileName);
        }

        [Test]
        public void SetBookEntries_ReplacesBookEntries_AndPreservesCollectionMetadata()
        {
            using var tempFile = TempFile.WithExtension(".appDef");
            RobustFile.WriteAllText(tempFile.Path, kSampleAppDef);

            var project = RabAppProject.Load(tempFile.Path);
            project.SetBookEntries(
                new[]
                {
                    new RabBookPublishInfo()
                    {
                        BookId = "book-1",
                        FolderPath = @"C:\books\book-1",
                        Title = "First Book",
                        BloomPubPath = @"C:\exports\first-book.bloompub",
                        ThumbnailFileName = "thumbnail.png",
                    },
                    new RabBookPublishInfo()
                    {
                        BookId = "book-2",
                        FolderPath = @"C:\books\book-2",
                        Title = "Second/Book",
                        BloomPubPath = @"C:\exports\second-book.bloompub",
                        ThumbnailFileName = "thumbnail.jpg",
                    },
                }
            );
            project.Save();

            var doc = XDocument.Load(tempFile.Path);
            var booksElement = doc.Root.Element("books");

            Assert.That(booksElement, Is.Not.Null);
            Assert.That(booksElement.Element("metadata"), Is.Not.Null);

            var books = booksElement.Elements("book").ToList();
            Assert.That(books, Has.Count.EqualTo(2));
            Assert.That(books[0].Element("name")?.Value, Is.EqualTo("First Book"));
            Assert.That(books[0].Element("filename")?.Value, Is.EqualTo("index.htm"));
            Assert.That(
                books[0].Element("source")?.Value,
                Is.EqualTo(@"C:\exports\first-book.bloompub")
            );
            Assert.That(books[1].Element("name")?.Value, Is.EqualTo("Second/Book"));
            Assert.That(books[1].Element("filename")?.Value, Is.EqualTo("index.htm"));
            Assert.That(
                books[1].Element("source")?.Value,
                Is.EqualTo(@"C:\exports\second-book.bloompub")
            );
            Assert.That(doc.Root.Element("apk-filename")?.Value, Is.EqualTo("Sample_Project.apk"));

            var contentsPath = Path.Combine(
                Path.GetDirectoryName(tempFile.Path),
                Path.GetFileNameWithoutExtension(tempFile.Path) + "_data",
                "contents",
                "contents.xml"
            );
            Assert.That(RobustFile.Exists(contentsPath), Is.True);

            var contentsDoc = XDocument.Load(contentsPath);
            var contentsItems = contentsDoc
                .Root.Element("contents-items")
                ?.Elements("contents-item")
                .ToList();
            Assert.That(contentsItems, Has.Count.EqualTo(2));
            Assert.That(contentsItems[0].Element("title")?.Value, Is.EqualTo("First Book"));
            Assert.That(
                contentsItems[0].Element("image-filename")?.Value,
                Is.EqualTo("thumbnail.png")
            );
            Assert.That(
                contentsItems[0].Element("link")?.Attribute("target")?.Value,
                Is.EqualTo("B001")
            );
            Assert.That(contentsItems[1].Element("title")?.Value, Is.EqualTo("Second/Book"));
            Assert.That(
                contentsItems[1].Element("image-filename")?.Value,
                Is.EqualTo("thumbnail.jpg")
            );
            Assert.That(
                contentsItems[1].Element("link")?.Attribute("target")?.Value,
                Is.EqualTo("B002")
            );

            var homeScreenItems = contentsDoc
                .Root.Element("contents-screens")
                ?.Element("contents-screen")
                ?.Element("items")
                ?.Elements("item")
                .Select(item => (string)item.Attribute("id"))
                .ToArray();
            Assert.That(homeScreenItems, Is.EqualTo(new[] { "1", "2" }));
            Assert.That(
                contentsDoc
                    .Root.Element("features")
                    ?.Elements("feature")
                    .FirstOrDefault(feature => (string)feature.Attribute("name") == "title-type")
                    ?.Attribute("value")
                    ?.Value,
                Is.EqualTo("app-name")
            );
        }

        [TestCase("thumbnail.png")]
        [TestCase("thumbnail.jpg")]
        public void GetBloomPubThumbnailFileName_ReturnsThumbnailPresentInBloomPub(
            string thumbnailFileName
        )
        {
            using var tempFile = TempFile.WithExtension(".bloompub");
            RobustFile.Delete(tempFile.Path);
            using (var archive = ZipFile.Open(tempFile.Path, ZipArchiveMode.Create))
            {
                archive.CreateEntry("index.htm");
                archive.CreateEntry(thumbnailFileName);
            }

            Assert.That(
                RabProjectService.GetBloomPubThumbnailFileName(tempFile.Path),
                Is.EqualTo(thumbnailFileName)
            );
        }

        [Test]
        public void DeleteGeneratedBookData_RemovesGeneratedBooksFolder_WithoutTouchingContents()
        {
            using var tempFile = TempFile.WithExtension(".appDef");
            RobustFile.WriteAllText(tempFile.Path, kSampleAppDef);

            var dataRoot = Path.Combine(
                Path.GetDirectoryName(tempFile.Path),
                Path.GetFileNameWithoutExtension(tempFile.Path) + "_data"
            );
            var booksRoot = Path.Combine(dataRoot, "books");
            var staleBookPath = Path.Combine(booksRoot, "C01", "B001", "bloom", "meta.json");
            var contentsPath = Path.Combine(dataRoot, "contents", "contents.xml");

            Directory.CreateDirectory(Path.GetDirectoryName(staleBookPath));
            RobustFile.WriteAllText(staleBookPath, "{\"title\":\"butterfly\"}");
            Directory.CreateDirectory(Path.GetDirectoryName(contentsPath));
            RobustFile.WriteAllText(contentsPath, "<contents />");

            var project = RabAppProject.Load(tempFile.Path);
            project.DeleteGeneratedBookData();

            Assert.That(Directory.Exists(booksRoot), Is.False);
            Assert.That(RobustFile.Exists(contentsPath), Is.True);
        }

        [Test]
        public void SynchronizeFonts_ReplacesUnreferencedEntries_AndPreservesReferencedFamilyIds()
        {
            using var tempFile = TempFile.WithExtension(".appDef");
            RobustFile.WriteAllText(
                tempFile.Path,
                @"<?xml version='1.0' encoding='utf-8'?>
<app-definition type='RAB' program-version='13.4'>
    <project-name>Sample Project</project-name>
    <fonts>
        <font-handling>
            <viewer type='default'/>
        </font-handling>
        <font family='font1'>
            <font-name>Andika</font-name>
            <display-name>Andika</display-name>
            <filename format='woff2'>Andika-Regular.woff2</filename>
            <style-decl property='font-weight' value='normal'/>
            <style-decl property='font-style' value='normal'/>
        </font>
        <font family='Obsolete Font'>
            <font-name>Obsolete Font</font-name>
            <display-name>Obsolete Font</display-name>
            <filename format='woff2'>Obsolete-Regular.woff2</filename>
            <style-decl property='font-weight' value='normal'/>
            <style-decl property='font-style' value='normal'/>
        </font>
    </fonts>
    <books id='C01'>
        <styles-info>
            <text-font family='font1'/>
        </styles-info>
    </books>
</app-definition>"
            );

            var project = RabAppProject.Load(tempFile.Path);
            project.SynchronizeFonts(
                new[]
                {
                    new RabAppFontDefinition
                    {
                        FamilyName = "Andika",
                        FontName = "Andika",
                        DisplayName = "Andika",
                        FileName = "Andika-Regular.woff2",
                        Format = "woff2",
                        Weight = "normal",
                        Style = "normal",
                    },
                    new RabAppFontDefinition
                    {
                        FamilyName = "ABeeZee",
                        FontName = "ABeeZee Bold",
                        DisplayName = "ABeeZee",
                        FileName = "ABeeZee-Bold.woff2",
                        Format = "woff2",
                        Weight = "bold",
                        Style = "normal",
                    },
                }
            );
            project.Save();

            var document = XDocument.Load(tempFile.Path);
            var fonts = document.Root.Element("fonts")?.Elements("font").ToList();
            var andikaFont = fonts?.Single(font => font.Element("display-name")?.Value == "Andika");
            var abeezeeFont = fonts?.Single(font =>
                font.Element("display-name")?.Value == "ABeeZee"
            );

            Assert.That(fonts, Has.Count.EqualTo(2));
            Assert.That(
                andikaFont?.Attribute("family")?.Value,
                Is.EqualTo("font1"),
                "The existing referenced family id should be preserved for Andika."
            );
            Assert.That(andikaFont?.Element("display-name")?.Value, Is.EqualTo("Andika"));
            Assert.That(abeezeeFont?.Attribute("family")?.Value, Is.EqualTo("ABeeZee"));
            Assert.That(abeezeeFont?.Element("font-name")?.Value, Is.EqualTo("ABeeZee Bold"));
            Assert.That(
                fonts.Any(font => font.Element("display-name")?.Value == "Obsolete Font"),
                Is.False,
                "Unreferenced stale font entries should be removed."
            );
        }

        [Test]
        public void ReadFontDefinitionsFromBloomPub_ReadsEmbeddedFontFaces()
        {
            var bloomPubPath = GetRabTestDataPath("Book4.bloompub");

            var fonts = RabProjectService.ReadFontDefinitionsFromBloomPub(bloomPubPath);

            Assert.That(fonts, Has.Count.EqualTo(1));
            Assert.That(fonts[0].DisplayName, Is.EqualTo("ABeeZee"));
            Assert.That(fonts[0].FontName, Is.EqualTo("ABeeZee"));
            Assert.That(fonts[0].FileName, Is.EqualTo("ABeeZee-Regular.woff2"));
            Assert.That(fonts[0].Format, Is.EqualTo("woff2"));
            Assert.That(fonts[0].Weight, Is.EqualTo("normal"));
            Assert.That(fonts[0].Style, Is.EqualTo("normal"));
        }

        [Test]
        public void ReadFontDefinitionsFromBloomPubs_UsesPreloadedEmbeddedFonts()
        {
            var fonts = RabProjectService.ReadFontDefinitionsFromBloomPubs(
                new[]
                {
                    new RabBookPublishInfo
                    {
                        BloomPubPath = Path.Combine("missing", "book.bloompub"),
                        EmbeddedFonts = new List<RabAppFontDefinition>
                        {
                            new RabAppFontDefinition
                            {
                                FamilyName = "ABeeZee",
                                FontName = "ABeeZee",
                                DisplayName = "ABeeZee",
                                FileName = "ABeeZee-Regular.woff2",
                                Format = "woff2",
                                Weight = "normal",
                                Style = "normal",
                            },
                        },
                    },
                }
            );

            Assert.That(fonts, Has.Count.EqualTo(1));
            Assert.That(fonts[0].DisplayName, Is.EqualTo("ABeeZee"));
            Assert.That(fonts[0].FileName, Is.EqualTo("ABeeZee-Regular.woff2"));
        }

        [TestCase("My Collection", "my-collection", "org.sil.bloom.my.collection")]
        [TestCase("123 Numbers First", "123-numbers-first", "org.sil.bloom.a123.numbers.first")]
        [TestCase("***", "bloom-app", "org.sil.bloom.bloom.app")]
        public void NamingHelpers_CreateDeterministicProjectAndPackageNames(
            string collectionName,
            string expectedSlug,
            string expectedPackage
        )
        {
            Assert.That(
                RabProjectService.MakeProjectSlug(collectionName),
                Is.EqualTo(expectedSlug)
            );
            Assert.That(
                RabProjectService.MakePackageName(collectionName),
                Is.EqualTo(expectedPackage)
            );
        }

        [Test]
        public void MakePackageName_UsesCopyrightHolderForOrganizationSegment()
        {
            var packageName = RabProjectService.MakePackageName(
                "Libre Foobar",
                "2019 Do Gooders",
                Array.Empty<(string organizationName, string packageSegment)>()
            );

            Assert.That(packageName, Is.EqualTo("org.dogooders.bloom.libre.foobar"));
        }

        [Test]
        public void MakePackageName_UsesOrganizationPairOverrideWhenAvailable()
        {
            var packageName = RabProjectService.MakePackageName(
                "Sample App",
                "Copyright © 2026, SIL International",
                new[] { (organizationName: "SIL International", packageSegment: "sil") }
            );

            Assert.That(packageName, Is.EqualTo("org.sil.bloom.sample.app"));
        }

        [Test]
        public void GetPackageName_UsesCollectionLanguage1TagForDefaultPrefixAndIgnoresCollectionName()
        {
            var collectionSettings = new CollectionSettings { Language1Tag = "xyz" };
            var service = new LanguageAwareRabProjectService(
                "My Story Collection",
                collectionSettings
            );

            Assert.That(service.GetPackageNameForTest(), Is.EqualTo("org.sil.xyz.stories"));
        }

        [Test]
        public void FindRabLauncherPath_UsesRegistryInstallDir_WhenAvailable()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var registryInstallDir = Path.Combine(tempFolder.Path, "registry");
            var defaultInstallDir = Path.Combine(tempFolder.Path, "default");
            Directory.CreateDirectory(registryInstallDir);
            Directory.CreateDirectory(defaultInstallDir);
            RobustFile.WriteAllText(Path.Combine(registryInstallDir, "rab.bat"), "");
            RobustFile.WriteAllText(Path.Combine(defaultInstallDir, "rab.bat"), "");

            var service = new RegistryAwareRabProjectService(
                defaultInstallDir,
                registryInstallDir,
                "15.2"
            );

            Assert.That(
                service.FindRabLauncherPath(),
                Is.EqualTo(Path.Combine(registryInstallDir, "rab.bat"))
            );
        }

        [Test]
        public void FindRabLauncherPath_FallsBackToDefaultInstallDir_WhenRegistryMissing()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var defaultInstallDir = Path.Combine(tempFolder.Path, "default");
            Directory.CreateDirectory(defaultInstallDir);
            RobustFile.WriteAllText(Path.Combine(defaultInstallDir, "rab.bat"), "");

            var service = new RegistryAwareRabProjectService(defaultInstallDir, null, null);

            Assert.That(
                service.FindRabLauncherPath(),
                Is.EqualTo(Path.Combine(defaultInstallDir, "rab.bat"))
            );
        }

        [Test]
        public void GetAvailableIconChoices_ReturnsRepresentativePngFromEachInstalledRabIconFolder()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var installDir = Path.Combine(tempFolder.Path, "Reading App Builder");
            var iconRoot = Path.Combine(installDir, "images", "icons", "rab");
            var blackIconFolder = Path.Combine(iconRoot, "ab-001-black");
            var greenIconFolder = Path.Combine(iconRoot, "ab-003-green");
            Directory.CreateDirectory(blackIconFolder);
            Directory.CreateDirectory(greenIconFolder);
            RobustFile.WriteAllText(Path.Combine(blackIconFolder, "ab-001-black.png"), "png");
            RobustFile.WriteAllText(Path.Combine(blackIconFolder, "ab-001-black-36.png"), "png");
            RobustFile.WriteAllText(Path.Combine(greenIconFolder, "ab-003-green.png"), "png");

            var service = new RegistryAwareRabProjectService(tempFolder.Path, installDir, "14.0");

            var choices = service.GetAvailableIconChoices().ToArray();

            Assert.That(
                choices.Select(choice => choice.Id),
                Is.EqualTo(new[] { "ab-001-black", "ab-003-green" })
            );
            Assert.That(
                choices[0].IconPath,
                Is.EqualTo(Path.Combine(blackIconFolder, "ab-001-black.png"))
            );
            Assert.That(
                choices[1].IconPath,
                Is.EqualTo(Path.Combine(greenIconFolder, "ab-003-green.png"))
            );
        }

        [Test]
        public void GetAvailableIconChoices_IncludesBundledIconsAlongsideInstalledRabIcons()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var installDir = Path.Combine(tempFolder.Path, "Reading App Builder");
            var installedIconRoot = Path.Combine(installDir, "images", "icons", "rab");
            var installedIconFolder = Path.Combine(installedIconRoot, "ab-003-green");
            Directory.CreateDirectory(installedIconFolder);
            RobustFile.WriteAllText(Path.Combine(installedIconFolder, "ab-003-green.png"), "png");

            var bundledIconRoot = Path.Combine(tempFolder.Path, "appbuilder-icons");
            Directory.CreateDirectory(bundledIconRoot);
            RobustFile.WriteAllText(
                Path.Combine(bundledIconRoot, "bloom-ai-01-book-stack.png"),
                "png"
            );

            var service = new RegistryAwareRabProjectService(tempFolder.Path, installDir, "14.0")
            {
                BundledIconRootToReturn = bundledIconRoot,
            };

            var choices = service.GetAvailableIconChoices().ToArray();

            Assert.That(
                choices.Select(choice => choice.Id),
                Is.EqualTo(new[] { "ab-003-green", "bloom-ai-01-book-stack" })
            );
            Assert.That(
                choices.Single(choice => choice.Id == "ab-003-green").IconPath,
                Is.EqualTo(Path.Combine(installedIconFolder, "ab-003-green.png"))
            );
            Assert.That(
                choices.Single(choice => choice.Id == "bloom-ai-01-book-stack").IconPath,
                Is.EqualTo(Path.Combine(bundledIconRoot, "bloom-ai-01-book-stack.png"))
            );
        }

        [Test]
        public void GetAvailableIconChoices_AcceptsLegacyFolderBasedBundledIcons()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var installDir = Path.Combine(tempFolder.Path, "Reading App Builder");
            var bundledIconRoot = Path.Combine(tempFolder.Path, "appbuilder-icons");
            var bundledIconFolder = Path.Combine(bundledIconRoot, "bloom-ai-01-book-stack");
            Directory.CreateDirectory(bundledIconFolder);
            RobustFile.WriteAllText(
                Path.Combine(bundledIconFolder, "bloom-ai-01-book-stack.png"),
                "png"
            );

            var service = new RegistryAwareRabProjectService(tempFolder.Path, installDir, "14.0")
            {
                BundledIconRootToReturn = bundledIconRoot,
            };

            var choices = service.GetAvailableIconChoices().ToArray();

            Assert.That(
                choices.Select(choice => choice.Id),
                Is.EqualTo(new[] { "bloom-ai-01-book-stack" })
            );
            Assert.That(
                choices[0].IconPath,
                Is.EqualTo(Path.Combine(bundledIconFolder, "bloom-ai-01-book-stack.png"))
            );
        }

        [Test]
        public void RunRabCommand_IncludesRegistryVersionInNotFoundMessage()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var service = new RegistryAwareRabProjectService(
                Path.Combine(tempFolder.Path, "default"),
                null,
                "15.2"
            );

            var error = Assert.Throws<ApplicationException>(() =>
                service.RunRabCommand("-help", tempFolder.Path)
            );

            Assert.That(error.Message, Does.Contain("registry reports version 15.2"));
        }

        [Test]
        public void GetRabRegistrySubKeys_PrefersBloomInstallerRegistryKey()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var service = new TestRabProjectService(
                paths,
                "Sample App",
                new List<RabBookPublishInfo>()
            );

            Assert.That(
                service.GetRabRegistrySubKeys(),
                Is.EqualTo(
                    new[]
                    {
                        @"Software\SIL\Reading App Builder for Bloom",
                        @"Software\SIL\Reading App Builder",
                    }
                )
            );
        }

        [Test]
        public void GetPaths_UsesBloomAppDataFolderUnderCollectionRoot()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");

            var service = new RealPathRabProjectService(tempFolder);

            var paths = service.ReadPaths();

            Assert.That(paths.RabRoot, Is.EqualTo(Path.Combine(tempFolder.Path, "Bloom App Data")));
            Assert.That(
                paths.KeystoreRoot,
                Is.EqualTo(
                    Path.Combine(
                        Bloom.ProjectContext.GetBloomAppDataFolder(),
                        "ReadingAppBuilder",
                        "keystore"
                    )
                )
            );
        }

        [Test]
        public void GetRabProcessEnvironmentVariables_UsesBloomOwnedAppData_AndClearsGlobalToolchainVars()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var service = new TestRabProjectService(
                paths,
                "Sample App",
                new List<RabBookPublishInfo>()
            );

            var environmentVariables = service.GetRabProcessEnvironmentVariables();

            Assert.That(environmentVariables["APPDATA"], Is.EqualTo(service.RabAppDataFolder));
            Assert.That(environmentVariables["ANDROID_HOME"], Is.EqualTo(string.Empty));
            Assert.That(environmentVariables["ANDROID_SDK_ROOT"], Is.EqualTo(string.Empty));
            Assert.That(environmentVariables["JAVA_HOME"], Is.EqualTo(string.Empty));
            Assert.That(environmentVariables["JDK_HOME"], Is.EqualTo(string.Empty));
            // Daemon disabled so no background JVM lingers and locks the collection folder after a build.
            Assert.That(
                environmentVariables["GRADLE_OPTS"],
                Does.Contain("-Dorg.gradle.daemon=false")
            );
        }

        [Test]
        public void ResolveAdbPath_FindsBloomManagedAndroidSdkRoot()
        {
            var localAppData = @"C:\Users\tester\AppData\Local";
            var adbPath = Path.Combine(
                localAppData,
                "SIL",
                "Bloom",
                "ReadingAppBuilder",
                "android-sdk",
                "platform-tools",
                "adb.exe"
            );

            var environmentVariables = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                { "LOCALAPPDATA", localAppData },
            };

            Assert.That(
                RabProjectService.ResolveAdbPath(
                    environmentVariables,
                    candidate =>
                        string.Equals(candidate, adbPath, StringComparison.OrdinalIgnoreCase)
                ),
                Is.EqualTo(adbPath)
            );
        }

        [Test]
        public void ResolveAdbPath_IgnoresNonBloomSdkCandidates()
        {
            var environmentVariables = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                { "ANDROID_SDK_ROOT", @"C:\sdk" },
                { "ANDROID_HOME", @"C:\android-sdk" },
                { "PATH", @"C:\sdk\platform-tools" },
            };

            Assert.That(
                RabProjectService.ResolveAdbPath(environmentVariables, candidate => true),
                Is.Null
            );
        }

        [Test]
        public void ResolveAdbPath_DoesNotFallBackToBareExecutableName()
        {
            var environmentVariables = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                { "PATH", string.Empty },
            };

            Assert.That(
                RabProjectService.ResolveAdbPath(environmentVariables, candidate => false),
                Is.Null
            );
        }

        [Test]
        public void ParseConnectedDeviceSerials_IgnoresWindowsSubsystemForAndroid()
        {
            var output = string.Join(
                Environment.NewLine,
                new[]
                {
                    "List of devices attached",
                    "38300DLJH007PN         device product:shiba model:Pixel_8 device:shiba transport_id:4",
                    "127.0.0.1:58526        device product:windows_x86_64 model:Subsystem_for_Android_TM_ device:windows_x86_64 transport_id:3",
                }
            );

            Assert.That(
                RabProjectService.ParseConnectedDeviceSerials(output),
                Is.EqualTo(new[] { "38300DLJH007PN" })
            );
        }

        [Test]
        public void ParseConnectedDeviceSerials_IgnoresWindowsSubsystemForAndroidWhenItIsAlone()
        {
            var output = string.Join(
                Environment.NewLine,
                new[]
                {
                    "List of devices attached",
                    "127.0.0.1:58526        device product:windows_x86_64 model:Subsystem_for_Android_TM_ device:windows_x86_64 transport_id:3",
                }
            );

            Assert.That(RabProjectService.ParseConnectedDeviceSerials(output), Is.Empty);
        }

        [Test]
        public void ParseConnectedDeviceSerials_KeepsMultiplePhysicalDevices()
        {
            var output = string.Join(
                Environment.NewLine,
                new[]
                {
                    "List of devices attached",
                    "38300DLJH007PN         device product:shiba model:Pixel_8 device:shiba transport_id:4",
                    "FA8AX1A00000           device product:husky model:Pixel_8_Pro device:husky transport_id:5",
                    "127.0.0.1:58526        device product:windows_x86_64 model:Subsystem_for_Android_TM_ device:windows_x86_64 transport_id:3",
                }
            );

            Assert.That(
                RabProjectService.ParseConnectedDeviceSerials(output),
                Is.EqualTo(new[] { "38300DLJH007PN", "FA8AX1A00000" })
            );
        }

        [Test]
        public void ParseConnectedDeviceDisplayNames_UsesFriendlyModelNames()
        {
            var output = string.Join(
                Environment.NewLine,
                new[]
                {
                    "List of devices attached",
                    "38300DLJH007PN         device product:shiba model:Pixel_8 device:shiba transport_id:4",
                }
            );

            Assert.That(
                RabProjectService.ParseConnectedDeviceDisplayNames(output),
                Is.EqualTo(new[] { "Pixel 8" })
            );
        }

        [Test]
        public void BuildLaunchAppArguments_TargetsLauncherCategory()
        {
            Assert.That(
                RabProjectService.BuildLaunchAppArguments(
                    "38300DLJH007PN",
                    "org.sil.bloom.sample.app"
                ),
                Is.EqualTo(
                    "-s \"38300DLJH007PN\" shell monkey -p \"org.sil.bloom.sample.app\" -c android.intent.category.LAUNCHER 1"
                )
            );
        }

        [Test]
        public async Task InstallAsync_UninstallsAndRetriesWhenExistingPackageHasDifferentSignature()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);
            await service.PrepareAsync();
            await service.BuildAsync();
            service.InstallApkResults.Enqueue(
                (
                    1,
                    "adb.exe: failed to install sample.apk: Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: Existing package org.sil.en.stories signatures do not match newer version; ignoring!]"
                )
            );
            service.InstallApkResults.Enqueue((0, "Performing Streamed Install"));

            var builtPackageName = RabAppProject.Load(service.GetStatus().AppDefPath).PackageName;

            await service.InstallAsync();

            Assert.That(service.UninstallCommands.Count, Is.EqualTo(1));
            Assert.That(
                service.UninstallCommands[0],
                Does.Contain($"uninstall \"{builtPackageName}\"")
            );
            Assert.That(service.InstallCommandCount, Is.EqualTo(2));
            Assert.That(
                service.RunProcessCommands,
                Has.Some.Contains(
                    $"shell monkey -p \"{builtPackageName}\" -c android.intent.category.LAUNCHER 1"
                )
            );
            Assert.That(service.Progress.Warnings, Has.Some.Contains("Removing it and retrying"));
        }

        [Test]
        public async Task InstallAsync_DoesNotUninstallWhenInstallFailsForAnotherReason()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);
            await service.PrepareAsync();
            await service.BuildAsync();
            service.InstallApkResults.Enqueue(
                (
                    1,
                    "adb.exe: failed to install sample.apk: Failure [INSTALL_FAILED_VERSION_DOWNGRADE]"
                )
            );

            var error = Assert.ThrowsAsync<ApplicationException>(async () =>
                await service.InstallAsync()
            );

            Assert.That(error.Message, Is.EqualTo("adb.exe exited with code 1."));
            Assert.That(service.UninstallCommands, Is.Empty);
            Assert.That(service.InstallCommandCount, Is.EqualTo(1));
        }

        [Test]
        public async Task SetupAndBuildAsync_CreatesTrackedProjectState_AndValidApk()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);

            Assert.That(service.GetStatus().ProjectExists, Is.False);
            Assert.That(service.GetStatus().ApkExists, Is.False);

            await service.PrepareAsync();
            Assert.That(service.GetStatus().BuildNeeded, Is.True);
            await service.BuildAsync();

            var status = service.GetStatus();
            Assert.That(status.RabInstalled, Is.True);
            Assert.That(status.ProjectExists, Is.True);
            Assert.That(status.ApkExists, Is.True);
            Assert.That(status.BuildNeeded, Is.False);
            Assert.That(status.ApkSizeBytes, Is.EqualTo(new FileInfo(status.ApkPath).Length));
            Assert.That(status.TrackedBookTitles, Is.EqualTo(new[] { "Book One" }));
            Assert.That(status.AppDefPath, Is.Not.Null);
            Assert.That(RobustFile.Exists(status.AppDefPath), Is.True);
            Assert.That(status.ApkPath, Is.Not.Null);
            Assert.That(RobustFile.Exists(status.ApkPath), Is.True);

            var prepareState = JsonConvert.DeserializeObject<RabPrepareState>(
                RobustFile.ReadAllText(paths.PrepareStatePath)
            );
            Assert.That(prepareState, Is.Not.Null);
            Assert.That(prepareState.AppDefPath, Is.EqualTo(status.AppDefPath));
            Assert.That(
                prepareState.Books.Select(book => book.Title),
                Is.EqualTo(new[] { "Book One" })
            );

            var project = RabAppProject.Load(status.AppDefPath);
            Assert.That(project.AppName, Is.EqualTo("Sample App"));
            Assert.That(project.BookTitles, Is.EqualTo(new[] { "Book One" }));

            using (var archive = ZipFile.OpenRead(status.ApkPath))
            {
                Assert.That(
                    archive.Entries.Any(entry => entry.FullName == "AndroidManifest.xml"),
                    Is.True
                );
                Assert.That(
                    archive.Entries.Any(entry => entry.FullName == "assets/book-1.bloompub"),
                    Is.True
                );
            }

            Assert.That(service.Commands, Has.Count.EqualTo(5));
            Assert.That(service.Commands[0], Does.StartWith("-install-sdks-if-needed "));
            Assert.That(service.Commands[0], Does.Contain("-jdk-install-folder "));
            Assert.That(service.Commands[0], Does.Contain(service.RabJdkInstallFolder));
            Assert.That(service.Commands[0], Does.Contain("-android-sdk-install-folder "));
            Assert.That(service.Commands[0], Does.Contain(service.RabAndroidSdkInstallFolder));
            Assert.That(service.Commands[1], Does.StartWith("-new "));
            Assert.That(service.Commands[1], Does.Contain("-b "));
            Assert.That(service.Commands[2], Does.StartWith("-install-sdks-if-needed "));
            Assert.That(service.Commands[2], Does.Contain("-jdk-install-folder "));
            Assert.That(service.Commands[2], Does.Contain(service.RabJdkInstallFolder));
            Assert.That(service.Commands[2], Does.Contain("-android-sdk-install-folder "));
            Assert.That(service.Commands[2], Does.Contain(service.RabAndroidSdkInstallFolder));
            Assert.That(service.Commands[3], Does.StartWith("-load "));
            Assert.That(service.Commands[3], Does.Contain("-b "));
            Assert.That(Regex.IsMatch(service.Commands[3], @"(^|\s)-build(\s|$)"), Is.False);
            Assert.That(service.Commands[4], Does.StartWith("-load "));
            Assert.That(service.Commands[4], Does.Not.Contain("-b "));
            Assert.That(Regex.IsMatch(service.Commands[4], @"(^|\s)-build(\s|$)"), Is.True);
            Assert.That(service.Progress.Stages, Does.Contain("preparing-workspace"));
            Assert.That(service.Progress.Stages, Does.Contain("installing-build-tools"));
            Assert.That(service.Progress.Stages, Does.Contain("exporting-bloompubs"));
            Assert.That(service.Progress.Stages, Does.Contain("creating-project"));
            Assert.That(service.Progress.Stages, Does.Contain("building-android-app"));
            Assert.That(service.Progress.Stages.Last(), Is.EqualTo("complete"));
            Assert.That(service.Progress.Percents.First(), Is.EqualTo(0));
            Assert.That(service.Progress.Percents, Does.Contain(5));
            Assert.That(service.Progress.Percents.Last(), Is.EqualTo(100));
        }

        [Test]
        public async Task BuildAsync_UsesRabBuildOutputToAdvanceProgress_AndTimestampLogLines()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);

            await service.PrepareAsync();
            await service.BuildAsync();

            Assert.That(service.Progress.Percents, Does.Contain(70));
            Assert.That(service.Progress.Percents, Does.Contain(97));
            Assert.That(
                service.Progress.Messages.Any(message =>
                    message.Item1.Contains("Progress: building-android-app (85%)")
                ),
                Is.True
            );
            Assert.That(
                service.Progress.Messages.Any(message =>
                    Regex.IsMatch(
                        message.Item1,
                        @"^\[\d{2}:\d{2}:\d{2}\.\d{3}\] \*\*\* Compiling Android APK \*\*\*$"
                    )
                ),
                Is.True
            );
            Assert.That(
                service.Progress.Messages.Any(message =>
                    message.Item1.Contains("BUILD SUCCESSFUL")
                ),
                Is.True
            );
        }

        [Test]
        public void GetBuildProgressPercentFromOutput_DoesNotTreatPackageReleaseResourcesAsPackageRelease()
        {
            Assert.That(
                RabProjectService.GetBuildProgressPercentFromOutput(
                    "> Task :packageReleaseResources"
                ),
                Is.Null
            );
            Assert.That(
                RabProjectService.GetBuildProgressPercentFromOutput(
                    "> Task :compileReleaseArtProfile"
                ),
                Is.EqualTo(94)
            );
            Assert.That(
                RabProjectService.GetBuildProgressPercentFromOutput("> Task :packageRelease"),
                Is.EqualTo(95)
            );
        }

        [Test]
        public void ResolveBloomPubPath_RewritesStaleDirectoryToCurrentBloomPubRoot()
        {
            var currentBloomPubRoot =
                @"C:\Users\tester\Documents\Collection Renamed\Bloom App Data\bloompubs";
            var staleBloomPubPath =
                @"C:\Users\tester\Documents\Collection Old\Bloom App Data\bloompubs\Book One.bloompub";

            var resolvedPath = RabProjectService.ResolveBloomPubPath(
                currentBloomPubRoot,
                "fallback.bloompub",
                staleBloomPubPath
            );

            Assert.That(
                resolvedPath,
                Is.EqualTo(Path.Combine(currentBloomPubRoot, "Book One.bloompub"))
            );
        }

        [Test]
        public void ResolveBloomPubPath_KeepsExistingPathWhenAlreadyInCurrentRoot()
        {
            var currentBloomPubRoot =
                @"C:\Users\tester\Documents\Collection\Bloom App Data\bloompubs";
            var existingPath = Path.Combine(currentBloomPubRoot, "Book One.bloompub");

            var resolvedPath = RabProjectService.ResolveBloomPubPath(
                currentBloomPubRoot,
                "fallback.bloompub",
                existingPath
            );

            Assert.That(resolvedPath, Is.EqualTo(existingPath));
        }

        [Test]
        public void GetStatus_ReportsBuildToolsIncomplete_WhenJdkTimezoneDatabaseIsMissing()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var service = new TestRabProjectService(
                paths,
                "Sample App",
                new List<RabBookPublishInfo>()
            );

            Directory.CreateDirectory(
                Path.Combine(
                    service.RabJdkInstallFolder,
                    "zulu17.42.19-ca-jdk17.0.7-win_x64",
                    "bin"
                )
            );
            RobustFile.WriteAllText(
                Path.Combine(
                    service.RabJdkInstallFolder,
                    "zulu17.42.19-ca-jdk17.0.7-win_x64",
                    "bin",
                    "java.exe"
                ),
                "java"
            );

            var adbPath = Path.Combine(
                service.RabAndroidSdkInstallFolder,
                "platform-tools",
                "adb.exe"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(adbPath));
            RobustFile.WriteAllText(adbPath, "adb");

            var status = service.GetStatus();

            AssertPrepareStep(status, "build-tools-installed", false);
        }

        [Test]
        public void GetStatus_IncludesUserDownloadsDirectory()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var service = new TestRabProjectService(
                paths,
                "Sample App",
                new List<RabBookPublishInfo>()
            )
            {
                UserDownloadsDirectoryToReturn = @"C:\Users\tester\Downloads",
            };

            var status = service.GetStatus();

            Assert.That(
                status.UserDownloadsDirectory,
                Is.EqualTo(service.UserDownloadsDirectoryToReturn)
            );
        }

        [Test]
        public async Task PrepareAsync_DeletesBrokenJdkInstallBeforeInstallingBuildTools()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var service = new TestRabProjectService(
                paths,
                "Sample App",
                new List<RabBookPublishInfo>()
            );

            Directory.CreateDirectory(
                Path.Combine(
                    service.RabJdkInstallFolder,
                    "zulu17.42.19-ca-jdk17.0.7-win_x64",
                    "bin"
                )
            );
            RobustFile.WriteAllText(
                Path.Combine(
                    service.RabJdkInstallFolder,
                    "zulu17.42.19-ca-jdk17.0.7-win_x64",
                    "bin",
                    "java.exe"
                ),
                "java"
            );

            await service.PrepareAsync();

            Assert.That(service.JavaExistsBeforeInstallSdksCommand, Is.False);
            Assert.That(service.TzdbExistsBeforeInstallSdksCommand, Is.False);
        }

        [Test]
        public async Task BuildAsync_RemovesGeneratedBookDataBeforeReloadingProject()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Flower",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "flower.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);
            await service.PrepareAsync();

            var appDefPath = service.GetStatus().AppDefPath;
            var booksRoot = Path.Combine(
                Path.GetDirectoryName(appDefPath),
                Path.GetFileNameWithoutExtension(appDefPath) + "_data",
                "books"
            );
            var staleBookPath = Path.Combine(booksRoot, "C01", "B001", "bloom", "meta.json");
            Directory.CreateDirectory(Path.GetDirectoryName(staleBookPath));
            RobustFile.WriteAllText(staleBookPath, "{\"title\":\"butterfly\"}");

            await service.BuildAsync();

            Assert.That(RobustFile.Exists(staleBookPath), Is.True);
            Assert.That(RobustFile.ReadAllText(staleBookPath), Does.Contain("Flower"));
            Assert.That(RobustFile.ReadAllText(staleBookPath), Does.Not.Contain("butterfly"));
            Assert.That(Directory.Exists(booksRoot), Is.True);
        }

        [Test]
        public async Task BuildAsync_RefreshesProjectFontsFromExportedBloomPubs()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Flower",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "Flower.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);
            service.FontsCssByFolderPath[trackedBooks[0].FolderPath] =
                "@font-face {font-family:'ABeeZee'; font-weight:normal; font-style:normal; src:url('ABeeZee-Regular.woff2') format('woff2');}";

            await service.PrepareAsync();
            await service.BuildAsync();

            var document = XDocument.Load(service.GetStatus().AppDefPath);
            var fonts = document.Root.Element("fonts")?.Elements("font").ToList();

            Assert.That(fonts, Is.Not.Null);
            Assert.That(fonts, Has.Count.EqualTo(1));
            Assert.That(fonts[0].Element("display-name")?.Value, Is.EqualTo("ABeeZee"));
            Assert.That(fonts[0].Element("filename")?.Value, Is.EqualTo("ABeeZee-Regular.woff2"));
            Assert.That(
                fonts[0].Element("filename")?.Attribute("format")?.Value,
                Is.EqualTo("woff2")
            );
        }

        [Test]
        public async Task BuildAsync_RemovesStaleBloomPubExportsForUntrackedBooks()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Flower",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "Flower.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);
            Directory.CreateDirectory(paths.BloomPubRoot);
            var staleBloomPubPath = Path.Combine(paths.BloomPubRoot, "butterfly.bloompub");
            RobustFile.WriteAllText(staleBloomPubPath, "BloomPUB for Butterfly");

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);
            await service.PrepareAsync();
            await service.BuildAsync();

            Assert.That(RobustFile.Exists(staleBloomPubPath), Is.False);
            Assert.That(RobustFile.Exists(trackedBooks[0].BloomPubPath), Is.True);
            Assert.That(
                Directory.GetFiles(paths.BloomPubRoot, "*.bloompub"),
                Has.Length.EqualTo(1)
            );
        }

        [Test]
        public async Task BuildAsync_ReusesExistingBloomPubsWithinCurrentScreenSession()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Flower",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "Flower.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);

            await service.PrepareAsync();
            var writesAfterPrepare = service.BloomPubWriteCount;

            await service.BuildAsync();
            var writesAfterFirstBuild = service.BloomPubWriteCount;

            await service.BuildAsync();

            Assert.That(service.BloomPubWriteCount, Is.EqualTo(writesAfterFirstBuild));
            Assert.That(writesAfterFirstBuild, Is.EqualTo(writesAfterPrepare));
            Assert.That(
                service.Progress.Messages.Select(message => message.Item1),
                Does.Contain("Reusing existing BloomPUB for Flower...")
            );
        }

        [Test]
        public async Task BuildAsync_ReusedBloomPubRefreshesTrackedThumbnailFileName()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Flower",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "Flower.bloompub"),
                    ThumbnailFileName = "thumbnail.png",
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);

            await service.PrepareAsync();

            RewriteBloomPubThumbnail(trackedBooks[0].BloomPubPath, "thumbnail.jpg");
            Assert.That(trackedBooks[0].ThumbnailFileName, Is.EqualTo("thumbnail.png"));

            await service.BuildAsync();

            var prepareState = JsonConvert.DeserializeObject<RabPrepareState>(
                RobustFile.ReadAllText(paths.PrepareStatePath)
            );
            Assert.That(prepareState?.Books?[0].ThumbnailFileName, Is.EqualTo("thumbnail.jpg"));

            var contentsPath = Path.Combine(
                Path.GetDirectoryName(prepareState.AppDefPath),
                Path.GetFileNameWithoutExtension(prepareState.AppDefPath) + "_data",
                "contents",
                "contents.xml"
            );
            var contentsDoc = XDocument.Load(contentsPath);
            Assert.That(
                contentsDoc
                    .Root?.Element("contents-items")
                    ?.Element("contents-item")
                    ?.Element("image-filename")
                    ?.Value,
                Is.EqualTo("thumbnail.jpg")
            );
        }

        [Test]
        public async Task ResetBloomPubCacheForScreenSession_DeletesExistingBloomPubs()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Flower",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "Flower.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);

            await service.PrepareAsync();
            Assert.That(RobustFile.Exists(trackedBooks[0].BloomPubPath), Is.True);

            service.ResetBloomPubCacheForScreenSession();

            Assert.That(RobustFile.Exists(trackedBooks[0].BloomPubPath), Is.False);
        }

        [Test]
        public async Task GetStatus_BuildNeededTracksSettingsAndBookOrderChanges()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
                new RabBookPublishInfo
                {
                    BookId = "book-2",
                    FolderPath = Path.Combine(tempFolder.Path, "book-2"),
                    Title = "Book Two",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-2.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);
            Directory.CreateDirectory(trackedBooks[1].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);
            await service.PrepareAsync();
            await service.BuildAsync();

            Assert.That(service.GetStatus().BuildNeeded, Is.False);

            service.SaveAppSettings(new RabAppSettings { AppName = "Updated App" });

            Assert.That(service.GetStatus().BuildNeeded, Is.True);

            await service.BuildAsync();

            Assert.That(service.GetStatus().BuildNeeded, Is.False);

            service.Commands.Clear();
            service.SaveTrackedBooks(
                new[]
                {
                    new RabTrackedBookInfo
                    {
                        BookId = "book-2",
                        FolderPath = trackedBooks[1].FolderPath,
                        Title = "Book Two",
                    },
                    new RabTrackedBookInfo
                    {
                        BookId = "book-1",
                        FolderPath = trackedBooks[0].FolderPath,
                        Title = "Book One",
                    },
                }
            );

            Assert.That(service.GetStatus().BuildNeeded, Is.True);
            Assert.That(service.Commands, Is.Empty);
        }

        [Test]
        public async Task SaveTrackedBooks_RefreshesPersistedThumbnailFileNameFromExistingBloomPub()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                    ThumbnailFileName = "thumbnail.png",
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);
            await service.PrepareAsync();
            await service.BuildAsync();

            RewriteBloomPubThumbnail(trackedBooks[0].BloomPubPath, "thumbnail.jpg");

            service.SaveTrackedBooks(
                new[]
                {
                    new RabTrackedBookInfo
                    {
                        BookId = trackedBooks[0].BookId,
                        FolderPath = trackedBooks[0].FolderPath,
                        Title = trackedBooks[0].Title,
                    },
                }
            );

            var prepareState = JsonConvert.DeserializeObject<RabPrepareState>(
                RobustFile.ReadAllText(paths.PrepareStatePath)
            );
            Assert.That(prepareState?.Books?[0].ThumbnailFileName, Is.EqualTo("thumbnail.jpg"));
        }

        [Test]
        public async Task SaveAppSettings_UpdatesProjectWithoutRunningRabCommand()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);
            await service.PrepareAsync();
            service.Commands.Clear();

            service.SaveAppSettings(
                new RabAppSettings()
                {
                    AppName = "Updated App",
                    PackageName = "org.sil.bloom.updated.app",
                    Copyright = "Updated Copyright",
                    About = "Updated about text",
                }
            );

            Assert.That(service.Commands, Is.Empty);

            var project = RabAppProject.Load(service.GetStatus().AppDefPath);
            var settings = project.GetAppSettings();
            Assert.That(project.AppName, Is.EqualTo("Updated App"));
            Assert.That(project.PackageName, Is.EqualTo("org.sil.bloom.updated.app"));
            Assert.That(settings.PackageName, Is.EqualTo("org.sil.bloom.updated.app"));
            Assert.That(settings.Copyright, Is.EqualTo("Updated Copyright"));
            Assert.That(
                RobustFile.ReadAllText(paths.AboutTextPath),
                Is.EqualTo("Updated about text")
            );
        }

        [Test]
        public async Task SaveAppSettings_DoesNotPersistShadowSettingsAfterProjectExists()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);
            await service.PrepareAsync();

            service.SaveAppSettings(
                new RabAppSettings
                {
                    AppName = "Project App",
                    PackageName = "org.sil.bloom.project.app",
                }
            );

            var persistedState = JObject.Parse(RobustFile.ReadAllText(paths.PrepareStatePath));
            Assert.That(persistedState["Settings"], Is.Null);
        }

        [Test]
        public async Task PrepareAsync_NewProject_StoresSigningKeyInBloomOwnedFolder()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var bloomOwnedRabRoot = Path.Combine(tempFolder.Path, "user", "ReadingAppBuilder");
            var paths = new RabWorkspacePaths(tempFolder.Path, bloomOwnedRabRoot);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);

            await service.PrepareAsync();

            var prepareState = JsonConvert.DeserializeObject<RabPrepareState>(
                RobustFile.ReadAllText(paths.PrepareStatePath)
            );
            Assert.That(prepareState.KeystorePath, Is.EqualTo(paths.SharedKeystorePath));
            Assert.That(prepareState.KeystorePath.StartsWith(paths.RabRoot), Is.False);
            Assert.That(RobustFile.Exists(paths.SharedKeystorePath), Is.True);
            Assert.That(RobustFile.Exists(paths.SharedSigningStatePath), Is.True);

            var project = RabAppProject.Load(service.GetStatus().AppDefPath);
            Assert.That(project.KeystorePath, Is.EqualTo(paths.SharedKeystorePath));
        }

        [Test]
        public async Task PrepareAsync_Reprepare_KeepsUsingBloomOwnedSigningKey()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var bloomOwnedRabRoot = Path.Combine(tempFolder.Path, "user", "ReadingAppBuilder");
            var paths = new RabWorkspacePaths(tempFolder.Path, bloomOwnedRabRoot);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);

            await service.PrepareAsync();
            await service.PrepareAsync();

            var prepareState = JsonConvert.DeserializeObject<RabPrepareState>(
                RobustFile.ReadAllText(paths.PrepareStatePath)
            );
            Assert.That(prepareState.KeystorePath, Is.EqualTo(paths.SharedKeystorePath));
            Assert.That(RobustFile.Exists(paths.SharedKeystorePath), Is.True);

            var project = RabAppProject.Load(service.GetStatus().AppDefPath);
            Assert.That(project.KeystorePath, Is.EqualTo(paths.SharedKeystorePath));
        }

        [Test]
        public void SaveAppSettings_ThrowsBeforePrepareCreatesProject()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);
            var error = Assert.Throws<ApplicationException>(() =>
                service.SaveAppSettings(
                    new RabAppSettings
                    {
                        AppName = "Configured App",
                        PackageName = "org.sil.bloom.configured.app",
                    }
                )
            );

            Assert.That(
                error?.Message,
                Is.EqualTo("Run Prepare before customizing the Reading App Builder project.")
            );
        }

        [Test]
        public async Task OpenInRab_SeedsRabSettingsAndLaunchesUi()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);
            await service.PrepareAsync();

            service.OpenInRab();

            var rabSettingsPath = service.GetRabSettingsFilePath();
            var settingsDocument = XDocument.Load(rabSettingsPath);

            Assert.That(service.DetachedFileName, Is.EqualTo("cmd.exe"));
            Assert.That(service.DetachedArguments, Does.Contain("call \""));
            Assert.That(service.DetachedArguments, Does.Not.Contain("-load"));
            Assert.That(
                service.DetachedWorkingDirectory,
                Is.EqualTo(Path.GetDirectoryName(service.RabLauncherPathToReturn))
            );
            Assert.That(
                service.DetachedEnvironmentVariables["APPDATA"],
                Is.EqualTo(service.RabAppDataFolder)
            );
            Assert.That(
                settingsDocument.Root?.Element("apps")?.Element("app")?.Element("filename")?.Value,
                Is.EqualTo(service.GetStatus().AppDefPath)
            );
        }

        [Test]
        public async Task OpenInRab_WhenRabAlreadyRunning_BringsItToFrontWithoutLaunching()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks)
            {
                TryBringRunningRabToFrontResult = true,
            };
            await service.PrepareAsync();

            service.OpenInRab();

            Assert.That(service.TryBringRunningRabToFrontCallCount, Is.EqualTo(1));
            Assert.That(service.DetachedFileName, Is.Null);
            Assert.That(RobustFile.Exists(service.GetRabSettingsFilePath()), Is.False);
        }

        [Test]
        public async Task OpenInRab_PreservesExistingSettingsWhenReplacingOpenApps()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);
            await service.PrepareAsync();

            Directory.CreateDirectory(Path.GetDirectoryName(service.GetRabSettingsFilePath()));
            new XDocument(
                new XElement(
                    "settings",
                    new XElement(
                        "preferences",
                        new XElement("preference", new XAttribute("name", "theme"), "dark")
                    ),
                    new XElement(
                        "apps",
                        new XElement(
                            "app",
                            new XElement("name", "Old App"),
                            new XElement("filename", @"C:\old\old.appDef")
                        )
                    )
                )
            ).Save(service.GetRabSettingsFilePath());

            service.OpenInRab();

            var settingsDocument = XDocument.Load(service.GetRabSettingsFilePath());

            Assert.That(
                settingsDocument.Root?.Element("preferences")?.Element("preference")?.Value,
                Is.EqualTo("dark")
            );
            Assert.That(
                settingsDocument.Root?.Element("apps")?.Elements("app").Count(),
                Is.EqualTo(1)
            );
            Assert.That(
                settingsDocument.Root?.Element("apps")?.Element("app")?.Element("filename")?.Value,
                Is.EqualTo(service.GetStatus().AppDefPath)
            );
        }

        [Test]
        public async Task OpenInRab_WhenRabSettingsXmlIsCorrupt_RecreatesSettingsFile()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);
            await service.PrepareAsync();

            Directory.CreateDirectory(Path.GetDirectoryName(service.GetRabSettingsFilePath()));
            RobustFile.WriteAllText(service.GetRabSettingsFilePath(), "<settings><apps>");

            Assert.DoesNotThrow(() => service.OpenInRab());

            var settingsDocument = XDocument.Load(service.GetRabSettingsFilePath());

            Assert.That(service.DetachedFileName, Is.EqualTo("cmd.exe"));
            Assert.That(
                settingsDocument.Root?.Element("apps")?.Elements("app").Count(),
                Is.EqualTo(1)
            );
            Assert.That(
                settingsDocument.Root?.Element("apps")?.Element("app")?.Element("filename")?.Value,
                Is.EqualTo(service.GetStatus().AppDefPath)
            );
        }

        [Test]
        public async Task OpenInRab_DoesNotWriteToProgress()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);
            await service.PrepareAsync();

            service.Progress.Messages.Clear();
            service.OpenInRab();

            Assert.That(service.Progress.Messages, Is.Empty);
        }

        [Test]
        public void GetSizeEstimates_UsesBookFolderSizesAndSharedLimits()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var firstBookFolder = Path.Combine(tempFolder.Path, "book-1");
            var secondBookFolder = Path.Combine(tempFolder.Path, "book-2");
            Directory.CreateDirectory(firstBookFolder);
            Directory.CreateDirectory(secondBookFolder);
            RobustFile.WriteAllBytes(Path.Combine(firstBookFolder, "a.bin"), new byte[1500]);
            Directory.CreateDirectory(Path.Combine(secondBookFolder, "images"));
            RobustFile.WriteAllBytes(
                Path.Combine(secondBookFolder, "images", "b.bin"),
                new byte[2500]
            );

            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = firstBookFolder,
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
                new RabBookPublishInfo
                {
                    BookId = "book-2",
                    FolderPath = secondBookFolder,
                    Title = "Book Two",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-2.bloompub"),
                },
            };

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);

            var estimates = service.GetSizeEstimates();

            Assert.That(
                estimates.EstimatedAppOverheadBytes,
                Is.EqualTo(RabProjectService.kEstimatedAppOverheadBytes)
            );
            Assert.That(estimates.MaxAppSizeBytes, Is.EqualTo(RabProjectService.kMaxAppSizeBytes));
            Assert.That(estimates.Books, Has.Length.EqualTo(2));
            Assert.That(
                estimates.Books.Single(book => book.BookId == "book-1").SizeBytes,
                Is.EqualTo(1500)
            );
            Assert.That(
                estimates.Books.Single(book => book.BookId == "book-2").SizeBytes,
                Is.EqualTo(2500)
            );
            Assert.That(estimates.Books.Single(book => book.BookId == "book-1").IsActual, Is.False);
            Assert.That(estimates.Books.Single(book => book.BookId == "book-2").IsActual, Is.False);
        }

        [Test]
        public void GetSizeEstimates_UsesBloomPubSizeWhenAvailable()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var firstBookFolder = Path.Combine(tempFolder.Path, "book-1");
            var secondBookFolder = Path.Combine(tempFolder.Path, "book-2");
            Directory.CreateDirectory(firstBookFolder);
            Directory.CreateDirectory(secondBookFolder);
            // Folder content sizes (should be ignored for book-1 when bloompub exists)
            RobustFile.WriteAllBytes(Path.Combine(firstBookFolder, "a.bin"), new byte[1500]);
            Directory.CreateDirectory(Path.Combine(secondBookFolder, "images"));
            RobustFile.WriteAllBytes(
                Path.Combine(secondBookFolder, "images", "b.bin"),
                new byte[2500]
            );

            // Create a bloompub file only for book-1
            Directory.CreateDirectory(paths.BloomPubRoot);
            RobustFile.WriteAllBytes(
                Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                new byte[4000]
            );

            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = firstBookFolder,
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
                new RabBookPublishInfo
                {
                    BookId = "book-2",
                    FolderPath = secondBookFolder,
                    Title = "Book Two",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-2.bloompub"),
                },
            };

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);

            var estimates = service.GetSizeEstimates();

            Assert.That(estimates.Books, Has.Length.EqualTo(2));
            // book-1 has a bloompub: use its size and mark as actual
            Assert.That(
                estimates.Books.Single(book => book.BookId == "book-1").SizeBytes,
                Is.EqualTo(4000)
            );
            Assert.That(estimates.Books.Single(book => book.BookId == "book-1").IsActual, Is.True);
            // book-2 has no bloompub: fall back to folder size and mark as estimate
            Assert.That(
                estimates.Books.Single(book => book.BookId == "book-2").SizeBytes,
                Is.EqualTo(2500)
            );
            Assert.That(estimates.Books.Single(book => book.BookId == "book-2").IsActual, Is.False);
        }

        [Test]
        public void GetStatus_ReportsIncompletePrepareSteps_WhenOnlyInstallerIsAvailable()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var service = new TestRabProjectService(
                paths,
                "Sample App",
                new List<RabBookPublishInfo>()
            )
            {
                IsRabInstalledForPrepareResult = false,
            };

            var status = service.GetStatus();

            AssertPrepareStep(status, "installer-available", true);
            AssertPrepareStep(status, "rab-installed", false);
            AssertPrepareStep(status, "build-tools-installed", false);
            AssertPrepareStep(status, "publisher-identity-created", false);
            AssertPrepareStep(status, "bloom-app-data-created", false);
            Assert.That(
                status
                    .PrepareSteps.Single(step => step.Id == "publisher-identity-created")
                    .IncompleteTooltip,
                Does.StartWith("Create a keystore that is used to sign any app you create")
            );
            Assert.That(
                status
                    .PrepareSteps.Single(step => step.Id == "installer-available")
                    .CompleteTooltip,
                Does.Contain("Reading-App-Builder-For-Bloom-6-4-Setup.exe")
            );
            Assert.That(
                status.PrepareSteps.Single(step => step.Id == "rab-installed").IncompleteTooltip,
                Is.EqualTo(
                    "Run the Reading App Builder installer to install the app on this computer."
                )
            );
            Assert.That(
                status
                    .PrepareSteps.Single(step => step.Id == "build-tools-installed")
                    .CompleteTooltip,
                Is.EqualTo("The Android SDK and JDK build tools are installed.").IgnoreCase
            );
        }

        [Test]
        public async Task GetStatus_ReportsCompletedPrepareSteps_AfterPrepareAsync()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);

            await service.PrepareAsync();

            var status = service.GetStatus();

            Assert.That(status.PrepareSteps.All(step => step.Complete), Is.True);
            Assert.That(
                status
                    .PrepareSteps.Single(step => step.Id == "publisher-identity-created")
                    .CompleteTooltip,
                Does.StartWith(
                    "Your keystore is used to sign apps, and you'll need it to publish new versions of your app."
                )
            );
            Assert.That(
                status
                    .PrepareSteps.Single(step => step.Id == "publisher-identity-created")
                    .CompleteTooltip,
                Does.Contain(paths.SharedKeystorePath)
            );
            Assert.That(
                status
                    .PrepareSteps.Single(step => step.Id == "bloom-app-data-created")
                    .CompleteTooltip,
                Is.EqualTo(
                    "A Reading App Builder project already exists for this Bloom collection."
                )
            );
        }

        [Test]
        public async Task PrepareAsync_InstallsRabSilently_AndContinues_WhenRegistryInstallIsMissingAndInstallerExists()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var installerPath = Path.Combine(
                tempFolder.Path,
                "Reading-App-Builder-For-Bloom-6-4-Setup.exe"
            );
            RobustFile.WriteAllText(installerPath, "installer");
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks)
            {
                IsRabInstalledForPrepareResult = false,
                RabSetupInstallerPathToReturn = installerPath,
            };

            await service.PrepareAsync();

            Assert.That(service.InstalledRabFromSetupPaths, Is.EqualTo(new[] { installerPath }));
            Assert.That(service.Commands, Is.Not.Empty);
            Assert.That(service.Commands[0], Does.StartWith("-install-sdks-if-needed "));
            Assert.That(
                service.Progress.Messages.Select(message => message.Item1),
                Does.Contain(
                    "Reading App Builder is not installed at the registry install path. Installing it now..."
                )
            );
            Assert.That(
                service.Progress.Messages.Select(message => message.Item1),
                Does.Contain("Reading App Builder installation complete.")
            );
        }

        [Test]
        public async Task PrepareAsync_DoesNotFail_WhenInstallerLaunchIsCanceledByUser()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var installerPath = Path.Combine(
                tempFolder.Path,
                "Reading-App-Builder-For-Bloom-6-4-Setup.exe"
            );
            RobustFile.WriteAllText(installerPath, "installer");

            var service = new TestRabProjectService(
                paths,
                "Sample App",
                new List<RabBookPublishInfo>()
            )
            {
                IsRabInstalledForPrepareResult = false,
                RabSetupInstallerPathToReturn = installerPath,
                LaunchExternalTargetException = new Win32Exception(
                    1223,
                    "The operation was canceled by the user."
                ),
            };

            Assert.DoesNotThrowAsync(async () => await service.PrepareAsync());

            Assert.That(service.Commands, Is.Empty);
            Assert.That(service.InstalledRabFromSetupPaths, Is.EqualTo(new[] { installerPath }));
            Assert.That(
                service.Progress.Messages.Select(message => message.Item1),
                Does.Contain(
                    "Reading App Builder installer did not start. Windows canceled the shell launch before the installer process started (error 1223: The operation was canceled by the user.). Bloom did not cancel it."
                )
            );
        }

        [Test]
        public void PrepareRabInstallerForLaunch_CopiesInstallerToStagingFolder_AndRemovesZoneIdentifier()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var installerPath = Path.Combine(
                tempFolder.Path,
                "Reading-App-Builder-For-Bloom-6-4-Setup.exe"
            );
            RobustFile.WriteAllText(installerPath, "installer");
            RobustFile.WriteAllText(
                installerPath + ":Zone.Identifier",
                "[ZoneTransfer]\r\nZoneId=3"
            );

            var stagingFolder = Path.Combine(tempFolder.Path, "installer-staging");
            var service = new TestRabProjectService(
                paths,
                "Sample App",
                new List<RabBookPublishInfo>()
            )
            {
                RabInstallerStagingDirectory = stagingFolder,
            };

            var stagedPath = service.CallPrepareRabInstallerForLaunch(installerPath);

            Assert.That(
                stagedPath,
                Is.EqualTo(Path.Combine(stagingFolder, Path.GetFileName(installerPath)))
            );
            Assert.That(RobustFile.ReadAllText(stagedPath), Is.EqualTo("installer"));
            Assert.That(
                () => RobustFile.ReadAllText(stagedPath + ":Zone.Identifier"),
                Throws.InstanceOf<FileNotFoundException>()
            );
        }

        [Test]
        public void BuildRabInstallerArguments_UsesSilentArguments()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var installDir = Path.Combine(
                tempFolder.Path,
                "Program Files",
                "SIL",
                "Reading App Builder for Bloom"
            );
            var service = new RegistryAwareRabProjectService(installDir, null, null);

            var arguments = service.BuildRabInstallerArguments(@"C:\temp\rab-install.log");

            Assert.That(arguments, Does.Contain("/VERYSILENT"));
            Assert.That(arguments, Does.Contain("/SUPPRESSMSGBOXES"));
            Assert.That(arguments, Does.Contain("/NORESTART"));
            Assert.That(arguments, Does.Contain("/SP-"));
            Assert.That(arguments, Does.Contain("/LANG=en"));
            Assert.That(arguments, Does.Contain("/LOG=\"C:\\temp\\rab-install.log\""));
            Assert.That(arguments, Does.Contain($"/DIR=\"{installDir}\""));
        }

        [Test]
        public void ShouldLogRabInstallerDownloadProgress_OnlyEveryFiveSeconds()
        {
            var start = new DateTime(2026, 4, 11, 10, 0, 0, DateTimeKind.Utc);

            Assert.That(
                RabProjectService.ShouldLogRabInstallerDownloadProgress(start, null),
                Is.True
            );
            Assert.That(
                RabProjectService.ShouldLogRabInstallerDownloadProgress(start.AddSeconds(4), start),
                Is.False
            );
            Assert.That(
                RabProjectService.ShouldLogRabInstallerDownloadProgress(start.AddSeconds(5), start),
                Is.True
            );
        }

        [Test]
        public void FormatRabInstallerDownloadProgressMessage_UsesHumanReadableSizes()
        {
            var message = RabProjectService.FormatRabInstallerDownloadProgressMessage(
                1572864,
                3145728
            );

            Assert.That(
                message,
                Is.EqualTo("Downloading Reading App Builder installer: 1.5 MB / 3.0 MB")
            );
        }

        [TestCase(@"C:\Users\tester\Downloads\Reading-App-Builder-For-Bloom-6-4-Setup.exe", true)]
        [TestCase("https://example.org/installer", false)]
        [TestCase(@"C:\Users\tester\Downloads\notes.txt", false)]
        public void IsExternalExecutablePath_MatchesExecutableFilesOnly(
            string pathOrUrl,
            bool expectedMatch
        )
        {
            Assert.That(
                RabProjectService.IsExternalExecutablePath(pathOrUrl),
                Is.EqualTo(expectedMatch)
            );
        }

        [TestCase("Reading-App-Builder-14.0-Setup.exe", true)]
        [TestCase("Reading-App-Builder-For-Bloom-6-4-Setup.exe", true)]
        [TestCase("Reading-App-Builder-14.0-Bloom.exe", false)]
        [TestCase("Reading-App-Builder-13.0-Setup.exe", false)]
        public void IsRabSetupInstallerFileName_MatchesExpectedVariants(
            string fileName,
            bool expectedMatch
        )
        {
            Assert.That(
                RabProjectService.IsRabSetupInstallerFileName(fileName),
                Is.EqualTo(expectedMatch)
            );
        }

        [Test]
        public async Task PrepareAsync_DownloadsInstaller_WhenRegistryInstallAndInstallerAreMissing()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var downloadedInstallerPath = Path.Combine(
                tempFolder.Path,
                "Reading-App-Builder-For-Bloom-6-4-Setup.exe"
            );

            var service = new TestRabProjectService(
                paths,
                "Sample App",
                new List<RabBookPublishInfo>()
            )
            {
                IsRabInstalledForPrepareResult = false,
                RabSetupInstallerPathToReturn = null,
                RabSetupInstallerDownloadPathToReturn = downloadedInstallerPath,
            };

            await service.PrepareAsync();

            Assert.That(
                service.DownloadedRabSetupInstallerPaths,
                Is.EqualTo(new[] { downloadedInstallerPath })
            );
            Assert.That(
                service.InstalledRabFromSetupPaths,
                Is.EqualTo(new[] { downloadedInstallerPath })
            );
            Assert.That(service.Commands, Is.Not.Empty);
            Assert.That(service.ExternalTargetsStarted, Is.Empty);
            Assert.That(
                service.Progress.Messages.Select(message => message.Item1),
                Does.Contain(
                    "Reading App Builder is not installed at the registry install path. Downloading it now..."
                )
            );
            Assert.That(
                service.Progress.Messages.Select(message => message.Item1),
                Does.Contain("Reading App Builder installation complete.")
            );
        }

        [Test]
        public async Task GetStatus_ShowsExistingProjectAsNotCurrentWhenRabIsUninstalled()
        {
            using var tempFolder = new TemporaryFolder("RabAppProjectTests");
            var paths = new RabWorkspacePaths(tempFolder.Path);
            var trackedBooks = new List<RabBookPublishInfo>
            {
                new RabBookPublishInfo
                {
                    BookId = "book-1",
                    FolderPath = Path.Combine(tempFolder.Path, "book-1"),
                    Title = "Book One",
                    BloomPubPath = Path.Combine(paths.BloomPubRoot, "book-1.bloompub"),
                },
            };
            Directory.CreateDirectory(trackedBooks[0].FolderPath);

            var service = new TestRabProjectService(paths, "Sample App", trackedBooks);
            await service.PrepareAsync();
            await service.BuildAsync();
            service.IsRabInstalledForPrepareResult = false;

            var status = service.GetStatus();

            Assert.That(status.ProjectExists, Is.True);
            Assert.That(status.ApkExists, Is.True);
            Assert.That(status.RabInstalled, Is.False);
        }

        private static void AssertPrepareStep(
            RabProjectStatus status,
            string stepId,
            bool expectedComplete
        )
        {
            Assert.That(
                status.PrepareSteps.Single(step => step.Id == stepId).Complete,
                Is.EqualTo(expectedComplete)
            );
        }

        private static void RewriteBloomPubThumbnail(string bloomPubPath, string thumbnailFileName)
        {
            using var archive = ZipFile.Open(bloomPubPath, ZipArchiveMode.Update);
            foreach (
                var existingThumbnail in archive
                    .Entries.Where(entry =>
                        entry.FullName == "thumbnail.png" || entry.FullName == "thumbnail.jpg"
                    )
                    .ToList()
            )
            {
                existingThumbnail.Delete();
            }

            using var writer = new StreamWriter(archive.CreateEntry(thumbnailFileName).Open());
            writer.Write("thumbnail");
        }

        private class TestRabProjectService : RabProjectService
        {
            private readonly RabWorkspacePaths _paths;
            private readonly string _appName;
            private readonly List<RabBookPublishInfo> _trackedBooks;
            private readonly ProgressSpy _progressSpy;

            public TestRabProjectService(
                RabWorkspacePaths paths,
                string appName,
                List<RabBookPublishInfo> trackedBooks
            )
                : this(paths, appName, trackedBooks, new ProgressSpy()) { }

            private TestRabProjectService(
                RabWorkspacePaths paths,
                string appName,
                List<RabBookPublishInfo> trackedBooks,
                ProgressSpy progressSpy
            )
                : base(null, null, null, null, null, progressSpy)
            {
                _paths = paths;
                _appName = appName;
                _trackedBooks = trackedBooks;
                _progressSpy = progressSpy;
                RabJdkInstallFolder = Path.Combine(paths.RabRoot, "toolchain", "jdk");
                RabAndroidSdkInstallFolder = Path.Combine(
                    paths.RabRoot,
                    "toolchain",
                    "android-sdk"
                );
                RabAppDataFolder = Path.Combine(paths.RabRoot, "toolchain", "appdata");
            }

            public List<string> Commands { get; } = new List<string>();
            public List<string> InstalledRabFromSetupPaths { get; } = new List<string>();
            public List<string> DownloadedRabSetupInstallerPaths { get; } = new List<string>();
            public string DetachedFileName { get; private set; }
            public string DetachedArguments { get; private set; }
            public string DetachedWorkingDirectory { get; private set; }
            public IReadOnlyDictionary<string, string> DetachedEnvironmentVariables
            {
                get;
                private set;
            }
            public List<string> ExternalTargetsStarted { get; } = new List<string>();
            public ProgressSpy Progress => _progressSpy;
            public bool IsRabInstalledForPrepareResult { get; set; } = true;
            public string RabLauncherPathToReturn { get; set; } =
                @"C:\Program Files (x86)\SIL\Reading App Builder\rab.bat";
            public string RabSetupInstallerPathToReturn { get; set; } =
                @"C:\Users\tester\Downloads\Reading-App-Builder-For-Bloom-6-4-Setup.exe";
            public string RabSetupInstallerDownloadPathToReturn { get; set; }
            public string UserDownloadsDirectoryToReturn { get; set; } =
                @"C:\Users\tester\Downloads";
            public string AdbPathToReturn { get; set; } =
                @"C:\Users\tester\AppData\Local\SIL\Bloom\ReadingAppBuilder\android-sdk\platform-tools\adb.exe";
            public string RabJdkInstallFolder { get; set; }
            public string RabAndroidSdkInstallFolder { get; set; }
            public string RabAppDataFolder { get; set; }
            public string RabInstallerStagingDirectory { get; set; }
            public bool TryBringRunningRabToFrontResult { get; set; }
            public int TryBringRunningRabToFrontCallCount { get; private set; }
            public Exception LaunchExternalTargetException { get; set; }
            public bool? JavaExistsBeforeInstallSdksCommand { get; private set; }
            public bool? TzdbExistsBeforeInstallSdksCommand { get; private set; }
            public int BloomPubWriteCount { get; private set; }
            public Queue<(int ExitCode, string Output)> InstallApkResults { get; } = new();
            public List<string> UninstallCommands { get; } = new List<string>();
            public List<string> RunProcessCommands { get; } = new List<string>();
            public int InstallCommandCount { get; private set; }
            public RabAdbConnectedDevice ConnectedDeviceToReturn { get; set; } =
                new RabAdbConnectedDevice
                {
                    Serial = "38300DLJH007PN",
                    Model = "Pixel_8",
                    Device = "shiba",
                    Product = "shiba",
                };

            internal override RabWorkspacePaths GetPaths()
            {
                return _paths;
            }

            internal override string GetAppName()
            {
                return _appName;
            }

            internal override string GetPackageName()
            {
                return MakeDefaultPackageName("stories", null);
            }

            private string GetAppSlug()
            {
                return MakeProjectSlug(_appName);
            }

            internal override IEnumerable<RabTrackedBookInfo> GetCollectionBooksForSizeEstimates()
            {
                return _trackedBooks.Select(book => new RabTrackedBookInfo
                {
                    BookId = book.BookId,
                    FolderPath = book.FolderPath,
                    Title = book.Title,
                });
            }

            internal override List<RabBookPublishInfo> ExportPrepareBooks(RabWorkspacePaths paths)
            {
                return ExportBooks(paths, _trackedBooks);
            }

            internal override List<RabBookPublishInfo> ExportTrackedBooks(
                RabWorkspacePaths paths,
                RabPrepareState state
            )
            {
                Assert.That(state.Books, Is.Not.Null);
                Assert.That(
                    state.Books.Select(book => book.FolderPath),
                    Is.EqualTo(_trackedBooks.Select(book => book.FolderPath))
                );
                return ExportBooks(paths, _trackedBooks, state.Books);
            }

            internal override RabProjectSupportFiles EnsureProjectSupportFiles(
                RabWorkspacePaths paths
            )
            {
                Directory.CreateDirectory(paths.ProjectAssetsRoot);
                Directory.CreateDirectory(paths.LauncherIconRoot);
                RobustFile.WriteAllText(
                    paths.AboutTextPath,
                    "Sample App\r\n\r\nCreated with Bloom."
                );

                var launcherIcons = new[] { 36, 48, 72 }
                    .Select(size =>
                    {
                        var iconPath = Path.Combine(paths.LauncherIconRoot, $"icon-{size}.png");
                        RobustFile.WriteAllBytes(iconPath, Encoding.UTF8.GetBytes($"png-{size}"));
                        return iconPath;
                    })
                    .ToArray();

                return new RabProjectSupportFiles
                {
                    AboutTextPath = paths.AboutTextPath,
                    LauncherIconPaths = launcherIcons,
                };
            }

            internal override void EnsureKeystore(string keystorePath, string password)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(keystorePath));
                RobustFile.WriteAllText(keystorePath, password);
            }

            internal override void RunRabCommand(string rabArguments, string workingDirectory)
            {
                Commands.Add(rabArguments);
                var tokens = TokenizeArguments(rabArguments);

                if (tokens.Contains("-install-sdks-if-needed"))
                {
                    JavaExistsBeforeInstallSdksCommand = RobustFile.Exists(
                        GetRabJavaExecutablePath()
                    );
                    TzdbExistsBeforeInstallSdksCommand = RobustFile.Exists(
                        Path.Combine(GetRabJdkRootPath(), "lib", "tzdb.dat")
                    );
                    CreateBuildToolMarkers();
                    return;
                }

                Assert.That(tokens, Does.Contain("-a"));
                Assert.That(tokens, Does.Contain("-ic"));
                Assert.That(tokens, Does.Contain("-ks"));
                Assert.That(tokens, Does.Contain("-ksp"));
                Assert.That(tokens, Does.Contain("-ka"));
                Assert.That(tokens, Does.Contain("-kap"));

                if (tokens.Contains("-new"))
                {
                    CreateAppDef(tokens);
                    ImportBooksIntoProject(
                        tokens,
                        Path.Combine(_paths.RabRoot, GetAppSlug() + ".appDef")
                    );
                    return;
                }

                if (tokens.Contains("-load"))
                    ImportBooksIntoProject(tokens, GetTokenValue(tokens, "-load"));

                if (tokens.Contains("-load") && tokens.Contains("-build"))
                {
                    EmitSimulatedBuildOutput(rabArguments);
                    CreateApk(tokens);
                }
            }

            internal override string GetUserDownloadsDirectory()
            {
                return UserDownloadsDirectoryToReturn;
            }

            private void EmitSimulatedBuildOutput(string rabArguments)
            {
                foreach (
                    var line in new[]
                    {
                        "*** Building Android app ***",
                        "*** Setting paths ***",
                        "*** JDK ***",
                        "*** Android SDK ***",
                        "*** Compiling Android APK ***",
                        "> Task :mergeReleaseNativeLibs",
                        "> Task :generateReleaseResources",
                        "> Task :mergeReleaseResources",
                        "> Task :compressReleaseAssets",
                        "> Task :processReleaseResources",
                        "> Task :compileReleaseJavaWithJavac",
                        "> Task :minifyReleaseWithR8",
                        "> Task :packageRelease",
                        "> Task :assembleRelease",
                        "BUILD SUCCESSFUL in 1m 38s",
                    }
                )
                {
                    ReportProcessOutputLine(line, commandArguments: rabArguments);
                }
            }

            internal override void StartDetachedProcess(
                string fileName,
                string arguments,
                string workingDirectory,
                IReadOnlyDictionary<string, string> environmentVariables = null
            )
            {
                DetachedFileName = fileName;
                DetachedArguments = arguments;
                DetachedWorkingDirectory = workingDirectory;
                DetachedEnvironmentVariables = environmentVariables;
            }

            internal override string FindRabLauncherPath()
            {
                return RabLauncherPathToReturn;
            }

            internal override bool IsRabInstalledForPrepare()
            {
                return IsRabInstalledForPrepareResult;
            }

            internal override string FindRabSetupInstallerPath()
            {
                return RabSetupInstallerPathToReturn;
            }

            internal override string DownloadRabSetupInstaller()
            {
                DownloadedRabSetupInstallerPaths.Add(RabSetupInstallerDownloadPathToReturn);

                if (!string.IsNullOrWhiteSpace(RabSetupInstallerDownloadPathToReturn))
                {
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(RabSetupInstallerDownloadPathToReturn)
                    );
                    RobustFile.WriteAllText(
                        RabSetupInstallerDownloadPathToReturn,
                        "downloaded installer"
                    );
                }

                return RabSetupInstallerDownloadPathToReturn;
            }

            public string CallPrepareRabInstallerForLaunch(string installerPath)
            {
                return PrepareRabInstallerForLaunch(installerPath);
            }

            internal override void LaunchExternalTarget(string pathOrUrl)
            {
                ExternalTargetsStarted.Add(pathOrUrl);
                if (LaunchExternalTargetException != null)
                    throw LaunchExternalTargetException;
            }

            internal override void InstallRabFromSetup(string installerPath)
            {
                InstalledRabFromSetupPaths.Add(installerPath);
                if (LaunchExternalTargetException != null)
                    throw LaunchExternalTargetException;

                IsRabInstalledForPrepareResult = true;
            }

            internal override string GetRabInstallerStagingDirectory()
            {
                return RabInstallerStagingDirectory ?? base.GetRabInstallerStagingDirectory();
            }

            internal override string GetRabJdkInstallFolder()
            {
                return RabJdkInstallFolder;
            }

            internal override string GetRabAndroidSdkInstallFolder()
            {
                return RabAndroidSdkInstallFolder;
            }

            internal override string GetRabAppDataFolder()
            {
                return RabAppDataFolder;
            }

            internal override bool TryBringRunningRabToFront()
            {
                TryBringRunningRabToFrontCallCount++;
                return TryBringRunningRabToFrontResult;
            }

            internal override string FindAdbPath()
            {
                return AdbPathToReturn;
            }

            internal override RabAdbConnectedDevice GetSingleConnectedDevice(string adbPath)
            {
                return ConnectedDeviceToReturn;
            }

            internal override (int ExitCode, string Output) InstallApkOnDevice(
                string adbPath,
                string deviceSerial,
                string apkPath,
                string workingDirectory
            )
            {
                InstallCommandCount++;
                if (InstallApkResults.Count > 0)
                    return InstallApkResults.Dequeue();

                return (0, string.Empty);
            }

            internal override void UninstallAppFromDevice(
                string adbPath,
                string deviceSerial,
                string packageName,
                string workingDirectory
            )
            {
                UninstallCommands.Add($"-s \"{deviceSerial}\" uninstall \"{packageName}\"");
            }

            internal override void RunProcess(
                string fileName,
                string arguments,
                string workingDirectory,
                IReadOnlyDictionary<string, string> environmentVariables = null
            )
            {
                RunProcessCommands.Add(arguments);
            }

            private void CreateBuildToolMarkers()
            {
                var javaPath = Path.Combine(
                    RabJdkInstallFolder,
                    "zulu17.42.19-ca-jdk17.0.7-win_x64",
                    "bin",
                    "java.exe"
                );
                Directory.CreateDirectory(Path.GetDirectoryName(javaPath));
                RobustFile.WriteAllText(javaPath, "java");

                var tzdbPath = Path.Combine(
                    RabJdkInstallFolder,
                    "zulu17.42.19-ca-jdk17.0.7-win_x64",
                    "lib",
                    "tzdb.dat"
                );
                Directory.CreateDirectory(Path.GetDirectoryName(tzdbPath));
                RobustFile.WriteAllText(tzdbPath, "tzdb");

                var adbPath = Path.Combine(RabAndroidSdkInstallFolder, "platform-tools", "adb.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(adbPath));
                RobustFile.WriteAllText(adbPath, "adb");
            }

            private List<RabBookPublishInfo> ExportBooks(
                RabWorkspacePaths paths,
                IEnumerable<RabBookPublishInfo> books,
                IEnumerable<RabBookPublishInfo> existingBooks = null
            )
            {
                Directory.CreateDirectory(paths.BloomPubRoot);
                var existingByFolder = (
                    existingBooks ?? Enumerable.Empty<RabBookPublishInfo>()
                ).ToDictionary(book => book.FolderPath, StringComparer.OrdinalIgnoreCase);
                var booksToExport = books.ToList();
                var bloomPubPathsToKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var book in booksToExport)
                {
                    var bloomPubPath = existingByFolder.TryGetValue(
                        book.FolderPath,
                        out var existing
                    )
                        ? existing.BloomPubPath
                        : book.BloomPubPath;
                    bloomPubPathsToKeep.Add(bloomPubPath);
                }

                foreach (
                    var existingBloomPubPath in Directory.GetFiles(paths.BloomPubRoot, "*.bloompub")
                )
                {
                    if (bloomPubPathsToKeep.Contains(existingBloomPubPath))
                        continue;

                    RobustFile.Delete(existingBloomPubPath);
                }

                return booksToExport
                    .Select(book =>
                    {
                        var bloomPubPath = existingByFolder.TryGetValue(
                            book.FolderPath,
                            out var existing
                        )
                            ? existing.BloomPubPath
                            : book.BloomPubPath;
                        if (RobustFile.Exists(bloomPubPath))
                        {
                            Progress.MessageWithoutLocalizing(
                                $"Reusing existing BloomPUB for {book.Title}..."
                            );
                        }
                        else
                        {
                            WriteBloomPub(bloomPubPath, book);
                        }
                        return new RabBookPublishInfo
                        {
                            BookId = book.BookId,
                            FolderPath = book.FolderPath,
                            Title = book.Title,
                            BloomPubPath = bloomPubPath,
                            ThumbnailFileName = RabProjectService.GetBloomPubThumbnailFileName(
                                bloomPubPath
                            ),
                        };
                    })
                    .ToList();
            }

            public Dictionary<string, string> FontsCssByFolderPath { get; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            private void WriteBloomPub(string bloomPubPath, RabBookPublishInfo book)
            {
                BloomPubWriteCount++;
                if (RobustFile.Exists(bloomPubPath))
                    RobustFile.Delete(bloomPubPath);

                using var archive = ZipFile.Open(bloomPubPath, ZipArchiveMode.Create);
                using (var htmlWriter = new StreamWriter(archive.CreateEntry("index.htm").Open()))
                {
                    htmlWriter.Write($"<html><body>{book.Title}</body></html>");
                }

                using (
                    var thumbnailWriter = new StreamWriter(
                        archive.CreateEntry(book.ThumbnailFileName ?? "thumbnail.png").Open()
                    )
                )
                {
                    thumbnailWriter.Write("thumbnail");
                }

                if (!FontsCssByFolderPath.TryGetValue(book.FolderPath, out var fontsCss))
                    return;

                using (var cssWriter = new StreamWriter(archive.CreateEntry("fonts.css").Open()))
                {
                    cssWriter.Write(fontsCss);
                }

                foreach (Match match in Regex.Matches(fontsCss, @"url\('(?<file>[^']+)'\)"))
                {
                    var fileName = match.Groups["file"].Value;
                    if (string.IsNullOrWhiteSpace(fileName))
                        continue;

                    using var fontWriter = new StreamWriter(archive.CreateEntry(fileName).Open());
                    fontWriter.Write("font-data");
                }
            }

            private void CreateAppDef(IReadOnlyList<string> tokens)
            {
                var appName = GetTokenValue(tokens, "-n");
                var keystorePath = GetTokenValue(tokens, "-ks");
                var keystorePassword = GetTokenValue(tokens, "-ksp");
                var alias = GetTokenValue(tokens, "-ka");
                var aliasPassword = GetTokenValue(tokens, "-kap");
                var appDefPath = Path.Combine(_paths.RabRoot, GetAppSlug() + ".appDef");

                var project = new XDocument(
                    new XElement(
                        "app-definition",
                        new XAttribute("type", "RAB"),
                        new XAttribute("program-version", "13.4"),
                        new XElement("project-name", GetAppSlug()),
                        new XElement("app-name", new XAttribute("lang", "default"), appName),
                        new XElement("package", GetTokenValue(tokens, "-p")),
                        new XElement(
                            "signing",
                            new XElement("keystore", keystorePath),
                            new XElement("keystore-password", keystorePassword),
                            new XElement("alias", alias),
                            new XElement("alias-password", aliasPassword)
                        ),
                        new XElement(
                            "books",
                            new XAttribute("id", "C01"),
                            new XElement("book-collection-name", "Main Collection")
                        )
                    )
                );

                project.Save(appDefPath);
            }

            private void CreateApk(IReadOnlyList<string> tokens)
            {
                var appDefPath = GetTokenValue(tokens, "-load");
                Assert.That(appDefPath, Is.Not.Null);
                Assert.That(RobustFile.Exists(appDefPath), Is.True);
                var project = RabAppProject.Load(appDefPath);

                Directory.CreateDirectory(_paths.ApkRoot);
                var apkPath = Path.Combine(_paths.ApkRoot, GetAppSlug() + ".apk");
                if (RobustFile.Exists(apkPath))
                    RobustFile.Delete(apkPath);

                using (var archive = ZipFile.Open(apkPath, ZipArchiveMode.Create))
                {
                    var manifest = archive.CreateEntry("AndroidManifest.xml");
                    using (var writer = new StreamWriter(manifest.Open()))
                    {
                        writer.Write("<manifest package=\"" + project.PackageName + "\" />");
                    }

                    foreach (var book in _trackedBooks)
                    {
                        var entry = archive.CreateEntry(
                            $"assets/{Path.GetFileName(book.BloomPubPath)}"
                        );
                        using var sourceStream = File.OpenRead(book.BloomPubPath);
                        using var destinationStream = entry.Open();
                        sourceStream.CopyTo(destinationStream);
                    }

                    var projectDataRoot = Path.Combine(
                        Path.GetDirectoryName(appDefPath),
                        Path.GetFileNameWithoutExtension(appDefPath) + "_data"
                    );
                    var generatedBooksRoot = Path.Combine(projectDataRoot, "books", "C01");
                    if (Directory.Exists(generatedBooksRoot))
                    {
                        foreach (
                            var generatedBookDirectory in Directory.GetDirectories(
                                generatedBooksRoot
                            )
                        )
                        {
                            var entry = archive.CreateEntry(
                                $"assets/{Path.GetFileName(generatedBookDirectory)}/meta.json"
                            );
                            using var writer = new StreamWriter(entry.Open());
                            writer.Write(Path.GetFileName(generatedBookDirectory));
                        }
                    }
                }
            }

            private void ImportBooksIntoProject(IReadOnlyList<string> tokens, string appDefPath)
            {
                var bloomPubPaths = GetTokenValues(tokens, "-b");
                if (bloomPubPaths.Count == 0)
                    return;

                var document = XDocument.Load(appDefPath);
                var booksElement = document.Root?.Element("books");
                Assert.That(booksElement, Is.Not.Null);
                var nextBookIndex = booksElement.Elements("book").Count() + 1;

                foreach (var bloomPubPath in bloomPubPaths)
                {
                    var trackedBook = _trackedBooks.First(book =>
                        string.Equals(
                            book.BloomPubPath,
                            bloomPubPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
                    var bookElementId = $"B{nextBookIndex:000}";
                    booksElement.Add(
                        new XElement(
                            "book",
                            new XAttribute("id", bookElementId),
                            new XAttribute("type", "bloom-player"),
                            new XAttribute("bloom", "true"),
                            new XAttribute("format", "html"),
                            new XElement("name", trackedBook.Title),
                            new XElement("font-choice", new XAttribute("type", "book-collection")),
                            new XElement("filename", "index.htm"),
                            new XElement("source", bloomPubPath),
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
                    CreateGeneratedBookFolder(appDefPath, bookElementId, trackedBook.Title);
                    nextBookIndex++;
                }

                document.Save(appDefPath);
            }

            private static void CreateGeneratedBookFolder(
                string appDefPath,
                string bookElementId,
                string title
            )
            {
                var metaJsonPath = Path.Combine(
                    Path.GetDirectoryName(appDefPath),
                    Path.GetFileNameWithoutExtension(appDefPath) + "_data",
                    "books",
                    "C01",
                    bookElementId,
                    "bloom",
                    "meta.json"
                );
                Directory.CreateDirectory(Path.GetDirectoryName(metaJsonPath));
                RobustFile.WriteAllText(metaJsonPath, "{\"title\":\"" + title + "\"}");
            }

            private static List<string> TokenizeArguments(string arguments)
            {
                var tokens = new List<string>();
                var current = new StringBuilder();
                var inQuotes = false;

                foreach (var ch in arguments)
                {
                    if (ch == '"')
                    {
                        inQuotes = !inQuotes;
                        continue;
                    }

                    if (char.IsWhiteSpace(ch) && !inQuotes)
                    {
                        if (current.Length > 0)
                        {
                            tokens.Add(current.ToString());
                            current.Clear();
                        }

                        continue;
                    }

                    current.Append(ch);
                }

                if (current.Length > 0)
                    tokens.Add(current.ToString());

                return tokens;
            }

            private static string GetTokenValue(IReadOnlyList<string> tokens, string key)
            {
                var index = -1;
                for (var i = 0; i < tokens.Count; i++)
                {
                    if (tokens[i] == key)
                    {
                        index = i;
                        break;
                    }
                }
                Assert.That(index, Is.GreaterThanOrEqualTo(0), $"Expected argument {key}.");
                Assert.That(index + 1, Is.LessThan(tokens.Count), $"Expected value after {key}.");
                return tokens[index + 1];
            }

            private static List<string> GetTokenValues(IReadOnlyList<string> tokens, string key)
            {
                var values = new List<string>();

                for (var index = 0; index < tokens.Count - 1; index++)
                {
                    if (tokens[index] == key)
                        values.Add(tokens[index + 1]);
                }

                return values;
            }
        }

        private class LanguageAwareRabProjectService : RabProjectService
        {
            private readonly string _appName;

            public LanguageAwareRabProjectService(
                string appName,
                CollectionSettings collectionSettings
            )
                : base(null, null, null, collectionSettings, null, new ProgressSpy())
            {
                _appName = appName;
            }

            internal override string GetAppName()
            {
                return _appName;
            }

            public string GetPackageNameForTest()
            {
                return GetPackageName();
            }
        }

        private class RealPathRabProjectService : RabProjectService
        {
            public RealPathRabProjectService(TemporaryFolder tempFolder)
                : base(
                    new FakeCollectionModel(tempFolder, new CollectionSettings()),
                    null,
                    null,
                    new CollectionSettings(),
                    null,
                    new ProgressSpy()
                ) { }

            public RabWorkspacePaths ReadPaths()
            {
                return GetPaths();
            }
        }

        private class RegistryAwareRabProjectService : RabProjectService
        {
            private readonly string _defaultInstallDir;
            private readonly string _registryInstallDir;
            private readonly string _registryVersion;
            public string BundledIconRootToReturn { get; set; }

            public RegistryAwareRabProjectService(
                string defaultInstallDir,
                string registryInstallDir,
                string registryVersion
            )
                : base(null, null, null, null, null)
            {
                _defaultInstallDir = defaultInstallDir;
                _registryInstallDir = registryInstallDir;
                _registryVersion = registryVersion;
            }

            internal override string GetDefaultRabInstallDir()
            {
                return _defaultInstallDir;
            }

            internal override string GetRabRegistryValue(string valueName)
            {
                if (valueName == "InstallDir")
                    return _registryInstallDir;

                if (valueName == "Version")
                    return _registryVersion;

                return null;
            }

            internal override string GetBundledIconRoot()
            {
                return BundledIconRootToReturn;
            }
        }
    }
}
