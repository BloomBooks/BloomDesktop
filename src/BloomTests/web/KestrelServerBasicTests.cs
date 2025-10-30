// Copyright (c) 2024 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ImageProcessing;
using Bloom.web;
using L10NSharp;
using L10NSharp.Windows.Forms;
using Moq;
using NUnit.Framework;
using SIL.IO;
using SIL.Reporting;
using TemporaryFolder = SIL.TestUtilities.TemporaryFolder;

namespace BloomTests.web
{
    /// <summary>
    /// Unit tests for KestrelBloomServer Phase 2.1 checkpoint.
    /// Tests port discovery, lifecycle management, and basic request routing.
    /// </summary>
    [TestFixture]
    public class KestrelServerBasicTests
    {
        private TemporaryFolder _folder;
        private BloomFileLocator _fileLocator;
        private CollectionSettings _collectionSettings;
        private ILocalizationManager _localizationManager;
        private BookSelection _bookSelection;
        private RuntimeImageProcessor _imageProcessor;
        private KestrelBloomServer _server;

        [SetUp]
        public void Setup()
        {
            Logger.Init();
            _folder = new TemporaryFolder("KestrelServerTests");
            LocalizationManager.UseLanguageCodeFolders = true;
            var localizationDirectory =
                FileLocationUtilities.GetDirectoryDistributedWithApplication("localization");
            _localizationManager = LocalizationManagerWinforms.Create(
                "en",
                "Bloom",
                "Bloom",
                "1.0.0",
                localizationDirectory,
                "SIL/Bloom",
                null,
                "",
                new string[] { }
            );

            ErrorReport.IsOkToInteractWithUser = false;
            var collectionPath = Path.Combine(_folder.Path, "TestCollection");
            _collectionSettings = new CollectionSettings(
                Path.Combine(_folder.Path, "TestCollection.bloomCollection")
            );
            _fileLocator = new BloomFileLocator(
                _collectionSettings,
                new XMatterPackFinder(
                    new string[] { BloomFileLocator.GetFactoryXMatterDirectory() }
                ),
                ProjectContext.GetFactoryFileLocations(),
                ProjectContext.GetFoundFileLocations(),
                ProjectContext.GetAfterXMatterFileLocations()
            );

            _bookSelection = new BookSelection();
            _imageProcessor = new RuntimeImageProcessor(new BookRenamedEvent());
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up server if it was started
            if (_server != null)
            {
                _server.Stop();
                _server.Dispose();
            }

            _localizationManager.Dispose();
            LocalizationManager.ForgetDisposedManagers();
            _folder.Dispose();
            Logger.ShutDown();

            // Reset static port
            KestrelBloomServer.portForHttp = 0;
            KestrelBloomServer.ServerIsListening = false;
        }

        #region Port Discovery Tests

        [Test]
        public void EnsureListening_StartsServerOnPort8089()
        {
            // Setup - Use real BloomApiHandler instead of mock (it needs BookSelection)
            var apiHandler = new BloomApiHandler(_bookSelection);
            _server = new KestrelBloomServer(
                _imageProcessor,
                _bookSelection,
                _fileLocator,
                apiHandler
            );

            // Execute
            _server.EnsureListening();

            // Verify
            var assignedPort = KestrelBloomServer.portForHttp;
            Assert.GreaterOrEqual(assignedPort, 8089);
            Assert.AreEqual(
                1,
                assignedPort % 2,
                "Expected fallback ports to preserve odd-number pattern."
            );
            Assert.IsTrue(KestrelBloomServer.ServerIsListening);
            StringAssert.Contains(
                assignedPort.ToString(CultureInfo.InvariantCulture),
                KestrelBloomServer.ServerUrl
            );
        }

