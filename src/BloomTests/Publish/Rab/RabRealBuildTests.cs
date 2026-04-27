using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Bloom.Publish.BloomPub;
using Bloom.Publish.Rab;
using BloomTests.Book;
using Newtonsoft.Json;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Publish.Rab
{
    [TestFixture]
    [Category("SkipOnTeamCity")]
    [Category("RequiresReadingAppBuilder")]
    [NonParallelizable]
    public class RabRealBuildTests : BookTestsBase
    {
        private const string kRunManualRabBuildEnvVar = "BLOOM_RUN_RAB_MANUAL_TESTS";
        private const string kManualCollectionFolderName = "Kasɩm Books";
        private const string kManualBloomOwnedRabFolderName = "ReadingAppBuilder";

        private static string GetManualWorkRoot([CallerFilePath] string currentFilePath = "")
        {
            return Path.Combine(Path.GetDirectoryName(currentFilePath), "ManualWork");
        }

        private static string GetManualBloomOwnedRabRoot(string manualWorkRoot)
        {
            return Path.Combine(manualWorkRoot, "BloomAppData", kManualBloomOwnedRabFolderName);
        }

        private static string GetRepoBloomPubPath([CallerFilePath] string currentFilePath = "")
        {
            return Path.Combine(
                Path.GetDirectoryName(currentFilePath),
                "TestData",
                "Book4.bloompub"
            );
        }

        [Test]
        public async Task SetupAndBuildAsync_RealReadingAppBuilderBuild_CreatesValidApk()
        {
            // This is an opt-in verification pass against an installed copy of RAB, not a normal CI test.
            if (Environment.GetEnvironmentVariable(kRunManualRabBuildEnvVar) != "1")
            {
                Assert.Ignore(
                    $"Manual RAB verification test. Set {kRunManualRabBuildEnvVar}=1 to run it."
                );
            }

            var initialManualWorkRoot = GetManualWorkRoot();
            var paths = new RabWorkspacePaths(
                Path.Combine(initialManualWorkRoot, kManualCollectionFolderName),
                GetManualBloomOwnedRabRoot(initialManualWorkRoot)
            );
            var rabLauncherPath = RealRabProjectService.FindInstalledRabLauncherPath(paths);
            var rabKeytoolPath = RealRabProjectService.FindInstalledRabKeytoolPath(paths);

            Assert.That(
                !string.IsNullOrWhiteSpace(rabLauncherPath) && RobustFile.Exists(rabLauncherPath),
                Is.True,
                $"Install Reading App Builder before running this test. Expected launcher discovered by Bloom's RAB path logic."
            );
            Assert.That(
                !string.IsNullOrWhiteSpace(rabKeytoolPath) && RobustFile.Exists(rabKeytoolPath),
                Is.True,
                $"Reading App Builder runtime keytool is missing. Expected keytool discovered by Bloom's RAB path logic."
            );

            var sourceBloomPubPath = GetRepoBloomPubPath();

            Assert.That(
                RobustFile.Exists(sourceBloomPubPath),
                Is.True,
                $"The checked-in BloomPUB test input does not exist: {sourceBloomPubPath}"
            );

            var bookTitle = Path.GetFileNameWithoutExtension(sourceBloomPubPath);

            var manualWorkRoot = GetManualWorkRoot();

            if (Directory.Exists(manualWorkRoot))
                Directory.Delete(manualWorkRoot, true);

            Directory.CreateDirectory(manualWorkRoot);
            TestContext.Progress.WriteLine($"RAB manual work root: {manualWorkRoot}");

            var manualCollectionRoot = Path.Combine(manualWorkRoot, kManualCollectionFolderName);
            var manualBloomOwnedRabRoot = GetManualBloomOwnedRabRoot(manualWorkRoot);
            paths = new RabWorkspacePaths(manualCollectionRoot, manualBloomOwnedRabRoot);
            Directory.CreateDirectory(manualCollectionRoot);
            Directory.CreateDirectory(manualBloomOwnedRabRoot);
            Directory.CreateDirectory(paths.ProjectAssetsRoot);
            Directory.CreateDirectory(paths.LauncherIconRoot);
            RobustFile.WriteAllText(
                paths.AboutTextPath,
                "RAB Manual Test App\r\n\r\nCreated by the Bloom manual RAB verification test."
            );
            var iconPaths = CreateLauncherIcons(paths.LauncherIconRoot);

            var service = new RealRabProjectService(
                paths,
                sourceBloomPubPath,
                bookTitle,
                paths.AboutTextPath,
                iconPaths
            );

            await service.PrepareAsync();
            await service.BuildAsync();

            var status = service.GetStatus();
            Assert.That(status.ProjectExists, Is.True);
            Assert.That(status.ApkExists, Is.True);
            Assert.That(status.AppDefPath, Is.Not.Null);
            Assert.That(RobustFile.Exists(status.AppDefPath), Is.True);
            Assert.That(status.ApkPath, Is.Not.Null);
            Assert.That(RobustFile.Exists(status.ApkPath), Is.True);
            Assert.That(status.TrackedBookTitles, Is.EqualTo(new[] { bookTitle }));

            var prepareState = JsonConvert.DeserializeObject<RabPrepareState>(
                RobustFile.ReadAllText(paths.PrepareStatePath)
            );
            Assert.That(prepareState, Is.Not.Null);
            Assert.That(prepareState.AppDefPath, Is.EqualTo(status.AppDefPath));
            Assert.That(prepareState.Books, Has.Count.EqualTo(1));
            Assert.That(prepareState.Books[0].BloomPubPath, Is.Not.Null);
            Assert.That(RobustFile.Exists(prepareState.Books[0].BloomPubPath), Is.True);
            Assert.That(prepareState.Books[0].Title, Is.EqualTo(bookTitle));

            var project = RabAppProject.Load(status.AppDefPath);
            Assert.That(project.AppName, Is.EqualTo("RAB Manual Test App"));
            Assert.That(project.BookTitles, Is.EqualTo(new[] { bookTitle }));

            using (var archive = ZipFile.OpenRead(status.ApkPath))
            {
                Assert.That(
                    archive.Entries.Any(entry => entry.FullName == "AndroidManifest.xml"),
                    Is.True,
                    "The generated APK should contain AndroidManifest.xml."
                );
            }
        }

        private string[] CreateLauncherIcons(string launcherIconRoot)
        {
            var sizes = new[] { 36, 48, 72, 96, 144, 192, 512 };
            return sizes
                .Select(size =>
                {
                    var iconPath = Path.Combine(launcherIconRoot, $"manual-rab-icon-{size}.png");
                    MakeSamplePngImageWithMetadata(iconPath, size, size);
                    return iconPath;
                })
                .ToArray();
        }

        private class RealRabProjectService : RabProjectService
        {
            private readonly RabWorkspacePaths _paths;
            private readonly string _sourceBloomPubPath;
            private readonly string _bookTitle;
            private readonly string _aboutTextPath;
            private readonly string[] _iconPaths;

            public RealRabProjectService(
                RabWorkspacePaths paths,
                string sourceBloomPubPath,
                string bookTitle,
                string aboutTextPath,
                string[] iconPaths
            )
                : base(null, null, null, null, null)
            {
                _paths = paths;
                _sourceBloomPubPath = sourceBloomPubPath;
                _bookTitle = bookTitle;
                _aboutTextPath = aboutTextPath;
                _iconPaths = iconPaths;
            }

            internal static string FindInstalledRabLauncherPath(RabWorkspacePaths paths)
            {
                return new RealRabProjectService(
                    paths,
                    null,
                    null,
                    null,
                    null
                ).FindRabLauncherPath();
            }

            internal static string FindInstalledRabKeytoolPath(RabWorkspacePaths paths)
            {
                var rabLauncherPath = FindInstalledRabLauncherPath(paths);
                if (string.IsNullOrWhiteSpace(rabLauncherPath))
                    return null;

                return Path.Combine(
                    Path.GetDirectoryName(rabLauncherPath),
                    "runtime",
                    "bin",
                    "keytool.exe"
                );
            }

            internal override RabWorkspacePaths GetPaths()
            {
                return _paths;
            }

            internal override string GetAppName()
            {
                return "RAB Manual Test App";
            }

            internal override string GetPackageName()
            {
                return MakeDefaultPackageName("stories", null);
            }

            internal override List<RabBookPublishInfo> ExportPrepareBooks(RabWorkspacePaths paths)
            {
                return ExportBooks(paths, null);
            }

            internal override List<RabBookPublishInfo> ExportTrackedBooks(
                RabWorkspacePaths paths,
                RabPrepareState state
            )
            {
                return ExportBooks(paths, state.Books.FirstOrDefault()?.BloomPubPath);
            }

            internal override RabProjectSupportFiles EnsureProjectSupportFiles(
                RabWorkspacePaths paths
            )
            {
                return new RabProjectSupportFiles
                {
                    AboutTextPath = _aboutTextPath,
                    LauncherIconPaths = _iconPaths,
                };
            }

            private List<RabBookPublishInfo> ExportBooks(
                RabWorkspacePaths paths,
                string bloomPubPath
            )
            {
                Directory.CreateDirectory(paths.BloomPubRoot);
                var outputPath = string.IsNullOrWhiteSpace(bloomPubPath)
                    ? Path.Combine(
                        paths.BloomPubRoot,
                        "manual-rab-test" + BloomPubMaker.BloomPubExtensionWithDot
                    )
                    : bloomPubPath;

                if (
                    !string.Equals(
                        outputPath,
                        _sourceBloomPubPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    RobustFile.Copy(_sourceBloomPubPath, outputPath, true);

                return new List<RabBookPublishInfo>
                {
                    new RabBookPublishInfo
                    {
                        BookId = "manual-rab-test-book",
                        FolderPath = Path.GetDirectoryName(_sourceBloomPubPath),
                        Title = _bookTitle,
                        BloomPubPath = outputPath,
                    },
                };
            }
        }
    }
}
