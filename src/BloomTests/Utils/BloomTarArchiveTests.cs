using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Utils;
using ICSharpCode.SharpZipLib.Tar;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Utils
{
    [TestFixture]
    class BloomTarArchiveTests
    {
        private const string kTestFolderName = "BloomTarArchiveTests";

        [Test]
        public void AddDirectoryContents_GivenSimpleArchive_WritesContentAtExpectedPath()
        {
            ///////////
            // Setup //
            ///////////
            string fileContents = "Hello.";

            // When disposing, deletes the temporary folder including its contents.
            using (var testFolder = new TemporaryFolder(kTestFolderName))
            {
                var testFilePath = Path.Combine(
                    testFolder.Path,
                    Path.ChangeExtension(Path.GetRandomFileName(), ".txt")
                );
                RobustFile.WriteAllText(testFilePath, fileContents);

                // This is not under the test folder. It makes creating the archive less thorny that way. make sure it gets cleaned up.
                using (var archiveFile = TempFile.WithExtension(".tar"))
                {
                    ///////////////////////
                    // System under test //
                    ///////////////////////
                    var archive = new BloomTarArchive(archiveFile.Path);
                    archive.AddDirectoryContents(testFolder.Path);
                    archive.Save();

                    //////////////////
                    // Verification //
                    //////////////////
                    var inStream = RobustFile.OpenRead(archiveFile.Path);
                    TarArchive archiveForReading = TarArchive.CreateInputTarArchive(
                        inStream,
                        Encoding.UTF8
                    );
                    using (
                        var extractedFolder = new TemporaryFolder(
                            testFolder,
                            "extractedTarContents"
                        )
                    )
                    {
                        archiveForReading.ExtractContents(extractedFolder.Path, false);

                        string expectedFilePath = Path.Combine(
                            extractedFolder.Path,
                            Path.GetFileName(testFilePath)
                        );
                        FileAssert.Exists(expectedFilePath);
                        FileAssert.AreEqual(
                            expectedFilePath,
                            testFilePath,
                            "Incorrect file contents."
                        );

                        archiveForReading.Close();
                    }
                }

                /////////////
                // Cleanup //
                /////////////

                // Nothing else necessary, disposing the TemporaryFolder cleans up all its subcontents.
            }
        }

        [Test]
        public void AddDirectoryContents_GivenSubfolder_WritesContentAtExpectedPath()
        {
            ///////////
            // Setup //
            ///////////
            string fileContents = "Hello.";

            // When disposing, deletes the temporary folder including its contents.
            using (var testFolder = new TemporaryFolder(kTestFolderName))
            {
                var subFolder = new TemporaryFolder(testFolder, "subFolder");
                var testFilePath = Path.Combine(
                    subFolder.Path,
                    Path.ChangeExtension(Path.GetRandomFileName(), ".txt")
                );
                RobustFile.WriteAllText(testFilePath, fileContents);

                // This is not under the test folder. It makes creating the archive less thorny that way. make sure it gets cleaned up.
                using (var archiveFile = TempFile.WithExtension(".tar"))
                {
                    ///////////////////////
                    // System under test //
                    ///////////////////////
                    var archive = new BloomTarArchive(archiveFile.Path);
                    archive.AddDirectoryContents(testFolder.Path);
                    archive.Save();

                    //////////////////
                    // Verification //
                    //////////////////
                    var inStream = RobustFile.OpenRead(archiveFile.Path);
                    TarArchive archiveForReading = TarArchive.CreateInputTarArchive(
                        inStream,
                        Encoding.UTF8
                    );
                    using (
                        var extractedFolder = new TemporaryFolder(
                            testFolder,
                            "extractedTarContents"
                        )
                    )
                    {
                        archiveForReading.ExtractContents(extractedFolder.Path, false);

                        string expectedFilePath = Path.Combine(
                            extractedFolder.Path,
                            "subFolder",
                            Path.GetFileName(testFilePath)
                        );
                        FileAssert.Exists(expectedFilePath);
                        FileAssert.AreEqual(
                            expectedFilePath,
                            testFilePath,
                            "Incorrect file contents."
                        );

                        archiveForReading.Close();
                    }
                }

                /////////////
                // Cleanup //
                /////////////

                // Nothing else necessary, disposing the TemporaryFolder cleans up all its subcontents.
            }
        }
    }
}