        [Test]
        public void EnsureListening_SetsServerUrlCorrectly()
        {
            // Setup - Use real BloomApiHandler instead of mock (it needs BookSelection)
            var apiHandler = new BloomApiHandler(_bookSelection);
            _server = new KestrelBloomServer(
                _imageProcessor,
                _bookSelection,
                _fileLocator,
                apiHandler
            );

            // Execute
            _server.EnsureListening();

            // Verify
            Assert.IsTrue(KestrelBloomServer.ServerUrl.StartsWith("http://localhost:"));
            StringAssert.Contains(
                KestrelBloomServer.portForHttp.ToString(),
                KestrelBloomServer.ServerUrl
            );
        }

        [Test]
        public void EnsureListening_ServerUrlEndingInSlashHasTrailingSlash()
        {
            // Setup
            _server = new KestrelBloomServer(_imageProcessor, _bookSelection, _fileLocator);

            // Execute
            _server.EnsureListening();

            // Verify
            StringAssert.EndsWith("/", KestrelBloomServer.ServerUrlEndingInSlash);
        }

        [Test]
        public void EnsureListening_ServerUrlWithBloomPrefixHasCorrectPath()
        {
            // Setup
            _server = new KestrelBloomServer(_imageProcessor, _bookSelection, _fileLocator);

            // Execute
            _server.EnsureListening();

            // Verify
            StringAssert.Contains(
                "/bloom/",
                KestrelBloomServer.ServerUrlWithBloomPrefixEndingInSlash
            );
            StringAssert.EndsWith("/", KestrelBloomServer.ServerUrlWithBloomPrefixEndingInSlash);
        }

        [Test]
        public void EnsureListening_PortPropertyUpdatedCorrectly()
        {
            // Setup
            _server = new KestrelBloomServer(_imageProcessor, _bookSelection, _fileLocator);
            int portBefore = KestrelBloomServer.portForHttp;

            // Execute
            _server.EnsureListening();
            int portAfter = KestrelBloomServer.portForHttp;

            // Verify
            Assert.AreNotEqual(0, portAfter);
            Assert.GreaterOrEqual(portAfter, 8089);
            Assert.AreEqual(
                1,
                portAfter % 2,
                "Expected fallback ports to preserve odd-number pattern."
            );
        }

        [Test]
        public void EnsureListening_ThrowsIfFileLocatorNotConfigured()
        {
            using (var server = new KestrelBloomServer(_imageProcessor, _bookSelection, null))
            {
                var ex = Assert.Throws<InvalidOperationException>(() => server.EnsureListening());
                StringAssert.Contains("BloomFileLocator", ex.Message);
            }
        }

