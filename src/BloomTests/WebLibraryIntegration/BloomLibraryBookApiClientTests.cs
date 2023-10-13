using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Bloom.WebLibraryIntegration;
using Microsoft.CSharp.RuntimeBinder;
using Newtonsoft.Json;
using NUnit.Framework;
using RestSharp;
using SIL.Code;

namespace BloomTests.WebLibraryIntegration
{
	[TestFixture]
	public class BloomLibraryBookApiClientTests
	{
		private BloomLibraryBookApiClientTestDouble _client;

		[SetUp]
		public void Setup()
		{
			_client = new BloomLibraryBookApiClientTestDouble();
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
			Assert.IsTrue(_client.TestOnly_LegacyLogIn("unittest@example.com", "unittest"),
				"Could not log in using the unittest@example.com account");
		}

		[Test, Ignore("This is failing sometimes on TeamCity; and since the main logic it tests is TestOnly_LegacyLogIn (which is no longer production code), it doesn't seem worth the intermittent failures.")]
		public void LogIn_BadCredentials_ReturnsFalse()
		{
			Assert.IsFalse(_client.LoggedIn);
			Assert.IsFalse(_client.TestOnly_LegacyLogIn("bogus@example.com", "abc"));
			Assert.IsFalse(_client.LoggedIn);
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
			var result = _client.TestOnly_CreateBookRecord(string.Format("{{\"bookInstanceId\":\"{0}\",\"title\":\"{1}\",{2}}}", bookInstanceId, title, _client.UploaderJsonString));
			Assert.AreEqual(HttpStatusCode.Created, result.StatusCode);
			return bookInstanceId;
		}

		[Test]
		public void GetBookRecord_BookIsThere_Succeeds()
		{
			//first make a book so that we know it is there
			var id = CreateBookRecord();
			var bookjson = _client.GetSingleBookRecord(id);

			Assert.AreEqual(id, bookjson.bookInstanceId.Value);

			// Partial matches are no good
			Assert.IsNull(_client.GetSingleBookRecord(new Guid().ToString()));

			// In Sept 2022, we started getting strange errors from NUnit.
			// The tests were passing but when NUnit was cleaning up, it
			// would throw a SocketException in its own code.
			// We finally tracked it down to the the code which was
			// attempting to log out of firebase (by launching a dialog).
			// We still don't understand why it started or what the underlying issue
			// was, but this prevents it.
			bool includeFirebaseLogout = false;

			// Our lookup uses the logged in user as part of the query.
			// Ensure the lookup fails if not logged in as the uploader.
			_client.Logout(includeFirebaseLogout);
			Assert.IsNull(_client.GetSingleBookRecord(id));
		}

		[Test]
		public void GetBookRecord_BookIsNotThere_Fails()
		{
			Assert.IsNull(_client.GetSingleBookRecord("notthere"));
		}

		//{"authors":["Heinrich Poschinger"],"categories":[],"description":"This is an EXACT reproduction of a book published before 1923. This IS NOT an OCR\"d book with strange characters, introduced typographical errors, and jumbled words. This book may have occasional imperfections such as missing or blurred pages, poor pictures, errant marks, etc. that were either part of the original artifact, or were introduced by the scanning process. We believe this work is culturally important, and despite the imperfections, have elected to bring it back into print as part of our continuing commitment to the preservation of printed works worldwide. We appreciate your understanding of the imperfections in the preservation process, and hope you enjoy this valuable book.","imageLinks":{"smallThumbnail":"http://bks1.books.google.co.th/books?id=MxhvSQAACAAJ&printsec=frontcover&img=1&zoom=5&source=gbs_api","thumbnail":"http://bks1.books.google.co.th/books?id=MxhvSQAACAAJ&printsec=frontcover&img=1&zoom=1&source=gbs_api"},"industryIdentifiers":[{"identifier":"1148362096","type":"ISBN_10"},{"identifier":"9781148362090","type":"ISBN_13"}],"language":"en","pageCount":270,"previewLink":"http://books.google.co.th/books?id=MxhvSQAACAAJ&dq=hamburger&hl=&cd=520&source=gbs_api","printType":"BOOK","publishedDate":"2010-04","publisher":"Nabu Press","title":"Fürst Bismarck und Seine Hamburger Freunde"}
	}

