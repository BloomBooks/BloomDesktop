using System.IO;
using BloomTemp;
using NUnit.Framework;

namespace BloomTests
{
    /// <summary>
    /// Checks that <see cref="TestTempDirectory"/> really did move this process's temp directory
    /// before any fixture ran. If these fail, tests are once again writing to machine-global
    /// paths and two concurrent runs can delete each other's folders (BL-16664).
    /// </summary>
    public class TestTempDirectoryTests
    {
        [Test]
        public void TempPath_IsARunFolderOfOurOwn_NotTheMachineTempFolder()
        {
            // Sanity check: the fixture recorded where temp used to be, so there is something to
            // compare against.
            Assert.That(
                TestTempDirectory.MachineTempFolder,
                Is.Not.Null.And.Not.Empty,
                "Setup sanity check: TestTempDirectory should have recorded the original temp folder."
            );

            var current = Normalize(Path.GetTempPath());

            Assert.That(
                current,
                Is.Not.EqualTo(Normalize(TestTempDirectory.MachineTempFolder)),
                "Tests must not write straight into the machine's temp folder, where another test run could delete what they make."
            );
            Assert.That(
                current,
                Is.EqualTo(Normalize(TestTempDirectory.RunFolder)),
                "Path.GetTempPath() should now return this run's own folder."
            );
        }

        /// <summary>
        /// Path.GetTempPath() always ends in a directory separator and the paths we compare it
        /// with do not, so strip it before comparing.
        /// </summary>
        private static string Normalize(string path)
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }

        [Test]
        public void TemporaryFolderWithAFixedName_LandsInsideOurRunFolder()
        {
            // This is the point of the whole exercise: the ~180 existing calls that name a temp
            // folder after their fixture are left exactly as they are, and are scoped to this run
            // anyway. So use a fixed name here too, just as they do.
            using (var folder = new TemporaryFolder("TestTempDirectoryTests_FixedName"))
            {
                Assert.That(Directory.Exists(folder.FolderPath), Is.True);
                Assert.That(
                    Path.GetFullPath(Path.GetDirectoryName(folder.FolderPath)),
                    Is.EqualTo(Path.GetFullPath(TestTempDirectory.RunFolder)),
                    "A plainly-named TemporaryFolder should be created inside this run's folder."
                );
            }
        }
    }
}
