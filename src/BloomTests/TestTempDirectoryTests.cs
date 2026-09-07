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
        /// <summary>
        /// The redirect happened at all: this process's temp directory is our own run folder
        /// rather than the machine-wide one that other runs also write to.
        /// </summary>
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

        /// <summary>
        /// A run folder's name is built from a process id, and Windows recycles those, so a run
        /// can be handed the path of a crashed earlier run whose files are still sitting there.
        /// Starting from whatever that run left behind would cause exactly the stale-file
        /// confusion this class exists to prevent, so the folder must be emptied, not just made.
        /// </summary>
        [Test]
        public void StartFolderEmpty_FolderAlreadyHasFilesInIt_ClearsThem()
        {
            using (var parent = new TemporaryFolder("StartFolderEmptyTests"))
            {
                var reusedPath = Path.Combine(parent.FolderPath, "a7920d49-p12345");
                Directory.CreateDirectory(Path.Combine(reusedPath, "leftover subfolder"));
                File.WriteAllText(
                    Path.Combine(reusedPath, "leftover subfolder", "stale.txt"),
                    "from a run that died"
                );
                File.WriteAllText(Path.Combine(reusedPath, "also-stale.txt"), "likewise");

                // Sanity check: the setup really did leave something behind to be cleared.
                Assert.That(
                    Directory.GetFiles(reusedPath, "*", SearchOption.AllDirectories).Length,
                    Is.EqualTo(2),
                    "Setup sanity check: two stale files should be sitting in the reused folder."
                );

                TestTempDirectory.StartFolderEmpty(reusedPath);

                Assert.That(
                    Directory.Exists(reusedPath),
                    Is.True,
                    "The folder should still be there, ready to be used."
                );
                Assert.That(
                    Directory.GetFileSystemEntries(reusedPath),
                    Is.Empty,
                    "Nothing from the previous run should have survived into this one."
                );
            }
        }

        /// <summary>
        /// The same call is what creates the folder in the ordinary case, where nothing is there.
        /// </summary>
        [Test]
        public void StartFolderEmpty_FolderDoesNotExist_CreatesIt()
        {
            using (var parent = new TemporaryFolder("StartFolderEmptyCreates"))
            {
                var path = Path.Combine(parent.FolderPath, "brand-new");
                Assert.That(Directory.Exists(path), Is.False, "Setup sanity check.");

                TestTempDirectory.StartFolderEmpty(path);

                Assert.That(Directory.Exists(path), Is.True);
            }
        }

        /// <summary>
        /// When a test leaves a file open, the end-of-run warning has to say which file and why,
        /// otherwise "could not delete the temp folder" gives whoever reads it nowhere to start.
        /// </summary>
        [Test]
        public void DescribeWhyFolderCouldNotBeDeleted_FileStillOpen_NamesThatFileAndTheReason()
        {
            using (var folder = new TemporaryFolder("DescribeWhyFolderCouldNotBeDeleted"))
            {
                var lockedPath = Path.Combine(folder.FolderPath, "someone-left-me-open.txt");
                File.WriteAllText(lockedPath, "contents");
                var innocentPath = Path.Combine(folder.FolderPath, "closed-properly.txt");
                File.WriteAllText(innocentPath, "contents");

                // Sanity check: with nothing holding either file, no file should be singled out.
                Assert.That(
                    TestTempDirectory.DescribeWhyFolderCouldNotBeDeleted(folder.FolderPath),
                    Does.Not.Contain("could not be opened"),
                    "Setup sanity check: neither file is open yet, so none should be blamed."
                );

                using (File.Open(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var message = TestTempDirectory.DescribeWhyFolderCouldNotBeDeleted(
                        folder.FolderPath
                    );

                    Assert.That(message, Is.Not.Null);
                    Assert.That(
                        message,
                        Does.Contain("someone-left-me-open.txt"),
                        "The warning should name the file that is actually held."
                    );
                    Assert.That(
                        message,
                        Does.Not.Contain("closed-properly.txt"),
                        "It should point at the locked file, not just the first file it happened to find."
                    );
                    Assert.That(
                        message,
                        Does.Contain("being used by another process").IgnoreCase,
                        "It should pass on the reason the operating system gave."
                    );
                }
            }
        }

        /// <summary>
        /// The same call is how the teardown decides whether there is anything to complain about
        /// at all, so a folder that really did go must produce nothing.
        /// </summary>
        [Test]
        public void DescribeWhyFolderCouldNotBeDeleted_FolderIsGone_SaysNothing()
        {
            string path;
            using (var folder = new TemporaryFolder("DescribeWhyFolderIsGone"))
            {
                path = folder.FolderPath;
                Assert.That(Directory.Exists(path), Is.True, "Setup sanity check.");
            }

            Assert.That(Directory.Exists(path), Is.False, "Setup sanity check: it was disposed.");
            Assert.That(TestTempDirectory.DescribeWhyFolderCouldNotBeDeleted(path), Is.Null);
        }

        /// <summary>
        /// The redirect reaches the code that matters: an ordinary fixed-name TemporaryFolder,
        /// written exactly as the ~180 existing calls are, is created inside our run folder.
        /// </summary>
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
