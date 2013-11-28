using System;
using System.Net;
using Bloom.WebLibraryIntegration;
using NUnit.Framework;
using RestSharp.Extensions;
using SayMore.UI.Utilities;

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


		[Test]
		public void GetBookCount_AfterAddingABook_Increases()
		{
			var initialCount = _client.GetBookCount();
			CreateBookRecord_Succeeds();
			Assert.Greater(_client.GetBookCount(), initialCount);
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
			Login();
			Assert.IsTrue(_client.LoggedIn);
		}

		private void Login()
		{
			Assert.IsTrue(_client.LogIn("unittest@example.com", "unittest"),
				"Could not log in using the unittest@example.com account");
		}

		[Test]
		public void LogIn_BadCredentials_ReturnsFalse()
		{
			Assert.IsFalse(_client.LoggedIn);
			Assert.IsFalse(_client.LogIn("bogus@example.com", "abc"));
			Assert.IsFalse(_client.LoggedIn);
		}

		[Test]
		[ExpectedException(typeof(ApplicationException))]
		public void CreateBookRecord_NotLoggedIn_Throws()
		{
			_client.CreateBookRecord("{\"bookInstanceId\":\"123\"}");
		}

		[Test]
		public string CreateBookRecord_Succeeds()
		{
			Login();
			string bookInstanceId = Guid.NewGuid().ToString();
			var title = "unittest" + bookInstanceId;
			var result =_client.CreateBookRecord(string.Format("{{\"bookInstanceId\":\"{0}\",\"volumeInfo\":{{\"title\":\"{1}\"}} }}", bookInstanceId, title));
			Assert.AreEqual(HttpStatusCode.Created, result.StatusCode);
			return bookInstanceId;
		}

		[Test]
		public void GetBookRecord_BookIsThere_Succeeds()
		{
			//first make a book so that we know it is there
			var bookInstanceId= CreateBookRecord_Succeeds();
			var bookjson = _client.GetSingleBookRecord(bookInstanceId);

			Assert.AreEqual(bookInstanceId, bookjson.bookInstanceId.Value);
		}
		[Test]
		public void GetBookRecord_BookIsNotThere_Fails()
		{
			Assert.IsNull(_client.GetSingleBookRecord("notthere"));
		}

		//{"authors":["Heinrich Poschinger"],"categories":[],"description":"This is an EXACT reproduction of a book published before 1923. This IS NOT an OCR\"d book with strange characters, introduced typographical errors, and jumbled words. This book may have occasional imperfections such as missing or blurred pages, poor pictures, errant marks, etc. that were either part of the original artifact, or were introduced by the scanning process. We believe this work is culturally important, and despite the imperfections, have elected to bring it back into print as part of our continuing commitment to the preservation of printed works worldwide. We appreciate your understanding of the imperfections in the preservation process, and hope you enjoy this valuable book.","imageLinks":{"smallThumbnail":"http://bks1.books.google.co.th/books?id=MxhvSQAACAAJ&printsec=frontcover&img=1&zoom=5&source=gbs_api","thumbnail":"http://bks1.books.google.co.th/books?id=MxhvSQAACAAJ&printsec=frontcover&img=1&zoom=1&source=gbs_api"},"industryIdentifiers":[{"identifier":"1148362096","type":"ISBN_10"},{"identifier":"9781148362090","type":"ISBN_13"}],"language":"en","pageCount":270,"previewLink":"http://books.google.co.th/books?id=MxhvSQAACAAJ&dq=hamburger&hl=&cd=520&source=gbs_api","printType":"BOOK","publishedDate":"2010-04","publisher":"Nabu Press","title":"Fürst Bismarck und Seine Hamburger Freunde"}
	}
}