	public class BloomLibraryBookApiClientTestDouble : BloomLibraryBookApiClient
	{
		private RestRequest MakeParsePutRequest(string path)
		{
			return MakeParseRequest(path, Method.PUT);
		}

		private RestRequest MakeParseDeleteRequest(string path)
		{
			return MakeParseRequest(path, Method.DELETE);
		}

		// Log in directly to parse server with name and password.
		// Some unit tests need it, since we haven't been able to get the new firebase login to work
		// except when Bloom is actually running.
		public bool TestOnly_LegacyLogIn(string account, string password)
		{
			_sessionToken = string.Empty;
			Account = string.Empty;
			var request = MakeParseGetRequest("login");
			request.AddParameter("username", account.ToLowerInvariant());
			request.AddParameter("password", password);

			bool result = false;
			RetryUtility.Retry(() => {
				var response = RestClientForParse.Execute(request);
				var dy = JsonConvert.DeserializeObject<dynamic>(response.Content);
				try
				{
					_sessionToken = dy.sessionToken; //there's also an "error" in there if it fails, but a null sessionToken tells us all we need to know
				}
				catch (RuntimeBinderException)
				{
					// We are seeing this sometimes while running unit tests.
					// This is simply an attempt to diagnose what is happening.
					Console.WriteLine("Attempt to deserialize response content session token failed while attempting log in to parse (BL-4099).");
					Console.WriteLine($"username: {request.Parameters.Where(p => p.Name == "username")}");
					Console.WriteLine($"request.Resource: {request.Resource}");
					Console.WriteLine($"response.IsSuccessful: {response.IsSuccessful}");
					Console.WriteLine($"response.Content: {response.Content}");
					Console.WriteLine($"response.ContentLength: {response.ContentLength}");
					Console.WriteLine($"response.ContentType: {response.ContentType}");
					Console.WriteLine($"response.ResponseStatus: {response.ResponseStatus}");
					Console.WriteLine($"response.StatusCode: {response.StatusCode}");
					Console.WriteLine($"response.StatusDescription: {response.StatusDescription}");
					Console.WriteLine($"response.ErrorMessage: {response.ErrorMessage}");
					Console.WriteLine($"response.ErrorException: {response.ErrorException}");
					Console.WriteLine($"deserialized response.Content: {dy}");
					throw;
				}
				_userId = dy.objectId;
				Account = account;
				result = LoggedIn;
			}, 3, 2000, new HashSet<Type> { typeof(RuntimeBinderException).Assembly.GetType("Microsoft.CSharp.RuntimeBinder.RuntimeBinderException") });
			return result;
		}

		public IRestResponse TestOnly_UpdateBookRecord(string metadataJson, string bookObjectId)
		{
			if (!LoggedIn)
				throw new ApplicationException("user is not logged in.");

			var request = MakeParsePutRequest("classes/books/" + bookObjectId);
			request.AddParameter("application/json", metadataJson, ParameterType.RequestBody);

			var response = RestClientForParse.Execute(request);

			if (response.StatusCode != HttpStatusCode.OK)
				throw new ApplicationException("TestOnly_UpdateBookRecord: " + response.StatusDescription + " " + response.Content);
			return response;
		}

		public IRestResponse TestOnly_CreateBookRecord(string metadataJson)
		{
			if (!LoggedIn)
				throw new ApplicationException("user is not logged in.");

			var request = MakeParsePostRequest("classes/books");
			request.AddParameter("application/json", metadataJson, ParameterType.RequestBody);

			var response = RestClientForParse.Execute(request);

			if (response.StatusCode != HttpStatusCode.Created)
			{
				var message = new StringBuilder();
				message.AppendLine("Request.Json: " + metadataJson);
				message.AppendLine("Response.Code: " + response.StatusCode);
				message.AppendLine("Response.Uri: " + response.ResponseUri);
				message.AppendLine("Response.Description: " + response.StatusDescription);
				message.AppendLine("Response.Content: " + response.Content);
				throw new ApplicationException(message.ToString());
			}
			return response;
		}

		public void TestOnly_DeleteBookRecord(string bookObjectId)
		{
			if (!LoggedIn)
				throw new ApplicationException("Must be logged in to delete book");

			var request = MakeParseDeleteRequest("classes/books/" + bookObjectId);
			var response = RestClientForParse.Execute(request);
			if (response.StatusCode != HttpStatusCode.OK)
				throw new ApplicationException(response.StatusDescription + " " + response.Content);
		}
	}
}