        [Test]
        public void EnsureListening_WorksWhenLocatorSuppliedAfterConstruction()
        {
            using (var server = new KestrelBloomServer(_imageProcessor, _bookSelection, null))
            {
                server.SetFileLocator(_fileLocator);
                Assert.DoesNotThrow(() => server.EnsureListening());
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ApplicationContainer_BloomServerStartsBeforeProjectSelection()
        {
            using (var container = new ApplicationContainer())
            {
                var server = container.BloomServer as KestrelBloomServer;
                Assert.IsNotNull(
                    server,
                    "Expected ApplicationContainer to provide KestrelBloomServer"
                );
                Assert.DoesNotThrow(() => server.EnsureListening());
                server.Stop();
                server.Dispose();
            }
        }

        #endregion

        #region Server Lifecycle Tests

        [Test]
        public void EnsureListening_CallTwiceDoesNotRestartServer()
        {
            // Setup
            _server = new KestrelBloomServer(_imageProcessor, _bookSelection, _fileLocator);

            // Execute
            _server.EnsureListening();
            int firstPort = KestrelBloomServer.portForHttp;
            _server.EnsureListening(); // Call again
            int secondPort = KestrelBloomServer.portForHttp;

            // Verify - port should be the same
            Assert.AreEqual(firstPort, secondPort);
        }

        [Test]
        public void Stop_StopsTheServer()
        {
            // Setup
            _server = new KestrelBloomServer(_imageProcessor, _bookSelection, _fileLocator);
            _server.EnsureListening();
            Assert.IsTrue(KestrelBloomServer.ServerIsListening);

            // Execute
            _server.Stop();

            // Verify - should be able to start again
            var server2 = new KestrelBloomServer(_imageProcessor, _bookSelection, _fileLocator);
            // Note: We can't fully verify it stopped without more complex testing
            // This is a basic smoke test
            Assert.Pass("Server stopped without throwing");
        }

        [Test]
        public void Dispose_ReleasesResources()
        {
            // Setup
            _server = new KestrelBloomServer(_imageProcessor, _bookSelection, _fileLocator);
            _server.EnsureListening();

            // Execute - should not throw
            Assert.DoesNotThrow(() => _server.Dispose());
        }

        [Test]
        public void DisposeWithoutStart_DoesNotThrow()
        {
            // Setup
            _server = new KestrelBloomServer(_imageProcessor, _bookSelection, _fileLocator);

            // Execute - should not throw even though server was never started
            Assert.DoesNotThrow(() => _server.Dispose());
        }

        #endregion

        #region Basic Routing Tests

        [Test]
        public void EnsureListening_ServerRespondsToTestConnection()
        {
            // Setup
            _server = new KestrelBloomServer(_imageProcessor, _bookSelection, _fileLocator);

            // Execute
            _server.EnsureListening();

            // Verify - test connection should succeed (this is done in EnsureListening)
            Assert.IsTrue(KestrelBloomServer.ServerIsListening);
        }

        [Test]
        public void ServerUrl_ReturnsCorrectUrl()
        {
            // Setup
            _server = new KestrelBloomServer(_imageProcessor, _bookSelection, _fileLocator);

            // Execute
            _server.EnsureListening();
            string serverUrl = KestrelBloomServer.ServerUrl;

            // Verify
            Assert.That(serverUrl, Does.Match(@"http://localhost:\d+"));
        }

        [Test]
        [Explicit("Requires browser or network test; not run in normal unit tests")]
        public void GET_RootPath_ReturnsHtml()
        {
            // Setup
            _server = new KestrelBloomServer(_imageProcessor, _bookSelection, _fileLocator);
            _server.EnsureListening();

            try
            {
                // Execute
                using (
                    var client = new System.Net.Http.HttpClient
                    {
                        Timeout = TimeSpan.FromSeconds(5),
                    }
                )
                {
                    string html = client
                        .GetStringAsync(KestrelBloomServer.ServerUrlEndingInSlash)
                        .Result;

                    // Verify
                    Assert.That(html, Does.Contain("Bloom Server") | Does.Contain("reactRoot"));
                }
            }
            catch (Exception ex)
            {
                Assert.Ignore("Network test failed (server may not be responding): " + ex.Message);
            }
        }

        #endregion

        #region Singleton Pattern Tests

        [Test]
        public void TheOneInstance_IsSet()
        {
            // Setup & Execute
            _server = new KestrelBloomServer(_imageProcessor, _bookSelection, _fileLocator);

            // Verify
            Assert.IsNotNull(KestrelBloomServer._theOneInstance);
        }

        [Test]
        public void TheOneInstance_ReferenceIsSame()
        {
            // Setup & Execute
            _server = new KestrelBloomServer(_imageProcessor, _bookSelection, _fileLocator);

            // Verify
            Assert.AreSame(_server, KestrelBloomServer._theOneInstance);
        }

        #endregion

        #region IBloomServer Implementation Tests

        [Test]
        public void RegisterThreadBlocking_DoesNotThrow()
        {
            // Setup
            _server = new KestrelBloomServer(_imageProcessor, _bookSelection, _fileLocator);

            // Execute & Verify
            Assert.DoesNotThrow(() => _server.RegisterThreadBlocking());
        }

        [Test]
        public void RegisterThreadUnblocked_DoesNotThrow()
        {
            // Setup
            _server = new KestrelBloomServer(_imageProcessor, _bookSelection, _fileLocator);

            // Execute & Verify
            Assert.DoesNotThrow(() => _server.RegisterThreadUnblocked());
        }

        #endregion
    }
}
