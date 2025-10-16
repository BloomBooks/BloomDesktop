using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ImageProcessing;
using Bloom.web;
using NUnit.Framework;

namespace BloomTests.web
{
    [TestFixture]
    public class KestrelServerSimpleTest
    {
        [Test]
        public async Task SimpleConnectionTest()
        {
            // Create minimal dependencies - FileLocator is required by FileLocationService
            var bookRenamedEvent = new BookRenamedEvent();
            var bookSelection = new BookSelection();
            var imageProcessor = new RuntimeImageProcessor(bookRenamedEvent);
            var apiHandler = new BloomApiHandler(bookSelection);

            // Create a minimal CollectionSettings and BloomFileLocator
            var tempFolder = new SIL.TestUtilities.TemporaryFolder("KestrelSimpleTest");
            var collectionPath = Path.Combine(tempFolder.Path, "test.bloomCollection");
            var collectionSettings = new CollectionSettings(collectionPath);

            var fileLocator = new BloomFileLocator(
                collectionSettings,
                new XMatterPackFinder(
                    new string[] { BloomFileLocator.GetFactoryXMatterDirectory() }
                ),
                ProjectContext.GetFactoryFileLocations(),
                ProjectContext.GetFoundFileLocations(),
                ProjectContext.GetAfterXMatterFileLocations()
            );

            // Create server with all required dependencies
            var server = new KestrelBloomServer(
                imageProcessor,
                bookSelection,
                fileLocator,
                apiHandler
            );

            try
            {
                // Start server
                server.EnsureListening();

                // Give it a moment to fully start
                await Task.Delay(500);

                // Make a simple HTTP request to root endpoint
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    var url = $"http://localhost:{KestrelBloomServer.portForHttp}/";
                    Console.WriteLine($"Testing URL: {url}");

                    var response = await client.GetAsync(url);
                    Console.WriteLine($"Response status: {response.StatusCode}");

                    var content = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Response content length: {content.Length}");
                    Console.WriteLine($"Response content: {content}");

                    Assert.IsTrue(response.IsSuccessStatusCode);
                    Assert.IsTrue(content.Contains("Bloom Server"));
                }
            }
            finally
            {
                server.Stop();
                server.Dispose();
                tempFolder.Dispose();
            }
        }
    }
}
