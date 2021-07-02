﻿using Bloom;
using Bloom.TeamCollection;
using Bloom.web;
using BloomTemp;
using Moq;
using NUnit.Framework;
using SIL.IO;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using SIL.Reporting;

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
			using (var collectionFolder = new TemporaryFolder("FolderTeamCollectionTests2_Collection"))
			{
				using (var sharedFolder = new TemporaryFolder("FolderTeamCollectionTests2_Shared"))
				{
					var bookFolderName1 = "Some book";
					SyncAtStartupTests.MakeFakeBook(collectionFolder.FolderPath, bookFolderName1, "Something");
					// BL-9573 tests cases where the book name isn't exactly the same as the folder name
					var bookFolderName2 = "Some other book";
					SyncAtStartupTests.MakeFakeBook(collectionFolder.FolderPath, "Some other name altogether",
						"Strange book content", bookFolderName2);
					var settingsFileName =
						Path.ChangeExtension(Path.GetFileName(collectionFolder.FolderPath), "bloomCollection");
					var settingsPath = Path.Combine(collectionFolder.FolderPath, settingsFileName);

					// As an aside, this is a convenient place to check that a TC manager created when TC settings does not exist
					// functions and does not have a current collection.
					var tcManager = new TeamCollectionManager(settingsPath, null, new BookRenamedEvent(), new BookStatusChangeEvent(), null, null);
					Assert.That(tcManager.CurrentCollection, Is.Null);

					RobustFile.WriteAllText(settingsPath, "This is a fake settings file");
					FolderTeamCollection.CreateTeamCollectionLinkFile(collectionFolder.FolderPath,
						sharedFolder.FolderPath);

					var nonBookFolder = Path.Combine(collectionFolder.FolderPath, "Some other folder");
					Directory.CreateDirectory(nonBookFolder);
					tcManager = new TeamCollectionManager(settingsPath, null, new BookRenamedEvent(), new BookStatusChangeEvent(), null, null);
					var collection = tcManager.CurrentCollection;

					// sut
					(collection as FolderTeamCollection)?.SetupTeamCollection(sharedFolder.FolderPath, new NullWebSocketProgress());

					Assert.That(collection, Is.Not.Null);
					var joinCollectionPath =
						Path.Combine(sharedFolder.FolderPath, "Join this Team Collection.JoinBloomTC");
					Assert.That(File.Exists(joinCollectionPath));

					var teamCollectionLinkPath =
						Path.Combine(collectionFolder.FolderPath, TeamCollectionManager.TeamCollectionLinkFileName);
					Assert.That(File.Exists(teamCollectionLinkPath));
					var collectionFileContent = RobustFile.ReadAllText(teamCollectionLinkPath);
					Assert.That(collectionFileContent, Is.EqualTo(sharedFolder.FolderPath));
					var sharedSettingsPath = Path.Combine(collectionFolder.FolderPath, settingsFileName);
					Assert.That(RobustFile.ReadAllText(sharedSettingsPath), Is.EqualTo("This is a fake settings file"));
					var bookPath = Path.Combine(sharedFolder.FolderPath, "Books", bookFolderName1 + ".bloom");
					Assert.That(File.Exists(bookPath));
					var bookPath2 = Path.Combine(sharedFolder.FolderPath, "Books", bookFolderName2 + ".bloom");
					Assert.That(File.Exists(bookPath2));
				}
			}

		}

		[Test]
		public void FilesToMonitorForCollection_NonStandardCollectionFileName_FindsIt()
		{
			using (var collectionFolder =
				new TemporaryFolder("SyncLocalAndRepoCollectionFiles_SyncsInRightDirection_Collection"))
			{
				using (var repoFolder =
					new TemporaryFolder("SyncLocalAndRepoCollectionFiles_SyncsInRightDirection_Shared"))
				{
					var mockTcManager = new Mock<ITeamCollectionManager>();
					var tc = new FolderTeamCollection(mockTcManager.Object, collectionFolder.FolderPath, repoFolder.FolderPath);
					tc.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
					var bcPath = Path.Combine(collectionFolder.FolderPath, "mybooks.bloomCollection");
					File.WriteAllText(bcPath, "something");
					var files = tc.FilesToMonitorForCollection();
					Assert.That(files, Contains.Item(bcPath));
				}
			}
		}

		[Test]
		public void SyncLocalAndRepoCollectionFiles_SyncsInRightDirection()
		{
			using (var collectionFolder =
				new TemporaryFolder("SyncLocalAndRepoCollectionFiles_SyncsInRightDirection_Collection"))
			{
				using (var repoFolder =
					new TemporaryFolder("SyncLocalAndRepoCollectionFiles_SyncsInRightDirection_Shared"))
				{
					var settingsFileName =
						Path.ChangeExtension(Path.GetFileName(collectionFolder.FolderPath), "bloomCollection");
					var settingsPath = Path.Combine(collectionFolder.FolderPath, settingsFileName);
					var tcManager = new TeamCollectionManager(settingsPath, null, new BookRenamedEvent(), new BookStatusChangeEvent(), null, null);
					var tc = new FolderTeamCollection(tcManager, collectionFolder.FolderPath, repoFolder.FolderPath);
					tc.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
					var bloomCollectionPath = Bloom.TeamCollection.TeamCollection.CollectionPath(collectionFolder.FolderPath);
					Assert.That(tc.LocalCollectionFilesRecordedSyncTime, Is.EqualTo(DateTime.MinValue));
					File.WriteAllText(bloomCollectionPath, "This is a fake collection file");
					var collectionStylesPath = Path.Combine(collectionFolder.FolderPath, "customCollectionStyles.css");
					RobustFile.WriteAllText(collectionStylesPath, "This is the collection styles");

					// SUT 1: nothing in repo, no sync time file. Copies to repo.
					tc.SyncLocalAndRepoCollectionFiles();

					var localWriteTime1 = tc.LocalCollectionFilesRecordedSyncTime();
					Assert.That(localWriteTime1, Is.LessThanOrEqualTo(DateTime.Now));
					Assert.That(localWriteTime1, Is.GreaterThan(DateTime.Now.Subtract(new TimeSpan(0, 0, 5, 0))));
					var otherFilesPath = FolderTeamCollection.GetRepoProjectFilesZipPath(repoFolder.FolderPath);
					Assert.That(File.Exists(otherFilesPath));
					var anotherPlace = Path.Combine(repoFolder.FolderPath, "anotherPlace.zip");
					RobustFile.Copy(otherFilesPath, anotherPlace);
					var repoWriteTime1 = new FileInfo(otherFilesPath).LastWriteTime;
					var collectionWriteTime1 = new FileInfo(bloomCollectionPath).LastWriteTime;

					// SUT 2: nothing has changed. But it's a startup, so sync still happens to local.
					tc.SyncLocalAndRepoCollectionFiles();
					var localWriteTime2 = tc.LocalCollectionFilesRecordedSyncTime();
					Assert.That(localWriteTime2, Is.GreaterThanOrEqualTo(localWriteTime1));
					Assert.That(new FileInfo(otherFilesPath).LastWriteTime, Is.EqualTo(repoWriteTime1));
					Assert.That(new FileInfo(bloomCollectionPath).LastWriteTime, Is.GreaterThanOrEqualTo(collectionWriteTime1));

					File.WriteAllText(bloomCollectionPath, "This is a modified fake collection file");
					var collectionWriteTime2 = new FileInfo(bloomCollectionPath).LastWriteTime;

					// SUT 3: local change copied to repo (only when not at startup)
					tc.SyncLocalAndRepoCollectionFiles(false);
					var localWriteTime3 = tc.LocalCollectionFilesRecordedSyncTime();
					Assert.That(localWriteTime3, Is.GreaterThan(localWriteTime1), "localWriteTime3 should be greater than localWriteTime1");
					var repoWriteTime2 = new FileInfo(otherFilesPath).LastWriteTime;
					Assert.That(repoWriteTime2, Is.GreaterThan(repoWriteTime1), "repoWriteTime2 should be greater than repoWriteTime1");
					// not modified by sync
					Assert.That(new FileInfo(bloomCollectionPath).LastWriteTime, Is.EqualTo(collectionWriteTime2));

					File.WriteAllText(bloomCollectionPath, "This is a further modified fake collection file");
					var collectionWriteTime3 = new FileInfo(bloomCollectionPath).LastWriteTime;
					var version2Path = Path.Combine(repoFolder.FolderPath, "version2.zip");
					RobustFile.Copy(otherFilesPath, version2Path);
					// modify the remote version by copying the old one back.
					Thread.Sleep(10);
					RobustFile.Copy(anotherPlace, otherFilesPath, true);
					var repoWriteTime3 = new FileInfo(otherFilesPath).LastWriteTime;
					Assert.That(repoWriteTime3, Is.GreaterThan(collectionWriteTime3), "repo file written after local collection file [sanity check]");

					// SUT 4: both changed: repo wins
					tc.SyncLocalAndRepoCollectionFiles();
					var localWriteTime4 = tc.LocalCollectionFilesRecordedSyncTime();
					Assert.That(localWriteTime4, Is.GreaterThan(localWriteTime3), "localWriteTime4 should be greater than localWriteTime3");
					var repoWriteTime4 = new FileInfo(otherFilesPath).LastWriteTime;
					Assert.That(repoWriteTime4, Is.EqualTo(repoWriteTime3)); // not modified by sync
					Assert.That(new FileInfo(bloomCollectionPath).LastWriteTime, Is.GreaterThan(collectionWriteTime3), "bloomCollection LastWriteTime should be greater than collectionWriteTime3");
					// We got the original back.
					Assert.That(File.ReadAllText(bloomCollectionPath), Is.EqualTo("This is a fake collection file"));
					
					Thread.Sleep(10);
					var allowedWords = Path.Combine(collectionFolder.FolderPath, "Allowed Words");
					Directory.CreateDirectory(allowedWords);
					File.WriteAllText(Path.Combine(allowedWords, "file1.txt"), "fake word list");

					// SUT5: local allowed words added
					tc.SyncLocalAndRepoCollectionFiles(false);
					var localWriteTime5 = tc.LocalCollectionFilesRecordedSyncTime();
					Assert.That(localWriteTime5, Is.GreaterThan(localWriteTime4), "localWriteTime5 should be greater than localWriteTime4");
					var repoWriteTime5 = new FileInfo(otherFilesPath).LastWriteTime;
					Assert.That(repoWriteTime5, Is.GreaterThan(repoWriteTime4), "repoWriteTime5 should be greater than repoWriteTime4");
					
					Thread.Sleep(5);
					var sampleTexts = Path.Combine(collectionFolder.FolderPath, "Sample Texts");
					Directory.CreateDirectory(sampleTexts);
					File.WriteAllText(Path.Combine(allowedWords, "sample1.txt"), "fake sample list");

					// SUT6: local sample texts added
					tc.SyncLocalAndRepoCollectionFiles(false);
					var localWriteTime6 = tc.LocalCollectionFilesRecordedSyncTime();
					Assert.That(localWriteTime6, Is.GreaterThan(localWriteTime5), "localWriteTime6 should be greater than localWriteTime5");
					var repoWriteTime6 = new FileInfo(otherFilesPath).LastWriteTime;
					Assert.That(repoWriteTime6, Is.GreaterThan(repoWriteTime5), "repoWriteTime6 should be greater than repoWriteTime5");
					
					Thread.Sleep(10);
					File.WriteAllText(Path.Combine(allowedWords, "sample1.txt"), "fake sample list");

					// SUT7: local file write time modified, but not actually changed. Want the sync time to
					// update, but NOT to write the remote file.
					tc.SyncLocalAndRepoCollectionFiles(false);
					var localWriteTime7 = tc.LocalCollectionFilesRecordedSyncTime();
					Assert.That(localWriteTime7, Is.GreaterThan(localWriteTime6), "localWriteTime7 should be greater than localWriteTime6");
					var repoWriteTime7 = new FileInfo(otherFilesPath).LastWriteTime;
					Assert.That(repoWriteTime7, Is.EqualTo(repoWriteTime6));

					tc._haveShownRemoteSettingsChangeWarning = false;
					File.WriteAllText(bloomCollectionPath, "This is a modified fake collection file, for SUT 8");
					var collectionWriteTimeBeforeSut8 = new FileInfo(bloomCollectionPath).LastWriteTime;
					var localWriteTimeBeforeSut8 = tc.LocalCollectionFilesRecordedSyncTime();
					var repoWriteTimeBeforeSut8 = new FileInfo(otherFilesPath).LastWriteTime;

					// SUT 8: local change copied to repo on idle
					tc.SyncLocalAndRepoCollectionFiles(false);
					Assert.That(tc._haveShownRemoteSettingsChangeWarning, Is.False, "user should not have been warned");
					var localWriteTimeAfterSut8 = tc.LocalCollectionFilesRecordedSyncTime();
					Assert.That(localWriteTimeAfterSut8, Is.GreaterThan(localWriteTimeBeforeSut8), "localWriteTime should increase copying on idle");
					var repoWriteTimeAfterSut8 = new FileInfo(otherFilesPath).LastWriteTime;
					Assert.That(repoWriteTimeAfterSut8, Is.GreaterThan(repoWriteTimeBeforeSut8), "repoWriteTime should increase copying on idle");
					// not modified by sync
					Assert.That(new FileInfo(bloomCollectionPath).LastWriteTime, Is.EqualTo(collectionWriteTimeBeforeSut8));

					// modify the remote version by copying version2 back.
					Thread.Sleep(10);
					var repoWriteTimeBeforeSut9Copy = new FileInfo(otherFilesPath).LastWriteTime;
					RobustFile.Copy(version2Path, otherFilesPath, true);
					var collectionWriteTimeBeforeSut9 = new FileInfo(bloomCollectionPath).LastWriteTime;
					var repoWriteTimeBeforeSut9 = new FileInfo(otherFilesPath).LastWriteTime;
					Assert.That(repoWriteTimeBeforeSut9, Is.GreaterThan(repoWriteTimeBeforeSut9Copy), "repo file written after local collection file [sanity check]");
					tc._haveShownRemoteSettingsChangeWarning = false;

					// SUT9: repo modified, doing check on idle. No changes or warning.
					tc.SyncLocalAndRepoCollectionFiles(false);
					Assert.That(tc._haveShownRemoteSettingsChangeWarning, Is.False, "user should not have been warned");
					var collectionWriteTimeAfterSut9 = new FileInfo(bloomCollectionPath).LastWriteTime;
					Assert.That(collectionWriteTimeAfterSut9, Is.EqualTo(collectionWriteTimeBeforeSut9), "local settings should not have been modified");

					File.WriteAllText(bloomCollectionPath, "This is a modified fake collection file, for SUT 10");
					var collectionWriteTimeBeforeSut10 = new FileInfo(bloomCollectionPath).LastWriteTime;
					var localWriteTimeBeforeSut10 = tc.LocalCollectionFilesRecordedSyncTime();
					var repoWriteTimeBeforeSut10 = new FileInfo(otherFilesPath).LastWriteTime;

					// SUT10: both modified, doing check on idle. No changes. User warned.
					using (var nfes = new ErrorReport.NonFatalErrorReportExpected())
					{
						tc.SyncLocalAndRepoCollectionFiles(false);
					}

					Assert.That(tc._haveShownRemoteSettingsChangeWarning, Is.True, "user should have been warned");
					var localWriteTimeAfterSut10 = tc.LocalCollectionFilesRecordedSyncTime();
					Assert.That(localWriteTimeAfterSut10, Is.EqualTo(localWriteTimeBeforeSut10), "localWriteTime should not be changed by idle sync where both changed");
					var repoWriteTimeAfterSut10 = new FileInfo(otherFilesPath).LastWriteTime;
					Assert.That(repoWriteTimeAfterSut10, Is.EqualTo(repoWriteTimeBeforeSut10), "repo should not be modified by idle sync where both changed"); // not modified by sync
					Assert.That(new FileInfo(bloomCollectionPath).LastWriteTime, Is.EqualTo(collectionWriteTimeBeforeSut10),
						"bloomCollection LastWriteTime should not be changed by idle sync both changed");

					// Get everything back in sync
					tc.SyncLocalAndRepoCollectionFiles();
					var localWriteTimeBeforeSut11 = tc.LocalCollectionFilesRecordedSyncTime();
					var repoWriteTimeBeforeSut11 = new FileInfo(otherFilesPath).LastWriteTime;
					RobustFile.WriteAllText(collectionStylesPath, "This is the modified collection styles");

					// SUT11: custom collection styles modified while Bloom was not running. Copied to repo.
					tc.SyncLocalAndRepoCollectionFiles();
					var repoWriteTimeAfterSut11 = new FileInfo(otherFilesPath).LastWriteTime;
					Assert.That(repoWriteTimeAfterSut11, Is.GreaterThanOrEqualTo(repoWriteTimeBeforeSut11));
					var localWriteTimeAfterSut11 = tc.LocalCollectionFilesRecordedSyncTime();
					// We will update the sync time even though the write is the other way.
					Assert.That(localWriteTimeAfterSut11, Is.GreaterThan(localWriteTimeBeforeSut11));
					Assert.That(File.ReadAllText(collectionStylesPath), Is.EqualTo("This is the modified collection styles"));
				}
			}
		}

		[Test]
		public void Checkin_RenamedBook_DeletesOriginal()
		{
			using (var collectionFolder =
				new TemporaryFolder("Checkin_RenamedBook_DeletesOriginal_Collection"))
			{
				using (var repoFolder =
					new TemporaryFolder("Checkin_RenamedBook_DeletesOriginal_Shared"))
				{
					var mockTcManager = new Mock<ITeamCollectionManager>();
					TeamCollectionManager.ForceCurrentUserForTests("me@somewhere.org");
					var tc = new FolderTeamCollection(mockTcManager.Object, collectionFolder.FolderPath, repoFolder.FolderPath);
					tc.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
					var oldFolderPath = SyncAtStartupTests.MakeFakeBook(collectionFolder.FolderPath, "old name", "book content");
					tc.PutBook(oldFolderPath);
					tc.AttemptLock("old name");
					SyncAtStartupTests.SimulateRename(tc, "old name", "middle name");
					SyncAtStartupTests.SimulateRename(tc,  "middle name", "new name");
					tc.PutBook(Path.Combine(collectionFolder.FolderPath,"new name"), true);
					Assert.That(File.Exists(tc.GetPathToBookFileInRepo("new name")),Is.True);
					Assert.That(File.Exists(tc.GetPathToBookFileInRepo("old name")), Is.False, "old name was not deleted");
					var status = tc.GetLocalStatus("new name");
					Assert.That(status.oldName??"", Is.Empty, "Should stop tracking previous name once we cleaned it up");
					TeamCollectionManager.ForceCurrentUserForTests(null);
				}
			}
		}

		[Test]
		public void OkToCheckIn_GivesCorrectResults()
		{
			using (var collectionFolder =
				new TemporaryFolder("OkToCheckIn_GivesCorrectResults_Collection"))
			{
				using (var repoFolder =
					new TemporaryFolder("OkToCheckIn_GivesCorrectResults_Shared"))
				{
					var mockTcManager = new Mock<ITeamCollectionManager>();
					TeamCollectionManager.ForceCurrentUserForTests("");
					var tc = new FolderTeamCollection(mockTcManager.Object, collectionFolder.FolderPath, repoFolder.FolderPath);
					tc.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
					var bookFolderPath =
						SyncAtStartupTests.MakeFakeBook(collectionFolder.FolderPath, "some name", "book content");
					Assert.That(tc.OkToCheckIn("some name"), Is.False, "can't check in new book when not registered");

					TeamCollectionManager.ForceCurrentUserForTests("fred@somewhere.com");
					Assert.That(tc.OkToCheckIn("some name"), Is.True, "can check in new book");

					tc.PutBook(bookFolderPath, true);
					tc.AttemptLock("some name");
					Assert.That(tc.OkToCheckIn("some name"), Is.True, "can check in unmodified book with normal checkout status");

					TeamCollectionManager.ForceCurrentUserForTests("");
					Assert.That(tc.OkToCheckIn("some name"), Is.False, "normally permitted checkin is forbidden with no registration");
					TeamCollectionManager.ForceCurrentUserForTests("fred@somewhere.com");

					var status = tc.GetStatus("some name");
					var altStatus = status.WithChecksum("some random thing");
					tc.WriteBookStatus("some name", altStatus);
					tc.WriteLocalStatus("some name", status);
					Assert.That(tc.OkToCheckIn("some name"), Is.False, "can't check in, mysteriously modified in repo");

					altStatus = status.WithLockedBy(null);
					tc.WriteBookStatus("some name", altStatus);
					tc.WriteLocalStatus("some name", status);
					Assert.That(tc.OkToCheckIn("some name"), Is.True, "special case, repo has lost checkout status, but not locked or modified");

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
			using (var collectionFolder =
				new TemporaryFolder("ChangeToFileInOther_RaisesRepoCollectionFilesChanged"))
			{
				using (var repoFolder =
					new TemporaryFolder("ChangeToFileInOther_RaisesRepoCollectionFilesChanged"))
				{
					var mockTcManager = new Mock<ITeamCollectionManager>();
					var tc = new TestFolderTeamCollection(mockTcManager.Object, collectionFolder.FolderPath,
						repoFolder.FolderPath);
					var otherPath = Path.Combine(collectionFolder.FolderPath, Path.GetFileName(collectionFolder.FolderPath) + ".bloomCollection");
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
					var otherRepoPath = FolderTeamCollection.GetRepoProjectFilesZipPath(repoFolder.FolderPath);
					RobustFile.WriteAllText(otherRepoPath, @"This is changed"); // no, not a zip at all

					var waitSucceeded = collectionChangedRaised.WaitOne(1000);

					// To avoid messing up other tests, clean up before asserting.
					tc.RepoCollectionFilesChanged -= monitorFunction;
					tc.StopMonitoring();

					Assert.That(eventWasRaised, Is.True, "event was not raised");
				}
			}
		}

		[Test]
		public void HandleCollectionSettingsChange_ProducesMessageInLog_AndStatusEvent()
		{
			using (var collectionFolder =
				new TemporaryFolder("HandleCollectionSettingsChange_ProducesMessageInLog"))
			{
				using (var repoFolder =
					new TemporaryFolder("HandleCollectionSettingsChange_ProducesMessageInLog"))
				{
					var mockTcManager = new Mock<ITeamCollectionManager>();
					var tc = new TestFolderTeamCollection(mockTcManager.Object, collectionFolder.FolderPath,
						repoFolder.FolderPath);
					tc.HandleCollectionSettingsChange(new RepoChangeEventArgs());
					var msg = tc.MessageLog.CurrentNewStuff.First();
					Assert.That(msg.MessageType, Is.EqualTo(MessageAndMilestoneType.NewStuff));
					Assert.That(msg.RawEnglishMessageTemplate, Is.EqualTo("One of your teammates has made changes to the collection settings."));
				}
			}
		}

		[Test]
		public void ChangeToFileInOther_FromLocal_DoesNothingUnexpected()
		{
			using (var collectionFolder =
				new TemporaryFolder("ChangeToFileInOther_FromLocal_DoesNothingUnexpected"))
			{
				using (var repoFolder =
					new TemporaryFolder("ChangeToFileInOther_FromLocal_DoesNothingUnexpected"))
				{
					var mockTcManager = new Mock<ITeamCollectionManager>();
					var tc = new TestFolderTeamCollection(mockTcManager.Object, collectionFolder.FolderPath,
						repoFolder.FolderPath);
					var otherPath = Path.Combine(collectionFolder.FolderPath, Path.GetFileName(collectionFolder.FolderPath) + ".bloomCollection");
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
			using (var collectionFolder =
				new TemporaryFolder("ChangeToFileInOther_FromLocal_DoesNothingUnexpected"))
			{
				using (var repoFolder =
					new TemporaryFolder("ChangeToFileInOther_FromLocal_DoesNothingUnexpected"))
				{
					var mockTcManager = new Mock<ITeamCollectionManager>();
					var tc = new TestFolderTeamCollection(mockTcManager.Object, collectionFolder.FolderPath,
						repoFolder.FolderPath);
					var result = tc.GetBadZipFileMessage("Roses are red. Violets are blue.");
					Assert.That(result,
						Is.EqualTo(
							"There is a problem with the book \"Roses are red. Violets are blue.\" in the Team Collection system. Please click <a href='/bloom/api/teamCollection/reportBadZip?file="
							+ UrlPathString.CreateFromUnencodedString(repoFolder.FolderPath).UrlEncoded
							+ "%5cBooks%5cRoses%20are%20red.%20Violets%20are%20blue..bloom'>here</a> to get help from the Bloom support team."));
				}
			}
		}
	}
}
