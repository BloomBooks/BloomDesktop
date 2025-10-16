using System;
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
            // Create minimal dependencies
            var bookRenamedEvent = new BookRenamedEvent();
            var bookSelection = new BookSelection();
            var imageProcessor = new RuntimeImageProcessor(bookRenamedEvent);
            var apiHandler = new BloomApiHandler(bookSelection);
            
            // Create server with null file locator (not needed for simple test)
            var server = new KestrelBloomServer(imageProcessor, bookSelection, null, apiHandler);
            
            try
            {
                // Start server
                server.EnsureListening();
                
                // Give it a moment to fully start
                await Task.Delay(500);
                
                // Make a simple HTTP request
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    var url = $"http://localhost:{KestrelBloomServer.portForHttp}/testconnection";
                    Console.WriteLine($"Testing URL: {url}");
                    
                    var response = await client.GetAsync(url);
                    Console.WriteLine($"Response status: {response.StatusCode}");
                    
                    var content = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Response content: {content}");
                    
                    Assert.IsTrue(response.IsSuccessStatusCode);
                    Assert.AreEqual("OK", content);
                }
            }
            finally
            {
                server.Stop();
                server.Dispose();
            }
        }
    }
}
