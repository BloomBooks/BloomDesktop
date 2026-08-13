using System.IO;
using Bloom;
using Bloom.Collection;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests
{
    public class ProjectContextTests
    {
        /// <summary>
        /// GetCollectionSettings is documented to fall back to whatever .bloomCollection file is in the
        /// folder when the path it was given does not exist. Callers rely on that when they can only
        /// derive the expected path from the folder name, which is not always the settings file's name:
        /// renaming a collection folder leaves the old file name, and a collection whose name ends with
        /// a period gets a folder without it, because Windows drops trailing periods when it creates a
        /// folder. The fallback used to be handed the settings file path where it wanted the folder, so
        /// instead of repairing anything it threw DirectoryNotFoundException. See BL-16679.
        /// </summary>
        [Test]
        public void GetCollectionSettings_ExpectedNameNotOnDisk_FindsTheSettingsFileThatIsThere()
        {
            using (var collectionFolder = new TemporaryFolder("ProjectContextTests"))
            {
                var folderName = Path.GetFileName(collectionFolder.FolderPath);
                // What a collection named "<folderName>." really looks like on disk: the folder lost
                // the trailing period, the settings file kept it.
                var realSettingsPath = Path.Combine(
                    collectionFolder.FolderPath,
                    folderName + "..bloomCollection"
                );
                RobustFile.WriteAllText(
                    realSettingsPath,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?><Collection version=\"0.2\"><Language1Iso639Code>de</Language1Iso639Code></Collection>"
                );
                // This is what a caller gets by deriving the name from the folder, and it does not exist.
                var expectedSettingsPath = CollectionSettings.GetSettingsFilePath(
                    collectionFolder.FolderPath
                );
                Assert.That(
                    RobustFile.Exists(expectedSettingsPath),
                    Is.False,
                    "the point of the test is that the derived name is not on disk"
                );

                var settings = ProjectContext.GetCollectionSettings(expectedSettingsPath);

                Assert.That(settings.SettingsFilePath, Is.EqualTo(realSettingsPath));
                Assert.That(
                    settings.Language1Tag,
                    Is.EqualTo("de"),
                    "should have really read the settings file we wrote, not just named it"
                );
            }
        }

        /// <summary>
        /// The CollectionSettings constructor treats a path that doesn't exist as "make me a new
        /// collection there", so once GetCollectionSettings started looking in the right folder it
        /// would have silently written a default .bloomCollection into any folder that had none --
        /// e.g. under a book being exported to a spreadsheet from outside a collection.
        /// </summary>
        [Test]
        public void GetCollectionSettings_NoCollectionInFolder_ThrowsRatherThanCreatingOne()
        {
            using (var collectionFolder = new TemporaryFolder("ProjectContextTests"))
            {
                var expectedSettingsPath = CollectionSettings.GetSettingsFilePath(
                    collectionFolder.FolderPath
                );
                // Sanity check: nothing in the folder to find, and nothing to overwrite.
                Assert.That(Directory.EnumerateFiles(collectionFolder.FolderPath), Is.Empty);

                Assert.That(
                    () => ProjectContext.GetCollectionSettings(expectedSettingsPath),
                    Throws.InstanceOf<FileNotFoundException>()
                );

                Assert.That(
                    Directory.EnumerateFiles(collectionFolder.FolderPath),
                    Is.Empty,
                    "must not have written a new default collection settings file"
                );
            }
        }

        [Test]
        public void GetCollectionSettings_ExpectedNameIsOnDisk_UsesIt()
        {
            using (var collectionFolder = new TemporaryFolder("ProjectContextTests"))
            {
                var expectedSettingsPath = CollectionSettings.GetSettingsFilePath(
                    collectionFolder.FolderPath
                );
                RobustFile.WriteAllText(
                    expectedSettingsPath,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?><Collection version=\"0.2\"><Language1Iso639Code>fr</Language1Iso639Code></Collection>"
                );

                var settings = ProjectContext.GetCollectionSettings(expectedSettingsPath);

                Assert.That(settings.SettingsFilePath, Is.EqualTo(expectedSettingsPath));
                Assert.That(settings.Language1Tag, Is.EqualTo("fr"));
            }
        }
    }
}
