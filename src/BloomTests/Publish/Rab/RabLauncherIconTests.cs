using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using Bloom.Publish.Rab;
using BloomTests.TeamCollection;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Publish.Rab
{
    /// <summary>
    /// Tests for generating the RAB launcher icons, in particular the BL-16467 case where the
    /// chosen icon image cannot be decoded. GDI+'s Image.FromFile throws a misleading
    /// OutOfMemoryException for an undecodable image, which used to abort the whole build.
    /// </summary>
    [TestFixture]
    public class RabLauncherIconTests
    {
        // The sizes EnsureLauncherIcons is expected to produce (see RabProjectService).
        private static readonly int[] kExpectedIconSizes = { 36, 48, 72, 96, 144, 192, 512 };

        /// <summary>
        /// Minimal RabProjectService that points the bundled-icon lookup at a test folder and
        /// exposes EnsureLauncherIcons plus the captured progress messages.
        /// </summary>
        private class TestableRabProjectService : RabProjectService
        {
            private readonly string _bundledIconRoot;
            private readonly ProgressSpy _progressSpy;

            public TestableRabProjectService(string bundledIconRoot, ProgressSpy progressSpy)
                : base(null, null, null, null, null, progressSpy)
            {
                _bundledIconRoot = bundledIconRoot;
                _progressSpy = progressSpy;
            }

            public ProgressSpy Progress => _progressSpy;

            internal override string GetBundledIconRoot()
            {
                return _bundledIconRoot;
            }

            public string[] RunEnsureLauncherIcons(RabWorkspacePaths paths, RabAppSettings settings)
            {
                return EnsureLauncherIcons(paths, settings);
            }
        }

        // Writes a valid square PNG of the given size and returns its path.
        private static string WriteValidPng(string path, int size)
        {
            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.CornflowerBlue);
                bitmap.Save(path, ImageFormat.Png);
            }
            return path;
        }

        // Writes a .png file whose bytes are not a decodable image (the BL-16467 trigger).
        private static string WriteUndecodableImage(string path)
        {
            var garbage = Enumerable.Range(0, 512).Select(i => (byte)(i % 256)).ToArray();
            RobustFile.WriteAllBytes(path, garbage);
            return path;
        }

        private static TestableRabProjectService MakeService(
            TemporaryFolder folder,
            out RabWorkspacePaths paths
        )
        {
            // A bundled-icon folder containing the default icon EnsureLauncherIcons falls back to.
            var bundledIconRoot = Path.Combine(folder.Path, "appbuilder-icons");
            Directory.CreateDirectory(bundledIconRoot);
            WriteValidPng(Path.Combine(bundledIconRoot, "bloom-app-icon-52.png"), 256);

            var collectionRoot = Path.Combine(folder.Path, "collection");
            Directory.CreateDirectory(collectionRoot);
            paths = new RabWorkspacePaths(collectionRoot);
            // EnsureLauncherIcons writes into LauncherIconRoot; the real flow creates it first.
            Directory.CreateDirectory(paths.LauncherIconRoot);

            return new TestableRabProjectService(bundledIconRoot, new ProgressSpy());
        }

        private static void AssertIconsAreValidAndCorrectlySized(string[] iconPaths)
        {
            Assert.That(
                iconPaths.Length,
                Is.EqualTo(kExpectedIconSizes.Length),
                "Should have produced one icon per requested size."
            );

            foreach (
                var (iconPath, expectedSize) in iconPaths.Zip(kExpectedIconSizes, (p, s) => (p, s))
            )
            {
                Assert.That(RobustFile.Exists(iconPath), $"Expected icon to exist: {iconPath}");
                // If the icon were not a valid image, Image.FromFile would itself throw the
                // misleading OutOfMemoryException; loading it proves we wrote a real PNG.
                using (var image = Image.FromFile(iconPath))
                {
                    Assert.That(image.Width, Is.EqualTo(expectedSize));
                    Assert.That(image.Height, Is.EqualTo(expectedSize));
                }
            }
        }

        [Test]
        public void EnsureLauncherIcons_UndecodableIcon_ThrowsOutOfMemoryFromGdiPlus_Sanity()
        {
            // This documents the BL-16467 root cause: GDI+ reports an undecodable image with a
            // misleading OutOfMemoryException, which is what aborted the original build.
            using (var folder = new TemporaryFolder("RabLauncherIcon_ReproSanity"))
            {
                var badIcon = WriteUndecodableImage(Path.Combine(folder.Path, "bad-icon.png"));

                // Sanity-check that the file really exists before we test how it decodes.
                Assert.That(RobustFile.Exists(badIcon), Is.True);

                Assert.That(
                    () =>
                    {
                        using (Image.FromFile(badIcon)) { }
                    },
                    Throws.InstanceOf<OutOfMemoryException>(),
                    "Expected GDI+ to throw its misleading OutOfMemoryException for an undecodable image."
                );
            }
        }

        [Test]
        public void EnsureLauncherIcons_UndecodableIcon_FallsBackToDefaultAndWarns()
        {
            using (var folder = new TemporaryFolder("RabLauncherIcon_Fallback"))
            {
                var service = MakeService(folder, out var paths);
                var badIcon = WriteUndecodableImage(Path.Combine(folder.Path, "bad-icon.png"));
                var settings = new RabAppSettings() { IconPath = badIcon };

                // Sanity: confirm the chosen icon really is one GDI+ cannot decode, so we know the
                // test is exercising the fallback rather than passing for some unrelated reason.
                Assert.That(
                    () =>
                    {
                        using (Image.FromFile(badIcon)) { }
                    },
                    Throws.InstanceOf<OutOfMemoryException>()
                );

                string[] iconPaths = null;
                Assert.That(
                    () => iconPaths = service.RunEnsureLauncherIcons(paths, settings),
                    Throws.Nothing,
                    "EnsureLauncherIcons should no longer crash on an undecodable icon."
                );

                AssertIconsAreValidAndCorrectlySized(iconPaths);

                Assert.That(
                    service.Progress.Warnings,
                    Has.Some.Contains("default Bloom icon"),
                    "User should be warned that their chosen icon was unusable."
                );
            }
        }

        [Test]
        public void EnsureLauncherIcons_ValidIcon_ResizesWithoutWarning()
        {
            using (var folder = new TemporaryFolder("RabLauncherIcon_Valid"))
            {
                var service = MakeService(folder, out var paths);
                var goodIcon = WriteValidPng(Path.Combine(folder.Path, "good-icon.png"), 300);
                var settings = new RabAppSettings() { IconPath = goodIcon };

                // Sanity: the chosen icon really is a decodable image.
                using (var probe = Image.FromFile(goodIcon))
                    Assert.That(probe.Width, Is.EqualTo(300));

                var iconPaths = service.RunEnsureLauncherIcons(paths, settings);

                AssertIconsAreValidAndCorrectlySized(iconPaths);
                Assert.That(
                    service.Progress.Warnings,
                    Is.Empty,
                    "A valid icon should not produce any fallback warning."
                );
            }
        }

        [Test]
        public void EnsureLauncherIcons_MissingIcon_Throws()
        {
            using (var folder = new TemporaryFolder("RabLauncherIcon_Missing"))
            {
                var service = MakeService(folder, out var paths);
                var settings = new RabAppSettings()
                {
                    IconPath = Path.Combine(folder.Path, "does-not-exist.png"),
                };

                Assert.That(
                    () => service.RunEnsureLauncherIcons(paths, settings),
                    Throws.InstanceOf<ApplicationException>()
                );
            }
        }
    }
}
