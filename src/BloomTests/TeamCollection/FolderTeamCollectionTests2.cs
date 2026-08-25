using System;
using System.IO;
using System.Linq;
using System.Threading;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.MiscUI;
using Bloom.TeamCollection;
using Bloom.web;
using BloomTemp;
using Moq;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.TeamCollection
{
    /// <summary>
    /// The main FolderTeamCollectionTests class is mostly made up of tests that benefit from a OneTimeSetup
    /// function that creates a collection already connected to local and TeamCollection folders that
    /// already have some content created in the OneTimeSetup. This class is for tests
    /// where that setup gets in the way.
    /// </summary>
    public class FolderTeamCollectionTests2
    {
        [Test]
        public void ConnectToTeamCollection_SetsUpRequiredFiles()
        {
            using (
                var collectionFolder = new TemporaryFolder("FolderTeamCollectionTests2_Collection")
            )
            {
                using (var sharedFolder = new TemporaryFolder("FolderTeamCollectionTests2_Shared"))
                {
                    var bookFolderName1 = "Some book";
                    SyncAtStartupTests.MakeFakeBook(
                        collectionFolder.FolderPath,
                        bookFolderName1,
                        "Something"
                    );
                    // BL-9573 tests cases where the book name isn't exactly the same as the folder name
                    var bookFolderName2 = "Some other book";
                    SyncAtStartupTests.MakeFakeBook(
                        collectionFolder.FolderPath,
                        "Some other name altogether",
                        "Strange book content",
                        bookFolderName2
                    );
                    var settingsPath = CollectionSettings.GetDefaultSettingsFilePath(
                        collectionFolder.FolderPath
                    );

                    // As an aside, this is a convenient place to check that a TC manager created when TC settings does not exist
                    // functions and does not have a current collection.
                    var tcManager = new TeamCollectionManager(
                        settingsPath,
                        null,
                        new BookStatusChangeEvent(),
                        null,
                        null,
                        null
                    );
                    Assert.That(tcManager.CurrentCollection, Is.Null);

                    RobustFile.WriteAllText(settingsPath, "This is a fake settings file");
                    FolderTeamCollection.CreateTeamCollectionLinkFile(
                        collectionFolder.FolderPath,
                        sharedFolder.FolderPath
                    );

                    var nonBookFolder = Path.Combine(
                        collectionFolder.FolderPath,
                        "Some other folder"
                    );
                    Directory.CreateDirectory(nonBookFolder);
                    tcManager = new TeamCollectionManager(
                        settingsPath,
                        null,
                        new BookStatusChangeEvent(),
                        null,
                        null,
                        null
                    );
                    var collection = tcManager.CurrentCollection;

                    // sut
                    (collection as FolderTeamCollection)?.SetupTeamCollection(
                        sharedFolder.FolderPath,
                        new NullWebSocketProgress()
                    );

                    Assert.That(collection, Is.Not.Null);
                    var joinCollectionPath = Path.Combine(
                        sharedFolder.FolderPath,
                        "Join this Team Collection.JoinBloomTC"
                    );
                    Assert.That(File.Exists(joinCollectionPath));

                    var teamCollectionLinkPath = Path.Combine(
                        collectionFolder.FolderPath,
                        TeamCollectionManager.TeamCollectionLinkFileName
                    );
                    Assert.That(File.Exists(teamCollectionLinkPath));
                    var collectionFileContent = RobustFile.ReadAllText(teamCollectionLinkPath);
                    Assert.That(collectionFileContent, Is.EqualTo(sharedFolder.FolderPath));
                    var sharedSettingsPath = CollectionSettings.GetDefaultSettingsFilePath(
                        collectionFolder.FolderPath
                    );
                    Assert.That(
                        RobustFile.ReadAllText(sharedSettingsPath),
                        Is.EqualTo("This is a fake settings file")
                    );
                    var bookPath = Path.Combine(
                        sharedFolder.FolderPath,
                        "Books",
                        bookFolderName1 + ".bloom"
                    );
                    Assert.That(File.Exists(bookPath));
                    var bookPath2 = Path.Combine(
                        sharedFolder.FolderPath,
                        "Books",
                        bookFolderName2 + ".bloom"
                    );
                    Assert.That(File.Exists(bookPath2));
                }
            }
        }

        [Test]
        public void FilesToMonitorForCollection_NonStandardCollectionFileName_FindsIt()
        {
            using (
                var collectionFolder = new TemporaryFolder(
                    "SyncLocalAndRepoCollectionFiles_SyncsInRightDirection_Collection"
                )
            )
            {
                using (
                    var repoFolder = new TemporaryFolder(
                        "SyncLocalAndRepoCollectionFiles_SyncsInRightDirection_Shared"
                    )
                )
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    var tc = new FolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    tc.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
                    var bcPath = Path.Combine(
                        collectionFolder.FolderPath,
                        "mybooks.bloomCollection"
                    );
                    File.WriteAllText(bcPath, "something");
                    var files = tc.FilesToMonitorForCollection();
                    Assert.That(files, Contains.Item(bcPath));
                }
            }
        }

        [Test]
        public void FixPossibleCaseChange_ChangesCase()
        {
            using (var collectionFolder = new TemporaryFolder("FixPossibleCaseChange_ChangesCase"))
            {
                using (
                    var repoFolder = new TemporaryFolder("FixPossibleCaseChange_ChangesCase_Shared")
                )
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    TeamCollectionManager.ForceCurrentUserForTests("me@somewhere.org");
                    var tc = new FolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    var fakeMetaData = @"{""rubbish"":""this is phony""}";
                    tc.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
                    var oldFolderPath = SyncAtStartupTests.MakeFakeBook(
                        collectionFolder.FolderPath,
                        "old name",
                        "book content",
                        metaJsonIfDifferent: fakeMetaData
                    );
                    tc.PutBook(oldFolderPath);
                    var repoBookPath = Path.Combine(
                        repoFolder.FolderPath,
                        "books",
                        "old name.bloom"
                    );
                    Assert.That(File.Exists(repoBookPath));
                    var repoBookPathChangeCase = Path.Combine(
                        repoFolder.FolderPath,
                        "books",
                        "Old Name.bloom"
                    );
                    BookStorage.MoveFilePossiblyOnlyChangingCaseAllowReplace(
                        repoBookPath,
                        repoBookPathChangeCase
                    );
                    var metaDataPath = BookMetaData.MetaDataPath(oldFolderPath);
                    RobustFile.WriteAllText(metaDataPath, new BookMetaData().Json);

                    // sut 1: we made the case of the repo and local book differ, so this should be true.
                    Assert.That(tc.DoLocalAndRemoteNamesDifferOnlyByCase("Old Name"), Is.True);

                    // sut 2: fix it
                    tc.EnsureConsistentCasingInLocalName("Old Name");

                    // and that should have fixed it
                    Assert.That(tc.DoLocalAndRemoteNamesDifferOnlyByCase("Old Name"), Is.False);

                    var realRepoName = Path.GetFileNameWithoutExtension(
                        Directory
                            .EnumerateFiles(Path.GetDirectoryName(repoBookPath), "old name.bloom")
                            .FirstOrDefault()
                    );
                    Assert.That(realRepoName, Is.EqualTo("Old Name"));
                    var realLocalFileName = Path.GetFileNameWithoutExtension(
                        Directory.EnumerateFiles(oldFolderPath, "old name.htm").FirstOrDefault()
                    );
                    Assert.That(realLocalFileName, Is.EqualTo("Old Name"));
                    var updatedMetaData = RobustFile.ReadAllText(metaDataPath);
                    Assert.That(updatedMetaData, Is.EqualTo(fakeMetaData));
                }
            }
        }

        [Test]
        public void SyncLocalAndRepoCollectionFiles_SyncsInRightDirection()
        {
            using (
                var collectionFolder = new TemporaryFolder(
                    "SyncLocalAndRepoCollectionFiles_SyncsInRightDirection_Collection"
                )
            )
            {
                using (
                    var repoFolder = new TemporaryFolder(
                        "SyncLocalAndRepoCollectionFiles_SyncsInRightDirection_Shared"
                    )
                )
                {
                    var settingsPath = CollectionSettings.GetDefaultSettingsFilePath(
                        collectionFolder.FolderPath
                    );
                    var tcManager = new TeamCollectionManager(
                        settingsPath,
                        null,
                        new BookStatusChangeEvent(),
                        null,
                        null,
                        null
                    );
                    var tc = new FolderTeamCollection(
                        tcManager,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    tc.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
                    var bloomCollectionPath = Bloom.TeamCollection.TeamCollection.CollectionPath(
                        collectionFolder.FolderPath
                    );
                    Assert.That(
                        tc.LocalCollectionFilesRecordedSyncTime,
                        Is.EqualTo(DateTime.MinValue)
                    );
                    File.WriteAllText(bloomCollectionPath, "This is a fake collection file");
                    var collectionStylesPath = Path.Combine(
                        collectionFolder.FolderPath,
                        "customCollectionStyles.css"
                    );
                    RobustFile.WriteAllText(collectionStylesPath, "This is the collection styles");

                    // SUT 1: nothing in repo, no sync time file. Copies to repo.
                    tc.SyncLocalAndRepoCollectionFiles();

                    var localWriteTime1 = tc.LocalCollectionFilesRecordedSyncTime();
                    Assert.That(localWriteTime1, Is.LessThanOrEqualTo(DateTime.Now));
                    Assert.That(
                        localWriteTime1,
                        Is.GreaterThan(DateTime.Now.Subtract(new TimeSpan(0, 0, 5, 0)))
                    );
                    var otherFilesPath = FolderTeamCollection.GetRepoProjectFilesZipPath(
                        repoFolder.FolderPath
                    );
                    Assert.That(File.Exists(otherFilesPath));
                    var anotherPlace = Path.Combine(repoFolder.FolderPath, "anotherPlace.zip");
                    RobustFile.Copy(otherFilesPath, anotherPlace);
                    var repoWriteTime1 = new FileInfo(otherFilesPath).LastWriteTime;
                    var collectionWriteTime1 = new FileInfo(bloomCollectionPath).LastWriteTime;

                    // SUT 2: nothing has changed. But it's a startup, so sync still happens to local.
                    tc.SyncLocalAndRepoCollectionFiles();
                    var localWriteTime2 = tc.LocalCollectionFilesRecordedSyncTime();
                    Assert.That(localWriteTime2, Is.GreaterThanOrEqualTo(localWriteTime1));
                    Assert.That(
                        new FileInfo(otherFilesPath).LastWriteTime,
                        Is.EqualTo(repoWriteTime1)
                    );
                    Assert.That(
                        new FileInfo(bloomCollectionPath).LastWriteTime,
                        Is.GreaterThanOrEqualTo(collectionWriteTime1)
                    );

                    // We need to make sure the write time of the modified file is measurably different
                    // so Bloom knows there is a change.
                    Thread.Sleep(2);
                    File.WriteAllText(
                        bloomCollectionPath,
                        "This is a modified fake collection file"
                    );
                    var collectionWriteTime2 = new FileInfo(bloomCollectionPath).LastWriteTime;
                    // According to https://stackoverflow.com/questions/31519880/windows-compatible-filesystems-file-time-resolutions,
                    // LastWriteTime on NTFS has a resolution of 100ns, as does the DateTime object. So even a
                    // 1ms delay before we write a file should result in a strictly greater LastWriteTime. Using 2 for a little more margin.
                    Thread.Sleep(2);

                    // SUT 3: local change copied to repo (only when not at startup)
                    tc.SyncLocalAndRepoCollectionFiles(false);
                    var localWriteTime3 = tc.LocalCollectionFilesRecordedSyncTime();
                    Assert.That(
                        localWriteTime3,
                        Is.GreaterThan(localWriteTime1),
                        "localWriteTime3 should be greater than localWriteTime1"
                    );
                    var repoWriteTime2 = new FileInfo(otherFilesPath).LastWriteTime;
                    Assert.That(
                        repoWriteTime2,
                        Is.GreaterThan(repoWriteTime1),
                        "repoWriteTime2 should be greater than repoWriteTime1"
                    );
                    // not modified by sync
                    Assert.That(
                        new FileInfo(bloomCollectionPath).LastWriteTime,
                        Is.EqualTo(collectionWriteTime2)
                    );

                    Thread.Sleep(2);
                    File.WriteAllText(
                        bloomCollectionPath,
                        "This is a further modified fake collection file"
                    );
                    var collectionWriteTime3 = new FileInfo(bloomCollectionPath).LastWriteTime;
                    var version2Path = Path.Combine(repoFolder.FolderPath, "version2.zip");
                    RobustFile.Copy(otherFilesPath, version2Path);
                    // modify the remote version by copying the old one back.
                    Thread.Sleep(2);
                    RobustFile.Copy(anotherPlace, otherFilesPath, true);
                    var repoWriteTime3 = new FileInfo(otherFilesPath).LastWriteTime;
                    Assert.That(
                        repoWriteTime3,
                        Is.GreaterThan(collectionWriteTime3),
                        "repo file written after local collection file [sanity check]"
                    );

                    // SUT 4: both changed: repo wins
                    Thread.Sleep(2);
                    tc.SyncLocalAndRepoCollectionFiles();
                    var localWriteTime4 = tc.LocalCollectionFilesRecordedSyncTime();
                    Assert.That(
                        localWriteTime4,
                        Is.GreaterThan(localWriteTime3),
                        "localWriteTime4 should be greater than localWriteTime3"
                    );
                    var repoWriteTime4 = new FileInfo(otherFilesPath).LastWriteTime;
                    Assert.That(repoWriteTime4, Is.EqualTo(repoWriteTime3)); // not modified by sync
                    Assert.That(
                        new FileInfo(bloomCollectionPath).LastWriteTime,
                        Is.GreaterThan(collectionWriteTime3),
                        "bloomCollection LastWriteTime should be greater than collectionWriteTime3"
                    );
                    // We got the original back.
                    Assert.That(
                        File.ReadAllText(bloomCollectionPath),
                        Is.EqualTo("This is a fake collection file")
                    );

                    Thread.Sleep(2);
                    var allowedWords = Path.Combine(collectionFolder.FolderPath, "Allowed Words");
                    Directory.CreateDirectory(allowedWords);
                    File.WriteAllText(Path.Combine(allowedWords, "file1.txt"), "fake word list");

                    // SUT5: local allowed words added
                    tc.SyncLocalAndRepoCollectionFiles(false);
                    var localWriteTime5 = tc.LocalCollectionFilesRecordedSyncTime();
                    Assert.That(
                        localWriteTime5,
                        Is.GreaterThan(localWriteTime4),
                        "localWriteTime5 should be greater than localWriteTime4"
                    );
                    var repoWriteTime5 = new FileInfo(otherFilesPath).LastWriteTime;
                    Assert.That(
                        repoWriteTime5,
                        Is.GreaterThan(repoWriteTime4),
                        "repoWriteTime5 should be greater than repoWriteTime4"
                    );

                    Thread.Sleep(2);
                    var sampleTexts = Path.Combine(collectionFolder.FolderPath, "Sample Texts");
                    Directory.CreateDirectory(sampleTexts);
                    File.WriteAllText(
                        Path.Combine(allowedWords, "sample1.txt"),
                        "fake sample list"
                    );

                    // SUT6: local sample texts added
                    tc.SyncLocalAndRepoCollectionFiles(false);
                    var localWriteTime6 = tc.LocalCollectionFilesRecordedSyncTime();
                    Assert.That(
                        localWriteTime6,
                        Is.GreaterThan(localWriteTime5),
                        "localWriteTime6 should be greater than localWriteTime5"
                    );
                    var repoWriteTime6 = new FileInfo(otherFilesPath).LastWriteTime;
                    Assert.That(
                        repoWriteTime6,
                        Is.GreaterThan(repoWriteTime5),
                        "repoWriteTime6 should be greater than repoWriteTime5"
                    );

                    Thread.Sleep(2);
                    File.WriteAllText(
                        Path.Combine(allowedWords, "sample1.txt"),
                        "fake sample list"
                    );

                    // SUT7: local file write time modified, but not actually changed. Want the sync time to
                    // update, but NOT to write the remote file.
                    tc.SyncLocalAndRepoCollectionFiles(false);
                    var localWriteTime7 = tc.LocalCollectionFilesRecordedSyncTime();
                    Assert.That(
                        localWriteTime7,
                        Is.GreaterThan(localWriteTime6),
                        "localWriteTime7 should be greater than localWriteTime6"
                    );
                    var repoWriteTime7 = new FileInfo(otherFilesPath).LastWriteTime;
                    Assert.That(repoWriteTime7, Is.EqualTo(repoWriteTime6));

                    tc._haveShownRemoteSettingsChangeWarning = false;
                    Thread.Sleep(2);
                    File.WriteAllText(
                        bloomCollectionPath,
                        "This is a modified fake collection file, for SUT 8"
                    );
                    var collectionWriteTimeBeforeSut8 = new FileInfo(
                        bloomCollectionPath
                    ).LastWriteTime;
                    var localWriteTimeBeforeSut8 = tc.LocalCollectionFilesRecordedSyncTime();
                    var repoWriteTimeBeforeSut8 = new FileInfo(otherFilesPath).LastWriteTime;

                    // SUT 8: local change copied to repo on idle
                    Thread.Sleep(2);
                    tc.SyncLocalAndRepoCollectionFiles(false);
                    Assert.That(
                        tc._haveShownRemoteSettingsChangeWarning,
                        Is.False,
                        "user should not have been warned"
                    );
                    var localWriteTimeAfterSut8 = tc.LocalCollectionFilesRecordedSyncTime();
                    Assert.That(
                        localWriteTimeAfterSut8,
                        Is.GreaterThan(localWriteTimeBeforeSut8),
                        "localWriteTime should increase copying on idle"
                    );
                    var repoWriteTimeAfterSut8 = new FileInfo(otherFilesPath).LastWriteTime;
                    Assert.That(
                        repoWriteTimeAfterSut8,
                        Is.GreaterThan(repoWriteTimeBeforeSut8),
                        "repoWriteTime should increase copying on idle"
                    );
                    // not modified by sync
                    Assert.That(
                        new FileInfo(bloomCollectionPath).LastWriteTime,
                        Is.EqualTo(collectionWriteTimeBeforeSut8)
                    );

                    // modify the remote version by copying version2 back.
                    Thread.Sleep(2);
                    var repoWriteTimeBeforeSut9Copy = new FileInfo(otherFilesPath).LastWriteTime;
                    RobustFile.Copy(version2Path, otherFilesPath, true);
                    var collectionWriteTimeBeforeSut9 = new FileInfo(
                        bloomCollectionPath
                    ).LastWriteTime;
                    var repoWriteTimeBeforeSut9 = new FileInfo(otherFilesPath).LastWriteTime;
                    Assert.That(
                        repoWriteTimeBeforeSut9,
                        Is.GreaterThan(repoWriteTimeBeforeSut9Copy),
                        "repo file written after local collection file [sanity check]"
                    );
                    tc._haveShownRemoteSettingsChangeWarning = false;

                    // SUT9: repo modified, doing check on idle. No changes or warning.
                    Thread.Sleep(2);
                    tc.SyncLocalAndRepoCollectionFiles(false);
                    Assert.That(
                        tc._haveShownRemoteSettingsChangeWarning,
                        Is.False,
                        "user should not have been warned"
                    );
                    var collectionWriteTimeAfterSut9 = new FileInfo(
                        bloomCollectionPath
                    ).LastWriteTime;
                    Assert.That(
                        collectionWriteTimeAfterSut9,
                        Is.EqualTo(collectionWriteTimeBeforeSut9),
                        "local settings should not have been modified"
                    );

                    File.WriteAllText(
                        bloomCollectionPath,
                        "This is a modified fake collection file, for SUT 10"
                    );
                    var collectionWriteTimeBeforeSut10 = new FileInfo(
                        bloomCollectionPath
                    ).LastWriteTime;
                    var localWriteTimeBeforeSut10 = tc.LocalCollectionFilesRecordedSyncTime();
                    var repoWriteTimeBeforeSut10 = new FileInfo(otherFilesPath).LastWriteTime;

                    // SUT10: both modified, doing check on idle. No changes. User warned.
                    Thread.Sleep(2);
                    using (var se = new BloomMessageBox.ShowExpected())
                    {
                        tc.SyncLocalAndRepoCollectionFiles(false);
                        Assert.That(se.Message, Is.Not.Empty);
                    }

                    Assert.That(
                        tc._haveShownRemoteSettingsChangeWarning,
                        Is.True,
                        "user should have been warned"
                    );
                    var localWriteTimeAfterSut10 = tc.LocalCollectionFilesRecordedSyncTime();
                    Assert.That(
                        localWriteTimeAfterSut10,
                        Is.EqualTo(localWriteTimeBeforeSut10),
                        "localWriteTime should not be changed by idle sync where both changed"
                    );
                    var repoWriteTimeAfterSut10 = new FileInfo(otherFilesPath).LastWriteTime;
                    Assert.That(
                        repoWriteTimeAfterSut10,
                        Is.EqualTo(repoWriteTimeBeforeSut10),
                        "repo should not be modified by idle sync where both changed"
                    ); // not modified by sync
                    Assert.That(
                        new FileInfo(bloomCollectionPath).LastWriteTime,
                        Is.EqualTo(collectionWriteTimeBeforeSut10),
                        "bloomCollection LastWriteTime should not be changed by idle sync both changed"
                    );

                    // Get everything back in sync
                    tc.SyncLocalAndRepoCollectionFiles();
                    var localWriteTimeBeforeSut11 = tc.LocalCollectionFilesRecordedSyncTime();
                    var repoWriteTimeBeforeSut11 = new FileInfo(otherFilesPath).LastWriteTime;
                    Thread.Sleep(2);
                    RobustFile.WriteAllText(
                        collectionStylesPath,
                        "This is the modified collection styles"
                    );

                    // SUT11: custom collection styles modified while Bloom was not running. Copied to repo.
                    Thread.Sleep(2);
                    tc.SyncLocalAndRepoCollectionFiles();
                    var repoWriteTimeAfterSut11 = new FileInfo(otherFilesPath).LastWriteTime;
                    Assert.That(
                        repoWriteTimeAfterSut11,
                        Is.GreaterThanOrEqualTo(repoWriteTimeBeforeSut11)
                    );
                    var localWriteTimeAfterSut11 = tc.LocalCollectionFilesRecordedSyncTime();
                    // We will update the sync time even though the write is the other way.
                    Assert.That(
                        localWriteTimeAfterSut11,
                        Is.GreaterThan(localWriteTimeBeforeSut11)
                    );
                    Assert.That(
                        File.ReadAllText(collectionStylesPath),
                        Is.EqualTo("This is the modified collection styles")
                    );
                }
            }
        }

        [Test]
        public void Checkin_RenamedBook_DeletesOriginal_NoTombstone()
        {
            using (
                var collectionFolder = new TemporaryFolder(
                    "Checkin_RenamedBook_DeletesOriginal_Collection"
                )
            )
            {
                using (
                    var repoFolder = new TemporaryFolder(
                        "Checkin_RenamedBook_DeletesOriginal_Shared"
                    )
                )
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    TeamCollectionManager.ForceCurrentUserForTests("me@somewhere.org");
                    var tc = new FolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    tc.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
                    var oldFolderPath = SyncAtStartupTests.MakeFakeBook(
                        collectionFolder.FolderPath,
                        "old name",
                        "book content"
                    );
                    tc.PutBook(oldFolderPath);
                    tc.AttemptLock("old name");
                    SyncAtStartupTests.SimulateRename(tc, "old name", "middle name");
                    SyncAtStartupTests.SimulateRename(tc, "middle name", "new name");
                    tc.PutBook(Path.Combine(collectionFolder.FolderPath, "new name"), true);
                    Assert.That(File.Exists(tc.GetPathToBookFileInRepo("new name")), Is.True);
                    Assert.That(
                        File.Exists(tc.GetPathToBookFileInRepo("old name")),
                        Is.False,
                        "old name was not deleted"
                    );
                    var status = tc.GetLocalStatus("new name");
                    Assert.That(
                        status.oldName ?? "",
                        Is.Empty,
                        "Should stop tracking previous name once we cleaned it up"
                    );
                    Assert.That(tc.KnownToHaveBeenDeleted("old name"), Is.False);
                    TeamCollectionManager.ForceCurrentUserForTests(null);
                }
            }
        }

        [Test]
        public void Checkin_RenamedBook_MissingOriginalRepoFile_StillSucceeds()
        {
            using (
                var collectionFolder = new TemporaryFolder(
                    "Checkin_RenamedBook_MissingOriginalRepoFile_Collection"
                )
            )
            {
                using (
                    var repoFolder = new TemporaryFolder(
                        "Checkin_RenamedBook_MissingOriginalRepoFile_Shared"
                    )
                )
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    TeamCollectionManager.ForceCurrentUserForTests("me@somewhere.org");
                    var tc = new FolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    tc.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
                    var oldFolderPath = SyncAtStartupTests.MakeFakeBook(
                        collectionFolder.FolderPath,
                        "old name",
                        "book content"
                    );
                    tc.PutBook(oldFolderPath);
                    tc.AttemptLock("old name");
                    SyncAtStartupTests.SimulateRename(tc, "old name", "new name");
                    RobustFile.Delete(tc.GetPathToBookFileInRepo("old name"));

                    Assert.DoesNotThrow(() =>
                        tc.PutBook(Path.Combine(collectionFolder.FolderPath, "new name"), true)
                    );
                    Assert.That(File.Exists(tc.GetPathToBookFileInRepo("new name")), Is.True);
                    Assert.That(
                        File.Exists(tc.GetPathToBookFileInRepo("old name")),
                        Is.False,
                        "old repo file should remain absent"
                    );
                    Assert.That(tc.GetLocalStatus("new name").oldName, Is.Null);
                    TeamCollectionManager.ForceCurrentUserForTests(null);
                }
            }
        }

        [Test]
        public void OkToCheckIn_GivesCorrectResults()
        {
            using (
                var collectionFolder = new TemporaryFolder(
                    "OkToCheckIn_GivesCorrectResults_Collection"
                )
            )
            {
                using (
                    var repoFolder = new TemporaryFolder("OkToCheckIn_GivesCorrectResults_Shared")
                )
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    TeamCollectionManager.ForceCurrentUserForTests("");
                    var tc = new FolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    tc.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
                    var bookFolderPath = SyncAtStartupTests.MakeFakeBook(
                        collectionFolder.FolderPath,
                        "some name",
                        "book content"
                    );
                    Assert.That(
                        tc.OkToCheckIn("some name"),
                        Is.False,
                        "can't check in new book when not registered"
                    );

                    TeamCollectionManager.ForceCurrentUserForTests("fred@somewhere.com");
                    Assert.That(tc.OkToCheckIn("some name"), Is.True, "can check in new book");

                    tc.PutBook(bookFolderPath, true);
                    tc.AttemptLock("some name");
                    Assert.That(
                        tc.OkToCheckIn("some name"),
                        Is.True,
                        "can check in unmodified book with normal checkout status"
                    );

                    TeamCollectionManager.ForceCurrentUserForTests("");
                    Assert.That(
                        tc.OkToCheckIn("some name"),
                        Is.False,
                        "normally permitted checkin is forbidden with no registration"
                    );
                    TeamCollectionManager.ForceCurrentUserForTests("fred@somewhere.com");

                    var status = tc.GetStatus("some name");
                    var altStatus = status.WithChecksum("some random thing");
                    tc.WriteBookStatus("some name", altStatus);
                    tc.WriteLocalStatus("some name", status);
                    Assert.That(
                        tc.OkToCheckIn("some name"),
                        Is.False,
                        "can't check in, mysteriously modified in repo"
                    );

                    altStatus = status.WithLockedBy(null);
                    tc.WriteBookStatus("some name", altStatus);
                    tc.WriteLocalStatus("some name", status);
                    Assert.That(
                        tc.OkToCheckIn("some name"),
                        Is.True,
                        "special case, repo has lost checkout status, but not locked or modified"
                    );

                    altStatus = status.WithLockedBy("fred@somewhere.org");
                    tc.WriteBookStatus("some name", altStatus);
                    tc.WriteLocalStatus("some name", status);
                    Assert.That(tc.OkToCheckIn("some name"), Is.False, "conflicting lock in repo");

                    TeamCollectionManager.ForceCurrentUserForTests("null");
                }
            }
        }

        [Test]
        public void ChangeToFileInOther_RaisesRepoCollectionFilesChanged()
        {
            using (
                var collectionFolder = new TemporaryFolder(
                    "ChangeToFileInOther_RaisesRepoCollectionFilesChanged"
                )
            )
            {
                using (
                    var repoFolder = new TemporaryFolder(
                        "ChangeToFileInOther_RaisesRepoCollectionFilesChanged"
                    )
                )
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    var tc = new TestFolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    var otherPath = CollectionSettings.GetDefaultSettingsFilePath(
                        collectionFolder.FolderPath
                    );
                    Directory.CreateDirectory(Path.GetDirectoryName(otherPath));
                    // this test doesn't need this folder except that StartMonitoring does.
                    Directory.CreateDirectory(Path.Combine(repoFolder.FolderPath, "Books"));
                    File.WriteAllText(otherPath, "This is the initial value");
                    tc.CopyRepoCollectionFilesFromLocal(collectionFolder.FolderPath);

                    var eventWasRaised = false;

                    tc.SetupMonitoringBehavior();
                    ManualResetEvent collectionChangedRaised = new ManualResetEvent(false);
                    EventHandler<EventArgs> monitorFunction = (sender, args) =>
                    {
                        eventWasRaised = true;
                        collectionChangedRaised.Set();
                    };
                    tc.RepoCollectionFilesChanged += monitorFunction;

                    // sut (at least, triggers it and waits for it)
                    Thread.Sleep(10);
                    var otherRepoPath = FolderTeamCollection.GetRepoProjectFilesZipPath(
                        repoFolder.FolderPath
                    );
                    RobustFile.WriteAllText(otherRepoPath, @"This is changed"); // no, not a zip at all

                    var waitSucceeded = collectionChangedRaised.WaitOne(1000);

                    // To avoid messing up other tests, clean up before asserting.
                    tc.RepoCollectionFilesChanged -= monitorFunction;
                    tc.StopMonitoring();
                    tc.Dispose();

                    Assert.That(eventWasRaised, Is.True, "event was not raised");
                }
            }
        }

        [Test]
        public void HandleCollectionSettingsChange_ProducesMessageInLog_AndStatusEvent()
        {
            using (
                var collectionFolder = new TemporaryFolder(
                    "HandleCollectionSettingsChange_ProducesMessageInLog"
                )
            )
            {
                using (
                    var repoFolder = new TemporaryFolder(
                        "HandleCollectionSettingsChange_ProducesMessageInLog"
                    )
                )
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    var tc = new TestFolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    tc.HandleCollectionSettingsChange(new RepoChangeEventArgs());
                    var msg = tc.MessageLog.CurrentNewStuff.First();
                    Assert.That(msg.MessageType, Is.EqualTo(MessageAndMilestoneType.NewStuff));
                    Assert.That(
                        msg.RawEnglishMessageTemplate,
                        Is.EqualTo(
                            "One of your teammates has made changes to the collection settings."
                        )
                    );
                }
            }
        }

        [Test]
        public void ChangeToFileInOther_FromLocal_DoesNothingUnexpected()
        {
            using (
                var collectionFolder = new TemporaryFolder(
                    "ChangeToFileInOther_FromLocal_DoesNothingUnexpected"
                )
            )
            {
                using (
                    var repoFolder = new TemporaryFolder(
                        "ChangeToFileInOther_FromLocal_DoesNothingUnexpected"
                    )
                )
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    var tc = new TestFolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    var otherPath = CollectionSettings.GetDefaultSettingsFilePath(
                        collectionFolder.FolderPath
                    );
                    // this test doesn't need this folder except that StartMonitoring does.
                    Directory.CreateDirectory(Path.Combine(repoFolder.FolderPath, "Books"));
                    File.WriteAllText(otherPath, "This is the initial value");
                    tc.CopyRepoCollectionFilesFromLocal(collectionFolder.FolderPath);

                    var eventWasRaised = false;

                    tc.StartMonitoring();

                    ManualResetEvent collectionChangedRaised = new ManualResetEvent(false);
                    // This action should be invoked (by test code, due to an override handler on the
                    // low-level event handler for the watcher).
                    tc.OnCollectionChangedCalled = () => collectionChangedRaised.Set();
                    EventHandler<EventArgs> monitorFunction = (sender, args) =>
                    {
                        // This should not happen because we should know we're writing locally.
                        eventWasRaised = true;
                        collectionChangedRaised.Set();
                    };
                    tc.RepoCollectionFilesChanged += monitorFunction;

                    // sut (at least, triggers it and waits for it)
                    RobustFile.WriteAllText(otherPath, @"This is changed");
                    tc.CopyRepoCollectionFilesFromLocal(collectionFolder.FolderPath);

                    var waitSucceeded = collectionChangedRaised.WaitOne(1000);

                    // To avoid messing up other tests, clean up before asserting.
                    tc.RepoCollectionFilesChanged -= monitorFunction;
                    tc.StopMonitoring();

                    Assert.That(waitSucceeded, "file change was not detected");
                    Assert.That(eventWasRaised, Is.False, "event was wrongly raised");
                }
            }
        }

        [Test]
        public void GetBadZipFileMessage_InsertsLinkAndFilename()
        {
            using (
                var collectionFolder = new TemporaryFolder(
                    "GetBadZipFileMessage_InsertsLinkAndFilename_Collection"
                )
            )
            {
                using (
                    var repoFolder = new TemporaryFolder(
                        "GetBadZipFileMessage_InsertsLinkAndFilename_Repo"
                    )
                )
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    var tc = new TestFolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    var result = tc.GetBadZipFileMessage("Roses are red& Violets are blue.");
                    Assert.That(
                        result,
                        Is.EqualTo(
                            "There is a problem with the book \"Roses are red& Violets are blue.\" in the Team Collection system. Bloom was not able to open the zip file, which may be corrupted. Please click <a href='/bloom/api/teamCollection/reportBadZip?file="
                                + UrlPathString
                                    .CreateFromUnencodedString(
                                        repoFolder.FolderPath.Replace("\\", "/")
                                    )
                                    .UrlEncoded
                                + "%2fBooks%2fRoses%20are%20red%26%20Violets%20are%20blue..bloom'>here</a> to get help from the Bloom support team."
                        )
                    );
                }
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void HandleNewBook_AddsMessage_IffReallyNew(bool reallyNew)
        {
            using (
                var collectionFolder = new TemporaryFolder(
                    "HandleNewBook_NewBook_AddsMessage_Collection"
                )
            )
            {
                using (
                    var repoFolder = new TemporaryFolder("HandleNewBook_NewBook_AddsMessage_Shared")
                )
                {
                    var bookFolderName1 = "New book";
                    var localBookFolderPath = SyncAtStartupTests.MakeFakeBook(
                        collectionFolder.FolderPath,
                        bookFolderName1,
                        "Something"
                    );
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    var tcLog = new TeamCollectionMessageLog(
                        TeamCollectionManager.GetTcLogPathFromLcPath(collectionFolder.FolderPath)
                    );
                    var tc = new TestFolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath,
                        tcLog
                    );
                    tc.PutBook(localBookFolderPath);
                    if (reallyNew)
                        SIL.IO.RobustIO.DeleteDirectory(localBookFolderPath, true);

                    tc.HandleNewBook(new NewBookEventArgs() { BookFileName = "New book.bloom" });

                    if (reallyNew)
                    {
                        var msg = tcLog.Messages[0];
                        Assert.That(
                            msg.RawEnglishMessageTemplate,
                            Is.EqualTo("A new book called '{0}' was added by a teammate.")
                        );
                    }
                    else
                    {
                        Assert.That(tcLog.Messages.Count, Is.EqualTo(0));
                    }
                }
            }
        }

        [Test]
        public void HandleNewBook_RenamedBook_AddsRenameMessage()
        {
            using (
                var collectionFolder = new TemporaryFolder(
                    "HandleNewBook_RenamedBook_AddsRenameMessage_Collection"
                )
            )
            {
                using (
                    var repoFolder = new TemporaryFolder(
                        "HandleNewBook_RenamedBook_AddsRenameMessage_Shared"
                    )
                )
                {
                    var bookFolderName1 = "Renamed book";
                    var localBookFolderPath = SyncAtStartupTests.MakeFakeBook(
                        collectionFolder.FolderPath,
                        bookFolderName1,
                        "Something"
                    );
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    var tcLog = new TeamCollectionMessageLog(
                        TeamCollectionManager.GetTcLogPathFromLcPath(collectionFolder.FolderPath)
                    );
                    var tc = new TestFolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath,
                        tcLog
                    );
                    tc.PutBook(localBookFolderPath);

                    SIL.IO.RobustIO.MoveDirectory(
                        localBookFolderPath,
                        Path.Combine(collectionFolder.FolderPath, "old name")
                    );
                    // We could rename the book file too, but it doesn't matter for the current SUT

                    tc.HandleNewBook(
                        new NewBookEventArgs() { BookFileName = "Renamed book.bloom" }
                    );

                    var msg = tcLog.Messages[0];
                    Assert.That(
                        msg.RawEnglishMessageTemplate,
                        Is.EqualTo("The book \"{0}\" has been renamed to \"{1}\" by a teammate.")
                    );
                    Assert.That(msg.Param0, Is.EqualTo("old name"));
                    Assert.That(msg.Param1, Is.EqualTo("Renamed book"));
                }
            }
        }

        [Test]
        public void AnyBooksCheckedOutHereByCurrentUser_TrueOnlyForRealCheckouts()
        {
            using (
                var collectionFolder = new TemporaryFolder(
                    "AnyBooksCheckedOutHereByCurrentUser_TrueOnlyForRealCheckouts"
                )
            )
            {
                using (
                    var repoFolder = new TemporaryFolder(
                        "AnyBooksCheckedOutHereByCurrentUser_TrueOnlyForRealCheckouts"
                    )
                )
                {
                    TeamCollectionManager.ForceCurrentUserForTests("test@somewhere.org");
                    var bookFolderName1 = "A very nice book book";
                    var localBookFolderPath = SyncAtStartupTests.MakeFakeBook(
                        collectionFolder.FolderPath,
                        bookFolderName1,
                        "Something"
                    );
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    var tcLog = new TeamCollectionMessageLog(
                        TeamCollectionManager.GetTcLogPathFromLcPath(collectionFolder.FolderPath)
                    );
                    var tc = new TestFolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath,
                        tcLog
                    );

                    SyncAtStartupTests.MakeFakeBook(
                        collectionFolder.FolderPath,
                        "Another nice book",
                        "Something"
                    );

                    Assert.That(tc.AnyBooksCheckedOutHereByCurrentUser, Is.False); // both currently local-only

                    tc.PutBook(localBookFolderPath);
                    Assert.That(tc.AnyBooksCheckedOutHereByCurrentUser, Is.False); // one local-only, one checked in

                    tc.AttemptLock(bookFolderName1, TeamCollectionManager.CurrentUser);
                    Assert.That(tc.AnyBooksCheckedOutHereByCurrentUser, Is.True); // one local-only, one checked out

                    tc.PutBook(localBookFolderPath, checkin: true);
                    tc.AttemptLock(bookFolderName1, "someoneElse.somewhere.org");
                    Assert.That(tc.AnyBooksCheckedOutHereByCurrentUser, Is.False); // one local-only, one checked out but to someone else.
                }
            }
        }

        [Test]
        public void ForgetChanges_HtmlChange_UndoesIt()
        {
            using (
                var collectionFolder = new TemporaryFolder(
                    "ForgetChanges_HtmlChange_UndoesIt_Collection"
                )
            )
            {
                using (
                    var repoFolder = new TemporaryFolder("ForgetChanges_HtmlChange_UndoesIt_Repo")
                )
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    var tc = new TestFolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    var bookFolderPath = Path.Combine(collectionFolder.FolderPath, "My book");
                    Directory.CreateDirectory(bookFolderPath);
                    var bookPath = Path.Combine(bookFolderPath, "My book.htm");
                    RobustFile.WriteAllText(bookPath, "This is just a dummy");
                    tc.PutBook(bookFolderPath);
                    tc.AttemptLock("My book", "fred@nowhere.org");
                    RobustFile.WriteAllText(bookPath, "This is the edited content");

                    var changedFolders = tc.ForgetChangesCheckin("My book");

                    Assert.That(changedFolders.Count, Is.EqualTo(0));
                    Assert.That(
                        RobustFile.ReadAllText(bookPath),
                        Is.EqualTo("This is just a dummy")
                    );
                    Assert.That(tc.GetStatus("My book").lockedBy, Is.Null);
                }
            }
        }

        [Test]
        public void ForgetChanges_HtmlChangeAndRename_UndoesBoth()
        {
            using (
                var collectionFolder = new TemporaryFolder(
                    "ForgetChanges_HtmlChangeAndRename_UndoesBoth_Collection"
                )
            )
            {
                using (
                    var repoFolder = new TemporaryFolder(
                        "ForgetChanges_HtmlChangeAndRename_UndoesBoth_Repo"
                    )
                )
                {
                    var tc = MakeAndRenameBook(
                        collectionFolder,
                        repoFolder,
                        out var bookPath,
                        out var newBookFolderPath
                    );

                    var changedFolders = tc.ForgetChangesCheckin("Renamed book");

                    Assert.That(changedFolders.Count, Is.EqualTo(2));
                    Assert.That(RobustFile.Exists(bookPath));
                    Assert.That(Directory.Exists(newBookFolderPath), Is.False);
                    Assert.That(
                        RobustFile.ReadAllText(bookPath),
                        Is.EqualTo("This is just a dummy")
                    );
                    Assert.That(tc.GetStatus("My book").lockedBy, Is.Null);
                    Assert.That(changedFolders[1], Is.EqualTo(newBookFolderPath));
                    Assert.That(changedFolders[0], Is.EqualTo(Path.GetDirectoryName(bookPath)));
                }
            }
        }

        private static TestFolderTeamCollection MakeAndRenameBook(
            TemporaryFolder collectionFolder,
            TemporaryFolder repoFolder,
            out string bookPath,
            out string newBookFolderPath
        )
        {
            var mockTcManager = new Mock<ITeamCollectionManager>();
            var tc = new TestFolderTeamCollection(
                mockTcManager.Object,
                collectionFolder.FolderPath,
                repoFolder.FolderPath
            );
            var bookFolderPath = Path.Combine(collectionFolder.FolderPath, "My book");
            Directory.CreateDirectory(bookFolderPath);
            bookPath = Path.Combine(bookFolderPath, "My book.htm");
            RobustFile.WriteAllText(bookPath, "This is just a dummy");
            tc.PutBook(bookFolderPath);
            tc.AttemptLock("My book", "fred@nowhere.org");
            RobustFile.WriteAllText(bookPath, "This is the edited content");
            var newBookPath = Path.Combine(bookFolderPath, "Renamed book");
            RobustFile.Move(bookPath, newBookPath);
            newBookFolderPath = Path.Combine(collectionFolder.FolderPath, "Renamed book");
            Directory.Move(bookFolderPath, newBookFolderPath);
            tc.HandleBookRename("My book", "Renamed book");
            return tc;
        }

        [Test]
        public void ForgetChanges_RenameAndReplace_UndoesAndMoves()
        {
            using (
                var collectionFolder = new TemporaryFolder(
                    "ForgetChanges_HtmlChangeAndRename_UndoesBoth_Collection"
                )
            )
            {
                using (
                    var repoFolder = new TemporaryFolder(
                        "ForgetChanges_HtmlChangeAndRename_UndoesBoth_Repo"
                    )
                )
                {
                    var tc = MakeAndRenameBook(
                        collectionFolder,
                        repoFolder,
                        out var bookPath,
                        out var newBookFolderPath
                    );
                    Directory.CreateDirectory(Path.GetDirectoryName(bookPath));
                    RobustFile.WriteAllText(
                        bookPath,
                        "This is some other book created after the rename"
                    );

                    var changedFolders = tc.ForgetChangesCheckin("Renamed book");

                    Assert.That(RobustFile.Exists(bookPath));
                    Assert.That(Directory.Exists(newBookFolderPath), Is.False);
                    Assert.That(
                        RobustFile.ReadAllText(bookPath),
                        Is.EqualTo("This is just a dummy")
                    );
                    Assert.That(tc.GetStatus("My book").lockedBy, Is.Null);
                    Assert.That(changedFolders.Count, Is.EqualTo(3));
                    var movedFolder = changedFolders[2];
                    var movedBookPath = Path.Combine(
                        movedFolder,
                        Path.ChangeExtension(Path.GetFileName(movedFolder), "htm")
                    );
                    Assert.That(
                        RobustFile.ReadAllText(movedBookPath),
                        Is.EqualTo("This is some other book created after the rename")
                    );
                }
            }
        }

        /// <summary>
        /// Sets up a team collection whose repo holds a settings file with the given content, and
        /// runs the given check against it. See BL-16691.
        /// </summary>
        private void WithRepoSettingsFile(
            string testName,
            string settingsFileContent,
            Action<TestFolderTeamCollection, CollectionSettings> check
        )
        {
            using (var collectionFolder = new TemporaryFolder(testName + "_Collection"))
            {
                using (var repoFolder = new TemporaryFolder(testName + "_Repo"))
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    var settings = new CollectionSettings();
                    mockTcManager.Setup(m => m.Settings).Returns(settings);
                    var tc = new TestFolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    var settingsPath = CollectionSettings.GetDefaultSettingsFilePath(
                        collectionFolder.FolderPath
                    );
                    Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                    Directory.CreateDirectory(Path.Combine(repoFolder.FolderPath, "Books"));
                    if (settingsFileContent != null)
                    {
                        File.WriteAllText(settingsPath, settingsFileContent);
                        tc.CopyRepoCollectionFilesFromLocal(collectionFolder.FolderPath);
                    }
                    check(tc, settings);
                }
            }
        }

        [TestCase("False", false)]
        [TestCase("True", true)]
        public void GetAllowCheckoutsFromRepo_ReadsTheRepoCopy(string valueInFile, bool expected)
        {
            WithRepoSettingsFile(
                "GetAllowCheckouts" + valueInFile,
                $"<Collection version=\"0.2\"><AllowCheckouts>{valueInFile}</AllowCheckouts></Collection>",
                (tc, settings) => Assert.That(tc.GetAllowCheckoutsFromRepo(), Is.EqualTo(expected))
            );
        }

        /// <summary>
        /// Reading the repo copy has to cope with what Bloom actually writes, and Bloom writes the
        /// settings file with a UTF-8 BOM. The other tests here write the file themselves without
        /// one, so they would not notice if the BOM reached the XML parser. See BL-16691.
        /// </summary>
        [Test]
        public void GetAllowCheckoutsFromRepo_FileWrittenByBloomWithBom_IsRead()
        {
            using (var collectionFolder = new TemporaryFolder("GetAllowCheckoutsBom_Collection"))
            {
                using (var repoFolder = new TemporaryFolder("GetAllowCheckoutsBom_Repo"))
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    var tc = new TestFolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    Directory.CreateDirectory(Path.Combine(repoFolder.FolderPath, "Books"));
                    var settingsPath = CollectionSettings.GetDefaultSettingsFilePath(
                        collectionFolder.FolderPath
                    );

                    // Write it the way Bloom really does, rather than by hand.
                    var settings = new CollectionSettings(settingsPath) { AllowCheckouts = false };
                    settings.Save();

                    // Sanity check: the whole point of this test is that Bloom emits a BOM.
                    Assert.That(
                        File.ReadAllBytes(settingsPath).Take(3).ToArray(),
                        Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }),
                        "setup failed: expected Bloom to write a UTF-8 BOM"
                    );
                    tc.CopyRepoCollectionFilesFromLocal(collectionFolder.FolderPath);

                    Assert.That(tc.GetAllowCheckoutsFromRepo(), Is.False);
                }
            }
        }

        [Test]
        public void GetAllowCheckoutsFromRepo_ElementMissing_IsTrue()
        {
            WithRepoSettingsFile(
                "GetAllowCheckoutsMissing",
                "<Collection version=\"0.2\"><AllowNewBooks>True</AllowNewBooks></Collection>",
                (tc, settings) => Assert.That(tc.GetAllowCheckoutsFromRepo(), Is.True)
            );
        }

        /// <summary>
        /// If we can't read the repo copy we must leave the setting alone rather than guess.
        /// </summary>
        [Test]
        public void GetAllowCheckoutsFromRepo_NoRepoSettings_IsNull()
        {
            WithRepoSettingsFile(
                "GetAllowCheckoutsNoRepo",
                null,
                (tc, settings) => Assert.That(tc.GetAllowCheckoutsFromRepo(), Is.Null)
            );
        }

        /// <summary>
        /// The point of the whole exercise: an administrator pausing checkouts must take effect
        /// on a machine that is already running, without waiting for a restart. See BL-16691.
        /// </summary>
        [Test]
        public void UpdateAllowCheckoutsFromRepo_RepoSaysPaused_UpdatesLiveSettings()
        {
            WithRepoSettingsFile(
                "UpdateAllowCheckoutsPaused",
                "<Collection version=\"0.2\"><AllowCheckouts>False</AllowCheckouts></Collection>",
                (tc, settings) =>
                {
                    // Sanity check: the running Bloom starts out allowing checkouts, which is what
                    // makes the change below meaningful.
                    Assert.That(
                        settings.AllowCheckouts,
                        Is.True,
                        "setup failed: should have started out allowing checkouts"
                    );

                    tc.UpdateAllowCheckoutsFromRepo();

                    Assert.That(settings.AllowCheckouts, Is.False);
                }
            );
        }

        /// <summary>
        /// The administrator pauses checkouts by editing their own local settings file while Bloom
        /// runs, and Bloom pushes that up to the repo. Their own repo watcher is suppressed while
        /// we write, so without help the machine that made the change is the one machine that
        /// doesn't act on it -- and its stale in-memory "allowed" would be written back over the
        /// change by the next Save(), un-pausing the whole team. See BL-16691.
        /// </summary>
        [Test]
        public void SyncLocalAndRepoCollectionFiles_LocalPausePushedUp_UpdatesLiveSettings()
        {
            using (var collectionFolder = new TemporaryFolder("LocalPausePushedUp_Collection"))
            {
                using (var repoFolder = new TemporaryFolder("LocalPausePushedUp_Repo"))
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    var settings = new CollectionSettings();
                    mockTcManager.Setup(m => m.Settings).Returns(settings);
                    var tc = new TestFolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    Directory.CreateDirectory(Path.Combine(repoFolder.FolderPath, "Books"));
                    var settingsPath = CollectionSettings.GetDefaultSettingsFilePath(
                        collectionFolder.FolderPath
                    );

                    // The administrator's hand-edit: the file says paused...
                    File.WriteAllText(
                        settingsPath,
                        "<Collection version=\"0.2\"><AllowCheckouts>False</AllowCheckouts></Collection>"
                    );
                    // ...while this running Bloom still has the value it loaded at startup.
                    Assert.That(
                        settings.AllowCheckouts,
                        Is.True,
                        "setup failed: the running Bloom should still think checkouts are allowed"
                    );

                    tc.SyncLocalAndRepoCollectionFiles(false);

                    Assert.That(
                        settings.AllowCheckouts,
                        Is.False,
                        "the machine that made the change should be paused too"
                    );
                    Assert.That(
                        tc.GetAllowCheckoutsFromRepo(),
                        Is.False,
                        "and the pause should have reached the repo"
                    );
                }
            }
        }

        /// <summary>
        /// The first-launch gap. An administrator sets a minimum version while a teammate's Bloom is
        /// closed. When that teammate starts up, their own copy of the settings is still yesterday's
        /// -- Bloom does not copy the repository's collection files down until later in startup, well
        /// after the gate has decided whether to open the collection. So the gate has to ask the
        /// repository itself, or the teammate gets the whole session inside a collection they are no
        /// longer allowed in. See BL-16690.
        /// </summary>
        [Test]
        public void IsThisBloomTooOld_OnlyTheRepoKnowsAboutTheRequirement_StillSaysTooOld()
        {
            using (var collectionFolder = new TemporaryFolder("RepoOnlyRequirement_Collection"))
            {
                using (var repoFolder = new TemporaryFolder("RepoOnlyRequirement_Repo"))
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    var settings = new CollectionSettings();
                    mockTcManager.Setup(m => m.Settings).Returns(settings);
                    var tc = new TestFolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    Directory.CreateDirectory(Path.Combine(repoFolder.FolderPath, "Books"));
                    var settingsPath = CollectionSettings.GetDefaultSettingsFilePath(
                        collectionFolder.FolderPath
                    );

                    // The administrator's edit, pushed to the repository...
                    File.WriteAllText(
                        settingsPath,
                        "<Collection version=\"0.2\"><MinimumBloomVersion>99.0</MinimumBloomVersion></Collection>"
                    );
                    tc.CopyRepoCollectionFilesFromLocal(collectionFolder.FolderPath);
                    FolderTeamCollection.CreateTeamCollectionLinkFile(
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );

                    // ...while this teammate's own copy still says nothing about it, exactly as it
                    // would on the morning after the administrator made the change.
                    File.WriteAllText(settingsPath, "<Collection version=\"0.2\"></Collection>");
                    Assert.That(
                        MinimumBloomVersionCheck.ReadMinimumBloomVersion(settingsPath),
                        Is.Empty,
                        "setup failed: the local file should not know about the requirement"
                    );

                    Assert.That(
                        MinimumBloomVersionCheck.IsThisBloomTooOld(
                            settingsPath,
                            out var minimumVersion
                        ),
                        Is.True,
                        "the repository's requirement should be honoured on the very first launch"
                    );
                    Assert.That(minimumVersion, Is.EqualTo("99.0"));
                }
            }
        }

        /// <summary>
        /// A Team Collection whose repository copy cannot be read at all. Used to prove that
        /// picking up the administrator's newly pushed requirement does not depend on reading back
        /// the zip we have just written -- that zip can briefly refuse to open while Dropbox is
        /// syncing it, and losing the value there is what would let the next save erase the
        /// administrator's protection for the whole team. See BL-16690.
        /// </summary>
        private class TeamCollectionWhoseRepoReadFails : TestFolderTeamCollection
        {
            public TeamCollectionWhoseRepoReadFails(
                ITeamCollectionManager tcManager,
                string localCollectionFolder,
                string repoFolderPath
            )
                : base(tcManager, localCollectionFolder, repoFolderPath) { }

            protected override string GetRepoCollectionSettingsContent()
            {
                throw new IOException("pretending the repo zip is mid-sync");
            }
        }

        [Test]
        public void SyncLocalAndRepoCollectionFiles_RepoReadFails_StillRemembersWhatWePushed()
        {
            using (var collectionFolder = new TemporaryFolder("RepoReadFails_Collection"))
            {
                using (var repoFolder = new TemporaryFolder("RepoReadFails_Repo"))
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    var settings = new CollectionSettings();
                    mockTcManager.Setup(m => m.Settings).Returns(settings);
                    var tc = new TeamCollectionWhoseRepoReadFails(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    Directory.CreateDirectory(Path.Combine(repoFolder.FolderPath, "Books"));
                    var settingsPath = CollectionSettings.GetDefaultSettingsFilePath(
                        collectionFolder.FolderPath
                    );

                    File.WriteAllText(
                        settingsPath,
                        "<Collection version=\"0.2\"><MinimumBloomVersion>1.0</MinimumBloomVersion></Collection>"
                    );
                    Assert.That(
                        settings.MinimumBloomVersion,
                        Is.Empty,
                        "setup failed: the running Bloom should not know about it yet"
                    );

                    tc.SyncLocalAndRepoCollectionFiles(false);

                    Assert.That(
                        settings.MinimumBloomVersion,
                        Is.EqualTo("1.0"),
                        "a repo read failure must not lose the requirement we just pushed, or the next save deletes it for everyone"
                    );
                }
            }
        }

        /// <summary>
        /// The administrator must be able to undo a mistake. Once a member is being refused, they
        /// never open the collection, so the startup sync that would refresh their own copy of the
        /// settings never runs -- which means a requirement lifted in the repository has to be
        /// honoured from the repository, or that member is shut out on every launch for ever, with
        /// no way back in from inside Bloom. See BL-16690.
        /// </summary>
        [Test]
        public void IsThisBloomTooOld_RepoHasWithdrawnTheRequirement_LetsThemBackIn()
        {
            using (var collectionFolder = new TemporaryFolder("RepoWithdrewRequirement_Collection"))
            {
                using (var repoFolder = new TemporaryFolder("RepoWithdrewRequirement_Repo"))
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    var settings = new CollectionSettings();
                    mockTcManager.Setup(m => m.Settings).Returns(settings);
                    var tc = new TestFolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    Directory.CreateDirectory(Path.Combine(repoFolder.FolderPath, "Books"));
                    var settingsPath = CollectionSettings.GetDefaultSettingsFilePath(
                        collectionFolder.FolderPath
                    );

                    // The administrator has thought better of it, so the repository asks for nothing...
                    File.WriteAllText(settingsPath, "<Collection version=\"0.2\"></Collection>");
                    tc.CopyRepoCollectionFilesFromLocal(collectionFolder.FolderPath);
                    FolderTeamCollection.CreateTeamCollectionLinkFile(
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );

                    // ...but this member's own copy still carries the requirement they were refused
                    // by, and always will, because being refused is what stops it being refreshed.
                    File.WriteAllText(
                        settingsPath,
                        "<Collection version=\"0.2\"><MinimumBloomVersion>99.0</MinimumBloomVersion></Collection>"
                    );
                    Assert.That(
                        MinimumBloomVersionCheck.ReadMinimumBloomVersion(settingsPath),
                        Is.EqualTo("99.0"),
                        "setup failed: the local file should still carry the old requirement"
                    );

                    Assert.That(
                        MinimumBloomVersionCheck.IsThisBloomTooOld(settingsPath, out _),
                        Is.False,
                        "the administrator lifting the requirement must let the member back in"
                    );
                }
            }
        }

        /// <summary>
        /// The same workflow, and the same trap, for MinimumBloomVersion: the administrator adds it
        /// to their own local settings file while Bloom runs. Their in-memory copy is still empty,
        /// so the next Save() would rewrite the file without it and push that up, erasing the
        /// requirement for the whole team. We deliberately only take the value here -- locking the
        /// administrator out mid-sync is not this method's job. See BL-16690.
        /// </summary>
        [Test]
        public void SyncLocalAndRepoCollectionFiles_LocalMinimumVersionPushedUp_UpdatesLiveSettings()
        {
            using (var collectionFolder = new TemporaryFolder("LocalMinVersionPushedUp_Collection"))
            {
                using (var repoFolder = new TemporaryFolder("LocalMinVersionPushedUp_Repo"))
                {
                    var mockTcManager = new Mock<ITeamCollectionManager>();
                    var settings = new CollectionSettings();
                    mockTcManager.Setup(m => m.Settings).Returns(settings);
                    var tc = new TestFolderTeamCollection(
                        mockTcManager.Object,
                        collectionFolder.FolderPath,
                        repoFolder.FolderPath
                    );
                    Directory.CreateDirectory(Path.Combine(repoFolder.FolderPath, "Books"));
                    var settingsPath = CollectionSettings.GetDefaultSettingsFilePath(
                        collectionFolder.FolderPath
                    );

                    // The administrator's hand-edit. 1.0 is old enough that nothing here will try to
                    // shut them out, which would want a dialog we cannot show from a unit test.
                    File.WriteAllText(
                        settingsPath,
                        "<Collection version=\"0.2\"><MinimumBloomVersion>1.0</MinimumBloomVersion></Collection>"
                    );
                    // ...while this running Bloom still has the value it loaded at startup.
                    Assert.That(
                        settings.MinimumBloomVersion,
                        Is.Empty,
                        "setup failed: the running Bloom should not know about the minimum version yet"
                    );

                    tc.SyncLocalAndRepoCollectionFiles(false);

                    Assert.That(
                        settings.MinimumBloomVersion,
                        Is.EqualTo("1.0"),
                        "the machine that made the change should know about it too, or its next save will erase it"
                    );
                }
            }
        }

        [Test]
        public void UpdateAllowCheckoutsFromRepo_NoRepoSettings_LeavesSettingAlone()
        {
            WithRepoSettingsFile(
                "UpdateAllowCheckoutsNoRepo",
                null,
                (tc, settings) =>
                {
                    settings.AllowCheckouts = false;
                    tc.UpdateAllowCheckoutsFromRepo();
                    Assert.That(settings.AllowCheckouts, Is.False);
                }
            );
        }

        /// <summary>
        /// Picking up the repo's minimum version matters even when this Bloom is new enough to carry
        /// on working. CollectionSettings.Save() rebuilds the file from memory, so if we were still
        /// holding the empty value we loaded at startup, the next ordinary save would drop the
        /// element and the Team Collection would push that up -- removing the administrator's
        /// protection for everybody. See BL-16690.
        /// </summary>
        [Test]
        public void HandleCollectionSettingsChange_RepoDeclaresAMinimumWeMeet_RemembersIt()
        {
            WithRepoSettingsFile(
                "RememberMinimumVersion",
                "<Collection version=\"0.2\"><MinimumBloomVersion>1.0</MinimumBloomVersion></Collection>",
                (tc, settings) =>
                {
                    // Sanity check: we must start out not knowing about it, or the test proves nothing.
                    Assert.That(
                        settings.MinimumBloomVersion,
                        Is.Empty,
                        "setup failed: should have started with no minimum version"
                    );
                    // And this Bloom really must satisfy the 1.0 below. On a build where the
                    // version was never stamped in (0.0.x) it would not, and the code under test
                    // would try to lock the user out -- which from a unit test means a dialog and a
                    // hang rather than a failure. Better to say so plainly here.
                    Assert.That(
                        typeof(CollectionSettings).Assembly.GetName().Version,
                        Is.GreaterThanOrEqualTo(new Version(1, 0)),
                        "setup failed: this Bloom's assembly version is not stamped, so the test cannot tell a met minimum from an unmet one"
                    );

                    // 1.0 is old enough that this cannot try to lock anyone out (which would want a dialog).
                    var lockedOut = tc.HandleCollectionSettingsChange(new RepoChangeEventArgs());

                    Assert.That(
                        lockedOut,
                        Is.False,
                        "should not shut anyone out over a minimum this Bloom easily meets"
                    );
                    Assert.That(settings.MinimumBloomVersion, Is.EqualTo("1.0"));
                }
            );
        }

        /// <summary>
        /// Not being able to read the repo copy is quite different from the repo saying there is no
        /// minimum, and only the second should clear what we are holding. Getting this wrong would
        /// turn a transient read failure into the loss of the setting. See BL-16690.
        /// </summary>
        [Test]
        public void HandleCollectionSettingsChange_NoRepoSettings_LeavesMinimumVersionAlone()
        {
            WithRepoSettingsFile(
                "RememberMinimumVersionNoRepo",
                null,
                (tc, settings) =>
                {
                    settings.MinimumBloomVersion = "1.0";

                    tc.HandleCollectionSettingsChange(new RepoChangeEventArgs());

                    Assert.That(settings.MinimumBloomVersion, Is.EqualTo("1.0"));
                }
            );
        }
    }
}
