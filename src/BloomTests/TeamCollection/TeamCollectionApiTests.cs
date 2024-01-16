using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.TeamCollection;
using Bloom.web;
using BloomTemp;
using BloomTests.DataBuilders;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace BloomTests.TeamCollection
{
    /// <summary>
    /// This class sets up a local team collection folder (on the filesystem) and TeamCollectionAPI
    /// before starting the tests.
    /// </summary>
    public class TeamCollectionApiTests
    {
        private TeamCollectionApi _api;
        private TemporaryFolder _localCollection;
        private TeamCollectionManager _tcManager;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _localCollection = new TemporaryFolder("TeamCollectionApiTests");
            var collectionPath = Path.Combine(
                _localCollection.FolderPath,
                Path.ChangeExtension(
                    Path.GetFileName(_localCollection.FolderPath),
                    ".bloomCollection"
                )
            );
            _tcManager = new TeamCollectionManager(
                collectionPath,
                new BloomWebSocketServer(),
                new BookRenamedEvent(),
                null,
                null,
                null,
                null
            );
            _api = new TeamCollectionApi(
                new CurrentEditableCollectionSelection(),
                new CollectionSettings(collectionPath),
                new BookSelection(),
                _tcManager,
                null,
                null,
                null
            );
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _localCollection.Dispose();
        }

        // a hack for making a settings with a specified name
        class ControlledNameSettings : CollectionSettings
        {
            public ControlledNameSettings(string name)
                : base()
            {
                CollectionName = name;
            }
        }

        [Test]
        public void ProblemsWithLocation_NoProblem_Succeeds()
        {
            using (var tempFolder = new TemporaryFolder(MethodBase.GetCurrentMethod().Name))
            {
                var settings = new ControlledNameSettings("SomeCollection");
                _tcManager.Settings = settings;
                Assert.That(_api.ProblemsWithLocation(tempFolder.FolderPath), Is.EqualTo(""));
            }
        }

        [Test]
        public void ProblemsWithLocation_ExistingTC_Fails()
        {
            using (var tempFolder = new TemporaryFolder(MethodBase.GetCurrentMethod().Name))
            {
                File.WriteAllText(
                    Path.Combine(tempFolder.FolderPath, "Join this Team Collection.JoinBloomTC"),
                    "some random content"
                );
                Assert.That(
                    _api.ProblemsWithLocation(tempFolder.FolderPath),
                    Is.EqualTo("There is a problem with this location")
                );
            }
        }

        [Test]
        public void ProblemsWithLocation_BloomCollection_Fails()
        {
            using (var tempFolder = new TemporaryFolder(MethodBase.GetCurrentMethod().Name))
            {
                File.WriteAllText(
                    Path.Combine(tempFolder.FolderPath, "something.bloomCollection"),
                    "some random content"
                );
                Assert.That(
                    _api.ProblemsWithLocation(tempFolder.FolderPath),
                    Is.EqualTo("There is a problem with this location")
                );
            }
        }

        [Test]
        public void ProblemsWithLocation_TCExists_Fails()
        {
            using (var tempFolder = new TemporaryFolder(MethodBase.GetCurrentMethod().Name))
            {
                var settings = new ControlledNameSettings("SomeCollection");
                _tcManager.Settings = settings;
                Directory.CreateDirectory(
                    Path.Combine(tempFolder.FolderPath, "SomeCollection - TC")
                );
                Assert.That(
                    _api.ProblemsWithLocation(tempFolder.FolderPath),
                    Is.EqualTo("There is a problem with this location")
                );
            }
        }

        [Test]
        public void MakeLockFailedMessageFromException_CannotLockException_AgentDropbox_MakesExpectedMessage()
        {
            var ex = new FolderTeamCollection.CannotLockException("We can't do it")
            {
                SyncAgent = "Dropbox"
            };
            var msg = _api.MakeLockFailedMessageFromException(ex, "c:/nowhere/My book");
            Assert.That(
                msg.TextForDisplay,
                Does.Contain(
                    "Bloom was not able to check out \"My book\". Some other program may be busy with it. This may just be Dropbox synchronizing the file. Please try again later. If the problem continues, restart your computer."
                )
            );
        }

        [Test]
        public void MakeLockFailedMessageFromException_CannotLockException_AgentUnknown_MakesExpectedMessage()
        {
            var ex = new FolderTeamCollection.CannotLockException("We can't do it")
            {
                SyncAgent = "Unknown"
            };
            var msg = _api.MakeLockFailedMessageFromException(ex, "c:/nowhere/Some book");
            Assert.That(
                msg.TextForDisplay,
                Does.Contain(
                    "Bloom was not able to check out \"Some book\". Some other program may be busy with it. This may just be something synchronizing the file. Please try again later. If the problem continues, restart your computer."
                )
            );
        }

        [Test]
        public void MakeLockFailedMessageFromException_OtherException_MakesExpectedMessage()
        {
            var ex = new ArgumentException("We can't do it");
            var msg = _api.MakeLockFailedMessageFromException(ex, "c:/nowhere/Other book");
            Assert.That(
                msg.TextForDisplay,
                Does.Contain("Bloom was not able to check out \"Other book\".")
            );
        }

        [Test]
        public void ProblemsWithLocation_FolderReadOnly_Fails()
        {
            using (var tempFolder = new TemporaryFolder(MethodBase.GetCurrentMethod().Name))
            {
                if (SIL.PlatformUtilities.Platform.IsWindows)
                {
                    var di = new DirectoryInfo(tempFolder.FolderPath);
                    var directorySecurity = di.GetAccessControl();
                    var currentUserIdentity = WindowsIdentity.GetCurrent();
                    var fileSystemRule = new FileSystemAccessRule(
                        currentUserIdentity.Name,
                        FileSystemRights.Write,
                        InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit,
                        PropagationFlags.None,
                        AccessControlType.Deny
                    );

                    directorySecurity.AddAccessRule(fileSystemRule);
                    di.SetAccessControl(directorySecurity);
                    Assert.That(
                        _api.ProblemsWithLocation(tempFolder.FolderPath),
                        Contains.Substring(
                            "Bloom does not have permission to write to the selected folder"
                        )
                    );
                    directorySecurity.RemoveAccessRule(fileSystemRule);
                    di.SetAccessControl(directorySecurity);
                }
                else
                {
                    var udi = new Mono.Unix.UnixDirectoryInfo(tempFolder.FolderPath);
                    var permissions = udi.FileAccessPermissions;
                    udi.FileAccessPermissions =
                        Mono.Unix.FileAccessPermissions.UserRead
                        | Mono.Unix.FileAccessPermissions.GroupRead
                        | Mono.Unix.FileAccessPermissions.OtherRead;
                    Assert.That(
                        _api.ProblemsWithLocation(tempFolder.FolderPath),
                        Contains.Substring(
                            "Bloom does not have permission to write to the selected folder"
                        )
                    );
                    udi.FileAccessPermissions = permissions;
                }
            }
        }
    }

    /// <summary>
    /// This class sets up a BloomServer once at the beginning, for any tests that want to rely on it.
    /// </summary>
    /// <remarks>
    /// This mainly uses a BloomServer to bypass all the setup of an IRequestInfo.
    /// It's also nice that we view things more from the perspective of the API caller, which will be a lot more frequent
    /// than the caller of the handler itself (which is largely boilerplate)
    /// </remarks>
    [TestFixture]
    public class TeamCollectionApiServerTests
    {
        // FYI: If the tests are run in parallel, you might want some locking, but it doesn't seem needed right now.
        private BloomServer _server;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _server = new BloomServer(new BookSelection());
        }

        [SetUp]
        public void Setup()
        {
            _server?.ApiHandler?.ClearEndpointHandlers();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _server?.Dispose();
            _server = null;
        }

        // TODO: HandleAttemptLockOfCurrentBook

        // TODO: HandleCheckInCurrentBook

        // TODO: HandleChooseFolderLoaction

        // TODO: HandleCreateTeamCollection

        #region HandleCurrentBookStatus
        [Test]
        public void HandleCurrentBookStatus_InsufficientRegistration_RequestFails()
        {
            var originalValue = SIL.Windows.Forms.Registration.Registration.Default.Email;

            try
            {
                SIL.Windows.Forms.Registration.Registration.Default.Email = "";

                var api = new TeamCollectionApiBuilder().WithDefaultMocks().Build();
                api.RegisterWithApiHandler(_server.ApiHandler);

                // System Under Test
                TestDelegate systemUnderTest = () =>
                    ApiTest.GetString(
                        _server,
                        endPoint: "teamCollection/currentBookStatus",
                        returnType: ApiTest.ContentType.Text
                    );

                Assert.Throws(typeof(System.Net.WebException), systemUnderTest);
            }
            finally
            {
                SIL.Windows.Forms.Registration.Registration.Default.Email = originalValue;
            }
        }

        [Test]
        public void HandleCurrentBookStatus_LockedByRealUser_StatusIndicatesThatuser()
        {
            var originalValue = SIL.Windows.Forms.Registration.Registration.Default.Email;

            try
            {
                SIL.Windows.Forms.Registration.Registration.Default.Email = "me@example.com";

                var apiBuilder = new TeamCollectionApiBuilder().WithDefaultMocks(true);
                var api = apiBuilder.Build();
                api.RegisterWithApiHandler(_server.ApiHandler);

                var mockTeamCollection = SetupMockTcForBookStatus(apiBuilder);
                mockTeamCollection
                    .Setup(x => x.WhoHasBookLocked(It.IsAny<string>()))
                    .Returns("other@example.com");
                mockTeamCollection
                    .Setup(x => x.WhatComputerHasBookLocked(It.IsAny<string>()))
                    .Returns("Other's Computer");

                // System Under Test
                var result = ApiTest.GetString(
                    _server,
                    endPoint: "teamCollection/selectedBookStatus",
                    returnType: ApiTest.ContentType.Text
                );

                // Verification
                StringAssert.Contains("\"who\":\"other@example.com\"", result);
            }
            finally
            {
                // Cleanup
                SIL.Windows.Forms.Registration.Registration.Default.Email = originalValue;
            }
        }

        private static Mock<FolderTeamCollection> SetupMockTcForBookStatus(
            TeamCollectionApiBuilder apiBuilder
        )
        {
            var mockTeamCollection = new Mock<Bloom.TeamCollection.FolderTeamCollection>();
            apiBuilder.MockTeamCollectionManager
                .SetupGet(x => x.CurrentCollectionEvenIfDisconnected)
                .Returns(mockTeamCollection.Object);
            apiBuilder.MockTeamCollectionManager
                .SetupGet(x => x.CurrentCollection)
                .Returns(mockTeamCollection.Object);
            mockTeamCollection
                .Setup(x => x.GetPathToBookFileInRepo(It.IsAny<string>()))
                .Returns("some fake path");
            mockTeamCollection
                .Setup(x => x.HasLocalChangesThatMustBeClobbered(It.IsAny<string>()))
                .Returns(false);
            mockTeamCollection
                .Setup(x => x.HasBeenChangedRemotely(It.IsAny<string>()))
                .Returns(false);
            mockTeamCollection
                .Setup(x => x.GetCouldNotOpenCorruptZipMessage())
                .Returns("some fake message");
            return mockTeamCollection;
        }

        [Test]
        public void HandleCurrentBookStatus_LockedByFakeUser_FakeUserConvertedToCurrentUser()
        {
            var originalValue = SIL.Windows.Forms.Registration.Registration.Default.Email;

            try
            {
                SIL.Windows.Forms.Registration.Registration.Default.Email = "me@example.com";

                var mockBook = new Mock<Bloom.Book.Book>();
                mockBook.Setup(m => m.IsSaveable).Returns(true);
                mockBook.Setup(m => m.FolderPath).Returns("");
                var mockBookSelection = new Mock<BookSelection>();
                mockBookSelection.Setup(m => m.CurrentSelection).Returns(mockBook.Object);

                var apiBuilder = new TeamCollectionApiBuilder()
                    .WithDefaultMocks(true)
                    .WithBookSelection(mockBookSelection.Object);
                var api = apiBuilder.Build();
                api.RegisterWithApiHandler(_server.ApiHandler);

                var mockTeamCollection = SetupMockTcForBookStatus(apiBuilder);
                mockTeamCollection
                    .Setup(x => x.WhoHasBookLocked(It.IsAny<string>()))
                    .Returns("this user");
                mockTeamCollection
                    .Setup(x => x.WhatComputerHasBookLocked(It.IsAny<string>()))
                    .Returns("My Computer");

                // System Under Test
                var result = ApiTest.GetString(
                    _server,
                    endPoint: "teamCollection/selectedBookStatus",
                    returnType: ApiTest.ContentType.Text
                );

                // Verification
                StringAssert.Contains("\"who\":\"me@example.com\"", result);
            }
            finally
            {
                // Cleanup
                SIL.Windows.Forms.Registration.Registration.Default.Email = originalValue;
            }
        }

        #endregion

        #region HandleGetLog
        [Test]
        public void HandleGetLog_NullMessageLog_RequestFailed()
        {
            var apiBuilder = new TeamCollectionApiBuilder().WithDefaultMocks();
            var api = apiBuilder.Build();
            api.RegisterWithApiHandler(_server.ApiHandler);

            apiBuilder.MockTeamCollectionManager
                .SetupGet(x => x.MessageLog)
                .Returns((ITeamCollectionMessageLog)null);

            TestDelegate systemUnderTest = () =>
            {
                ApiTest.GetString(
                    _server,
                    endPoint: "teamCollection/getLog",
                    returnType: ApiTest.ContentType.JSON
                );
            };

            Assert.Throws(typeof(System.Net.WebException), systemUnderTest);
        }

        [Test]
        public void HandleGetLog_NonZeroMessages_MessagesReturned()
        {
            var apiBuilder = new TeamCollectionApiBuilder().WithDefaultMocks();
            var api = apiBuilder.Build();
            api.RegisterWithApiHandler(_server.ApiHandler);

            var mockMessageLog = new Mock<ITeamCollectionMessageLog>();
            apiBuilder.MockTeamCollectionManager
                .SetupGet(x => x.MessageLog)
                .Returns(mockMessageLog.Object);
            mockMessageLog
                .Setup(x => x.GetProgressMessages())
                .Returns(
                    new BloomWebSocketProgressEvent[]
                    {
                        new BloomWebSocketProgressEvent("unused", ProgressKind.Progress, "1"),
                        new BloomWebSocketProgressEvent("unused", ProgressKind.Progress, "2")
                    }
                );

            // System Under Test
            var result = ApiTest.GetString(
                _server,
                endPoint: "teamCollection/getLog",
                returnType: ApiTest.ContentType.JSON
            );

            // Verification
            var deserializedArray = JsonConvert.DeserializeObject<BloomWebSocketProgressEvent[]>(
                result
            );
            var messages = deserializedArray.Select(x => x.message).ToArray();
            CollectionAssert.AreEqual(new string[] { "1", "2" }, messages);
        }
        #endregion


        #region HandleIsTeamCollectionEnabled
        [Test]
        public void HandleIsTeamCollectionEnabled_NoCollection_ReturnsFalse()
        {
            var apiBuilder = new TeamCollectionApiBuilder().WithDefaultMocks();
            var api = apiBuilder.Build();
            api.RegisterWithApiHandler(_server.ApiHandler);

            // apiBuilder.MockTeamCollectionManager.Object.CurrentCollectionEvenIfDisconnected hasn't been changed,
            // so it will return null

            // System Under Test
            var result = ApiTest.GetString(
                _server,
                endPoint: "teamCollection/isTeamCollectionEnabled",
                returnType: ApiTest.ContentType.Text
            );

            Assert.That(result, Is.EqualTo("false"));
        }

        [Test]
        public void HandleIsTeamCollectionEnabled_BookSelectedButNotEditable_ReturnsFalse()
        {
            var apiBuilder = new TeamCollectionApiBuilder().WithDefaultMocks();
            var api = apiBuilder.Build();
            api.RegisterWithApiHandler(_server.ApiHandler);

            var mockTeamCollection = new Mock<Bloom.TeamCollection.TeamCollection>();
            apiBuilder.MockTeamCollectionManager
                .SetupGet(x => x.CurrentCollectionEvenIfDisconnected)
                .Returns(mockTeamCollection.Object);

            var mockBook = new Mock<Bloom.Book.Book>();
            mockBook.SetupGet(x => x.IsInEditableCollection).Returns(false);

            apiBuilder.MockBookSelection.SetupGet(x => x.CurrentSelection).Returns(mockBook.Object);

            // System Under Test
            var result = ApiTest.GetString(
                _server,
                endPoint: "teamCollection/isTeamCollectionEnabled",
                returnType: ApiTest.ContentType.Text
            );

            Assert.That(result, Is.EqualTo("false"));
        }

        [Test]
        public void HandleIsTeamCollectionEnabled_CurrentSelectionNull_ReturnsTrue()
        {
            var apiBuilder = new TeamCollectionApiBuilder().WithDefaultMocks();
            var api = apiBuilder.Build();
            api.RegisterWithApiHandler(_server.ApiHandler);

            var mockTeamCollection = new Mock<Bloom.TeamCollection.TeamCollection>();
            apiBuilder.MockTeamCollectionManager
                .SetupGet(x => x.CurrentCollectionEvenIfDisconnected)
                .Returns(mockTeamCollection.Object);

            // apiBuilder.MockBookSelection.Object.CurrentSelection will return null

            // System Under Test
            var result = ApiTest.GetString(
                _server,
                endPoint: "teamCollection/isTeamCollectionEnabled",
                returnType: ApiTest.ContentType.Text
            );

            Assert.That(result, Is.EqualTo("true"));
        }

        [Test]
        public void HandleIsTeamCollectionEnabled_CurrentSelectionEditable_ReturnsTrue()
        {
            var apiBuilder = new TeamCollectionApiBuilder().WithDefaultMocks();
            var api = apiBuilder.Build();
            api.RegisterWithApiHandler(_server.ApiHandler);

            var mockTeamCollection = new Mock<Bloom.TeamCollection.TeamCollection>();
            apiBuilder.MockTeamCollectionManager
                .SetupGet(x => x.CurrentCollectionEvenIfDisconnected)
                .Returns(mockTeamCollection.Object);

            var mockBook = new Mock<Bloom.Book.Book>();
            mockBook.SetupGet(x => x.IsInEditableCollection).Returns(true);

            apiBuilder.MockBookSelection.SetupGet(x => x.CurrentSelection).Returns(mockBook.Object);

            // System Under Test
            var result = ApiTest.GetString(
                _server,
                endPoint: "teamCollection/isTeamCollectionEnabled",
                returnType: ApiTest.ContentType.Text
            );

            Assert.That(result, Is.EqualTo("true"));
        }

        [Test]
        public void HandleIsTeamCollectionEnabled_ExceptionThrown_RequestFailed()
        {
            var apiBuilder = new TeamCollectionApiBuilder().WithDefaultMocks();
            var api = apiBuilder.Build();
            api.RegisterWithApiHandler(_server.ApiHandler);

            apiBuilder.MockTeamCollectionManager
                .SetupGet(x => x.CurrentCollectionEvenIfDisconnected)
                .Returns(() => throw new ApplicationException());

            // System Under Test
            TestDelegate systemUnderTest = () =>
                ApiTest.GetString(
                    _server,
                    endPoint: "teamCollection/isTeamCollectionEnabled",
                    returnType: ApiTest.ContentType.Text
                );

            Assert.Throws(typeof(System.Net.WebException), systemUnderTest);
        }
        #endregion

        // ENHANCE: Test HandleJoinTeamCollection. But that one is mostly just testing logic in FolderTeamcollection.JoinCollectionTeam.
        // Not a very interesting test from the TeamCollectionApi side.

        #region HandleRepoFolderPath
        [Test]
        public void HandleRepoFolderPath_NullCurrCollection_ReturnsEmptyString()
        {
            var apiBuilder = new TeamCollectionApiBuilder().WithDefaultMocks();
            var api = apiBuilder.Build();
            api.RegisterWithApiHandler(_server.ApiHandler);

            // System Under Test
            var result = ApiTest.GetString(
                _server,
                endPoint: "teamCollection/repoFolderPath",
                returnType: ApiTest.ContentType.Text
            );

            Assert.That(result, Is.EqualTo(""));
        }

        [Test]
        public void HandleRepoFolderPath_NonNullCurrCollection_ReturnsRepoDescription()
        {
            var apiBuilder = new TeamCollectionApiBuilder().WithDefaultMocks();
            var api = apiBuilder.Build();
            api.RegisterWithApiHandler(_server.ApiHandler);

            var mockTeamCollection = new Mock<Bloom.TeamCollection.TeamCollection>();
            mockTeamCollection.SetupGet(x => x.RepoDescription).Returns("Fake Description");

            apiBuilder.MockTeamCollectionManager
                .SetupGet(x => x.CurrentCollectionEvenIfDisconnected)
                .Returns(mockTeamCollection.Object);

            // System Under Test
            var result = ApiTest.GetString(
                _server,
                endPoint: "teamCollection/repoFolderPath",
                returnType: ApiTest.ContentType.Text
            );

            Assert.That(result, Is.EqualTo("Fake Description"));
        }

        [Test]
        public void HandleRepoFolderPath_ExceptionThrown_RequestFailed()
        {
            var apiBuilder = new TeamCollectionApiBuilder().WithDefaultMocks();
            var api = apiBuilder.Build();
            api.RegisterWithApiHandler(_server.ApiHandler);

            apiBuilder.MockTeamCollectionManager
                .SetupGet(x => x.CurrentCollectionEvenIfDisconnected)
                .Returns(() => throw new ApplicationException());

            TestDelegate systemUnderTest = () =>
                ApiTest.GetString(
                    _server,
                    endPoint: "teamCollection/repoFolderPath",
                    returnType: ApiTest.ContentType.Text
                );

            Assert.Throws(typeof(System.Net.WebException), systemUnderTest);
        }

        #endregion
    }
}
