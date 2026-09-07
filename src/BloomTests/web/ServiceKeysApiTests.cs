using System.Linq;
using System.Threading;
using Bloom.Api;
using Bloom.Book;
using Bloom.Utils;
using Bloom.web.controllers;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SIL.TestUtilities;

namespace BloomTests.web
{
    /// <summary>
    /// Integration tests over the real HTTP surface of <see cref="ServiceKeysApi"/>: the two
    /// endpoints the front end uses to read and write the user's service API keys.
    ///
    /// <see cref="ServiceKeyStore.FolderForTests"/> points the store at a TemporaryFolder, so
    /// nothing here can read or overwrite the developer's own keys.
    /// </summary>
    [TestFixture]
    public class ServiceKeysApiTests
    {
        private BloomServer _server;
        private TemporaryFolder _folder;

        [SetUp]
        public void Setup()
        {
            // Share the same monitor as the other server tests so we never run two servers on
            // the fixed test port at once.
            Monitor.Enter(EndpointHandlerTests._portMonitor);

            _folder = new TemporaryFolder("ServiceKeysApiTests");
            ServiceKeyStore.FolderForTests = _folder.Path;
            _server = new BloomServer(new BookSelection());
            new ServiceKeysApi().RegisterWithApiHandler(_server.ApiHandler);
        }

        [TearDown]
        public void TearDown()
        {
            RetiredTestServers.Retire(_server);
            _server = null;
            ServiceKeyStore.FolderForTests = null;
            _folder.Dispose();

            Monitor.Exit(EndpointHandlerTests._portMonitor);
        }

        [Test]
        public void GetKey_WhenThereIsNone_RepliesNull()
        {
            Assert.That(
                ApiTest.GetString(_server, "serviceKeys/key", "name=openRouter"),
                Is.EqualTo("null")
            );
        }

        [Test]
        public void PostKey_ThenGet_RoundTripsCharactersUrlEscapingWouldChange()
        {
            // A real key can hold any of these, and each is something the default unescaping
            // would rewrite: "+" would become a space, and "%2B" a "+".
            const string key = "sk-a+b%2Bc d/e=";

            ApiTest.PostString(
                _server,
                "serviceKeys/key?name=openRouter",
                key,
                ApiTest.ContentType.Text
            );

            Assert.That(
                ServiceKeyStore.Get("openRouter"),
                Is.EqualTo(key),
                "the store must hold exactly what was posted"
            );
            Assert.That(
                ApiTest.GetString(_server, "serviceKeys/key", "name=openRouter"),
                Is.EqualTo(JToken.FromObject(key).ToString(Newtonsoft.Json.Formatting.None))
            );
        }

        [Test]
        public void PostKey_EmptyBody_RemovesTheKey()
        {
            ServiceKeyStore.Set("openRouter", "a key");
            Assert.That(
                ServiceKeyStore.Get("openRouter"),
                Is.EqualTo("a key"),
                "setup: the key must really be there, or this proves nothing"
            );

            ApiTest.PostString(
                _server,
                "serviceKeys/key?name=openRouter",
                "",
                ApiTest.ContentType.Text
            );

            Assert.That(ServiceKeyStore.Get("openRouter"), Is.Null);
        }

        [Test]
        public void GetNamespace_ReturnsTheVersionAndTheNamesWithThePrefixStripped()
        {
            ServiceKeyStore.Set("imageGallery.pixabay", "pix");
            ServiceKeyStore.Set("imageGallery.other", "oth");
            // Outside the namespace, so it must not appear.
            ServiceKeyStore.Set("openRouter", "or");

            var reply = JObject.Parse(
                ApiTest.GetString(_server, "serviceKeys/keys", "prefix=imageGallery.")
            );

            Assert.That((int)reply["version"], Is.EqualTo(1));
            Assert.That((string)reply["pixabay"], Is.EqualTo("pix"));
            Assert.That((string)reply["other"], Is.EqualTo("oth"));
            Assert.That(reply.Properties().Count(), Is.EqualTo(3), reply.ToString());
        }

        [Test]
        public void PostNamespace_StoresWhatIsPostedAndRemovesWhatIsNot()
        {
            ServiceKeyStore.Set("imageGallery.pixabay", "old");
            ServiceKeyStore.Set("imageGallery.goneNow", "bye");
            // Outside the namespace: the post says nothing about it, so it must survive.
            ServiceKeyStore.Set("openRouter", "or");

            ApiTest.PostString(
                _server,
                "serviceKeys/keys?prefix=imageGallery.",
                "{\"version\":1,\"pixabay\":\"new\"}",
                ApiTest.ContentType.JSON
            );

            Assert.That(ServiceKeyStore.Get("imageGallery.pixabay"), Is.EqualTo("new"));
            Assert.That(ServiceKeyStore.Get("imageGallery.goneNow"), Is.Null);
            Assert.That(ServiceKeyStore.Get("openRouter"), Is.EqualTo("or"));
        }
    }
}
