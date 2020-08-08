using System;
using System.Net;
using System.Threading;
using Bloom.WebLibraryIntegration;
using NUnit.Framework;
using NUnit.Framework.Constraints;
using RestSharp.Extensions;
using SayMore.UI.Utilities;

namespace BloomTests.WebLibraryIntegration
{
	[TestFixture]
	public class BloomParseClientTests
	{
		private BloomParseClient _client;

		[SetUp]
		public void Setup()
		{
			_client = new BloomParseClient();
		}

		/// <summary>
		/// When we restore this, we should also fix it so it deletes the book it creates. The inaccuracies were partly
		/// caused by accumulating over 1000 books (actually, over 17,000) from repeatedly running this and other tests.
		/// </summary>
		[Test, Ignore("parse.com has gotten into a state where count is not accurate in the unit test database")]
		public void GetBookCount_AfterAddingABook_Increases()
		{
			var initialCount = _client.GetBookCount();
			CreateBookRecord();
			Thread.Sleep(3000);//jh added this because the test failed frequently, but not when stepping through. Hypothesizing that AWS S3 doesn't update the count immediately
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

		// It would be nice to have some tests for the new FirebaseLogin code, too, but so far
		// we have not been able to get that code to run except after starting Bloom fully.

		private void Login()
		{
			// This line can be uncommented for a single run of one test as an easy way to re-create the record that we assume
			// always exists on parse.com.
			//_client.CreateUser("unittest@example.com", "unittest");
			Assert.IsTrue(_client.LegacyLogIn("unittest@example.com", "unittest"),
				"Could not log in using the unittest@example.com account");
		}

		[Test]
		public void LogIn_BadCredentials_ReturnsFalse()
		{
			Assert.IsFalse(_client.LoggedIn);
			Assert.IsFalse(_client.LegacyLogIn("bogus@example.com", "abc"));
			Assert.IsFalse(_client.LoggedIn);
		}

		[Test]
		public void CreateBookRecord_NotLoggedIn_Throws()
		{
			Assert.Throws<ApplicationException>(() =>_client.CreateBookRecord("{\"bookInstanceId\":\"123\"}"));
		}

		/// <summary>
		/// Could be an independent test, but it is needed as part of others.
		/// </summary>
		/// <returns></returns>
		private string CreateBookRecord()
		{
			Login();
			string bookInstanceId = Guid.NewGuid().ToString();
			var title = "unittest" + bookInstanceId;
			var result = _client.CreateBookRecord(string.Format("{{\"bookInstanceId\":\"{0}\",\"title\":\"{1}\",{2}}}", bookInstanceId, title, _client.UploaderJsonString));
			Assert.AreEqual(HttpStatusCode.Created, result.StatusCode);
			return bookInstanceId;
		}

		[Test]
		public void GetBookRecord_BookIsThere_Succeeds()
		{
			//first make a book so that we know it is there
			var id= CreateBookRecord();
			var bookjson = _client.GetSingleBookRecord(id);

			Assert.AreEqual(id, bookjson.bookInstanceId.Value);

			// Partial matches are no good
			Assert.IsNull(_client.GetSingleBookRecord(new Guid().ToString()));

			// Can't match if logged in as different user
			_client.LegacyLogIn("someotheruser@example.com", "unittest2");
			Assert.IsNull(_client.GetSingleBookRecord(id));

		}
		[Test]
		public void GetBookRecord_BookIsNotThere_Fails()
		{
			Assert.IsNull(_client.GetSingleBookRecord("notthere"));
		}

		//{"authors":["Heinrich Poschinger"],"categories":[],"description":"This is an EXACT reproduction of a book published before 1923. This IS NOT an OCR\"d book with strange characters, introduced typographical errors, and jumbled words. This book may have occasional imperfections such as missing or blurred pages, poor pictures, errant marks, etc. that were either part of the original artifact, or were introduced by the scanning process. We believe this work is culturally important, and despite the imperfections, have elected to bring it back into print as part of our continuing commitment to the preservation of printed works worldwide. We appreciate your understanding of the imperfections in the preservation process, and hope you enjoy this valuable book.","imageLinks":{"smallThumbnail":"http://bks1.books.google.co.th/books?id=MxhvSQAACAAJ&printsec=frontcover&img=1&zoom=5&source=gbs_api","thumbnail":"http://bks1.books.google.co.th/books?id=MxhvSQAACAAJ&printsec=frontcover&img=1&zoom=1&source=gbs_api"},"industryIdentifiers":[{"identifier":"1148362096","type":"ISBN_10"},{"identifier":"9781148362090","type":"ISBN_13"}],"language":"en","pageCount":270,"previewLink":"http://books.google.co.th/books?id=MxhvSQAACAAJ&dq=hamburger&hl=&cd=520&source=gbs_api","printType":"BOOK","publishedDate":"2010-04","publisher":"Nabu Press","title":"Fürst Bismarck und Seine Hamburger Freunde"}
	}
}
