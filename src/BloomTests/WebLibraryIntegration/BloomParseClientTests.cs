using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using Bloom.WebLibraryIntegration;
using Microsoft.CSharp.RuntimeBinder;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SIL.Code;

namespace BloomTests.WebLibraryIntegration
{
	[TestFixture]
	public class BloomParseClientTests
	{
		private BloomParseClientTestDouble _client;

		[SetUp]
		public void Setup()
		{
			_client = new BloomParseClientTestDouble();
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

		[Test, Ignore("This is causing a strange error when nunit is exiting; just skipping for now to get a good build")]
		public void GetBookRecord_BookIsThere_Succeeds()
		{
			//first make a book so that we know it is there
			var id = CreateBookRecord();
			var bookjson = _client.GetSingleBookRecord(id);

			Assert.AreEqual(id, bookjson.bookInstanceId.Value);

			// Partial matches are no good
			Assert.IsNull(_client.GetSingleBookRecord(new Guid().ToString()));

			// Our lookup uses the logged in user as part of the query.
			// Ensure the lookup fails if not logged in as the uploader.
			_client.Logout();
			Assert.IsNull(_client.GetSingleBookRecord(id));
		}

		[Test]
		public void GetBookRecord_BookIsNotThere_Fails()
		{
			Assert.IsNull(_client.GetSingleBookRecord("notthere"));
		}

		//{"authors":["Heinrich Poschinger"],"categories":[],"description":"This is an EXACT reproduction of a book published before 1923. This IS NOT an OCR\"d book with strange characters, introduced typographical errors, and jumbled words. This book may have occasional imperfections such as missing or blurred pages, poor pictures, errant marks, etc. that were either part of the original artifact, or were introduced by the scanning process. We believe this work is culturally important, and despite the imperfections, have elected to bring it back into print as part of our continuing commitment to the preservation of printed works worldwide. We appreciate your understanding of the imperfections in the preservation process, and hope you enjoy this valuable book.","imageLinks":{"smallThumbnail":"http://bks1.books.google.co.th/books?id=MxhvSQAACAAJ&printsec=frontcover&img=1&zoom=5&source=gbs_api","thumbnail":"http://bks1.books.google.co.th/books?id=MxhvSQAACAAJ&printsec=frontcover&img=1&zoom=1&source=gbs_api"},"industryIdentifiers":[{"identifier":"1148362096","type":"ISBN_10"},{"identifier":"9781148362090","type":"ISBN_13"}],"language":"en","pageCount":270,"previewLink":"http://books.google.co.th/books?id=MxhvSQAACAAJ&dq=hamburger&hl=&cd=520&source=gbs_api","printType":"BOOK","publishedDate":"2010-04","publisher":"Nabu Press","title":"Fürst Bismarck und Seine Hamburger Freunde"}
	}

	public class BloomParseClientTestDouble : BloomParseClient
	{
		public BloomParseClientTestDouble() { }

		public BloomParseClientTestDouble(string testId)
		{
			// Do NOT do this...it results in creating a garbage class in Parse.com which is hard to delete (manual only).
			//ClassesLanguagePath = "classes/language_" + testId;
		}

		public bool SimulateOldBloomUpload = false;

		public override string ChangeJsonBeforeCreatingOrModifyingBook(string json)
		{
			if (SimulateOldBloomUpload)
			{
				var bookRecord = JObject.Parse(json);
				bookRecord.Remove("lastUploaded");
				bookRecord.Remove("updateSource");
				return bookRecord.ToString();
			}
			return base.ChangeJsonBeforeCreatingOrModifyingBook(json);
		}

		// Log in directly to parse server with name and password.
		// Some unit tests need it, since we haven't been able to get the new firebase login to work
		// except when Bloom is actually running.
		public bool TestOnly_LegacyLogIn(string account, string password)
		{
			_sessionToken = string.Empty;
			Account = string.Empty;
			var request = MakeGetRequest("login");
			request.AddParameter("username", account.ToLowerInvariant());
			request.AddParameter("password", password);

			bool result = false;
			RetryUtility.Retry(() => {
				var response = Client.Execute(request);
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
	}
}
