using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using NUnit.Framework;

namespace BloomTests.web
{
	[TestFixture]
	public class ReadersApiTests
	{
		private EnhancedImageServer _server;
		[SetUp]
		public void Setup()
		{
			var bookSelection = new BookSelection();
			bookSelection.SelectBook(new Bloom.Book.Book());
			_server = new EnhancedImageServer(bookSelection);

			//needed to avoid a check in the server
			_server.CurrentCollectionSettings = new CollectionSettings();
			var controller = new ReadersApi(bookSelection);
			controller.RegisterWithServer(_server);
		}

		[TearDown]
		public void TearDown()
		{
			_server.Dispose();
			_server = null;
		}

		[Test]
		public void IsReceivingApiCalls()
		{
			var result = ApiTest.GetString(_server,"readers/io/test");
			Assert.That(result, Is.EqualTo("OK"));
		}
	}
}