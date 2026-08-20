using System.IO;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.web.controllers;
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
                var expectedSettingsPath = CollectionSettings.GetDefaultSettingsFilePath(
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
        /// The collection-file lock is taken on this path before anything else happens, so it has to be
        /// the file that really exists: locking a file that isn't there protects nothing (and throws in
        /// a DEBUG build). See BL-16679.
        /// </summary>
        [Test]
        public void GetRealSettingsPath_ExpectedNameNotOnDisk_ReturnsTheFileThatIs()
        {
            using (var collectionFolder = new TemporaryFolder("ProjectContextTests"))
            {
                var folderName = Path.GetFileName(collectionFolder.FolderPath);
                var realSettingsPath = Path.Combine(
                    collectionFolder.FolderPath,
                    folderName + "..bloomCollection"
                );
                RobustFile.WriteAllText(realSettingsPath, "<Collection version=\"0.2\"/>");
                var derivedPath = CollectionSettings.GetDefaultSettingsFilePath(
                    collectionFolder.FolderPath
                );
                // Sanity check: the name a caller would derive from the folder is not on disk.
                Assert.That(RobustFile.Exists(derivedPath), Is.False);

                Assert.That(
                    ProjectContext.GetRealSettingsPath(derivedPath),
                    Is.EqualTo(realSettingsPath)
                );
            }
        }

        /// <summary>
        /// The trailing period has to come off here, not only in the desktop startup path: this is the
        /// one place every caller passes through, including the command-line ones (creating artifacts,
        /// bulk upload, font analytics). File APIs happily open the un-normalized spelling, so nothing
        /// else notices — until a FileSystemWatcher gets it, which is the original bug. See BL-16679.
        /// </summary>
        [Test]
        public void GetRealSettingsPath_FolderSpelledWithTrailingPeriod_ReturnsTheFolderOnDisk()
        {
            using (var parent = new TemporaryFolder("ProjectContextTests_CliPath"))
            {
                var realFolder = Path.Combine(parent.FolderPath, "Collection Name");
                Directory.CreateDirectory(realFolder);
                var realSettingsPath = Path.Combine(realFolder, "Collection Name..bloomCollection");
                RobustFile.WriteAllText(realSettingsPath, "<Collection version=\"0.2\"/>");

                // What a command-line caller is handed: the folder spelled with the period Windows
                // dropped. Sanity check that file APIs are happy with it, which is why it survives.
                var dottedPath = Path.Combine(
                    parent.FolderPath,
                    "Collection Name.",
                    "Collection Name..bloomCollection"
                );
                Assert.That(
                    RobustFile.Exists(dottedPath),
                    Is.True,
                    "file APIs normalize, which is exactly why the bad spelling goes unnoticed"
                );

                var result = ProjectContext.GetRealSettingsPath(dottedPath);

                Assert.That(Path.GetDirectoryName(result), Is.EqualTo(realFolder));
                Assert.That(
                    Path.GetFileName(result),
                    Is.EqualTo("Collection Name..bloomCollection"),
                    "the settings file's own doubled period must survive"
                );
            }
        }

        [Test]
        public void GetRealSettingsPath_ExpectedNameIsOnDisk_ReturnsItUnchanged()
        {
            using (var collectionFolder = new TemporaryFolder("ProjectContextTests"))
            {
                var path = CollectionSettings.GetDefaultSettingsFilePath(
                    collectionFolder.FolderPath
                );
                RobustFile.WriteAllText(path, "<Collection version=\"0.2\"/>");

                Assert.That(ProjectContext.GetRealSettingsPath(path), Is.EqualTo(path));
            }
        }

        /// <summary>
        /// Callers that had no collection to open must keep whatever behaviour they had; this is not
        /// the place to start reporting the problem, because the lock deliberately tolerates it.
        /// </summary>
        [Test]
        public void GetRealSettingsPath_NothingToFind_ReturnsWhatItWasGiven()
        {
            using (var collectionFolder = new TemporaryFolder("ProjectContextTests"))
            {
                var path = CollectionSettings.GetDefaultSettingsFilePath(
                    collectionFolder.FolderPath
                );

                Assert.That(ProjectContext.GetRealSettingsPath(path), Is.EqualTo(path));

                // And a folder that isn't there at all must not throw.
                var missing = Path.Combine(
                    collectionFolder.FolderPath,
                    "No Such Folder",
                    "No Such Folder.bloomCollection"
                );
                Assert.That(ProjectContext.GetRealSettingsPath(missing), Is.EqualTo(missing));
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
                var expectedSettingsPath = CollectionSettings.GetDefaultSettingsFilePath(
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
                var expectedSettingsPath = CollectionSettings.GetDefaultSettingsFilePath(
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

        [TearDown]
        public void TearDown()
        {
            // Some tests below set this application-level static; don't leave it set for other fixtures.
            CommonApi.CurrentCollectionSettings = null;
        }

        /// <summary>
        /// Everything a project leaves on application-level objects has to come off again, whether the
        /// project shut down tidily or blew up half built. If the endpoint handlers stay registered, the
        /// next collection the user opens dies on a duplicate key and they are stuck until they restart
        /// Bloom. See BL-16678.
        /// </summary>
        [Test]
        public void ReleaseApplicationLevelProjectState_RemovesOnlyWhatTheProjectAdded()
        {
            var server = new BloomServer(new BookSelection());
            server.ApiHandler.RegisterEndpointHandler("common/instanceInfo", request => { }, false);
            server.ApiHandler.RecordApplicationLevelHandlers();
            server.ApiHandler.RegisterEndpointHandler("audio/startRecord", request => { }, false);
            CommonApi.CurrentCollectionSettings = new CollectionSettings();
            // Sanity checks on the state we are asking it to undo.
            Assert.That(server.ApiHandler.HasProjectLevelHandlers, Is.True);
            Assert.That(CommonApi.CurrentCollectionSettings, Is.Not.Null);

            ProjectContext.ReleaseApplicationLevelProjectState(server);

            Assert.That(server.ApiHandler.HasProjectLevelHandlers, Is.False);
            Assert.That(CommonApi.CurrentCollectionSettings, Is.Null);
            Assert.That(
                () =>
                    server.ApiHandler.RegisterEndpointHandler(
                        "common/instanceInfo",
                        request => { },
                        false
                    ),
                Throws.ArgumentException,
                "the application-level handler should still be registered"
            );
        }

        /// <summary>
        /// The constructor disposes itself when it fails, and it can fail before it ever gets hold of
        /// the server, so this has to cope with having no server to clean up. See BL-16678.
        /// </summary>
        [Test]
        public void ReleaseApplicationLevelProjectState_NoServer_StillForgetsTheCollection()
        {
            CommonApi.CurrentCollectionSettings = new CollectionSettings();

            Assert.That(
                () => ProjectContext.ReleaseApplicationLevelProjectState(null),
                Throws.Nothing
            );

            Assert.That(CommonApi.CurrentCollectionSettings, Is.Null);
        }

        [Test]
        public void ReleaseApplicationLevelProjectState_CalledTwice_DoesNotThrow()
        {
            var server = new BloomServer(new BookSelection());
            server.ApiHandler.RegisterEndpointHandler("audio/startRecord", request => { }, false);

            ProjectContext.ReleaseApplicationLevelProjectState(server);

            Assert.That(
                () => ProjectContext.ReleaseApplicationLevelProjectState(server),
                Throws.Nothing
            );
        }
    }
}
