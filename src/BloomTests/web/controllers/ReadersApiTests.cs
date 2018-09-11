using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using NUnit.Framework;

namespace BloomTests.web
{
	[TestFixture]
	public class ReadersApiTests
	{
		private BloomServer _server;
		[SetUp]
		public void Setup()
		{
			var bookSelection = new BookSelection();
			bookSelection.SelectBook(new Bloom.Book.Book());
			_server = new BloomServer(bookSelection);

			var controller = new ReadersApi(bookSelection);
			controller.RegisterWithApiHandler(_server.ApiHandler);
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
