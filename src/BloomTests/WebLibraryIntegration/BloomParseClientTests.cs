using Bloom.WebLibraryIntegration;
using NUnit.Framework;

namespace BloomTests.WebLibraryIntegration
{
    public class BloomParseClientTests
    {
        private BloomParseClient _client;

        [SetUp]
        public void Setup()
        {
            _client = new BloomParseClient();
        }

        [Test]//TODO this is dumb
        public void GetBookCount_GetsSome()
        {
            Assert.Greater(_client.GetBookCount(),0);
        }

        [Test]
        public void LoggedIn_Initially_IsFalse()
        {
            Assert.IsFalse(_client.LoggedIn);
        }
        [Test]
        public void LogIn_GoodCredentials_ReturnsTrue()
        {
            Assert.IsFalse(_client.LoggedIn);
            Assert.IsTrue(_client.LogIn("unittest@example.com", "unittest"), "Could not log in using the unittest@example.com account");
            Assert.IsTrue(_client.LoggedIn);
        }

        [Test]
        public void LogIn_BadCredentials_ReturnsFalse()
        {
            Assert.IsFalse(_client.LoggedIn); 
            Assert.IsFalse(_client.LogIn("bogus@example.com", "abc"));
            Assert.IsFalse(_client.LoggedIn);
        }

        [Test, Ignore("not yet")]
        public void CreateMetadataRecord_NotLoggedIn_Throws()
        {
        }

        [Test, Ignore("not yet")]
        public void GetRecord_AfterCreateMetadataRecord_Succeeds()
        {
        }

        [Test, Ignore("not yet")]
        public void GetRecord_NotOnParse_Fails()
        {
        }
    }
}
