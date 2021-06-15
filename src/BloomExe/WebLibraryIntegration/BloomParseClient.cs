﻿using System;
using System.Net;
using System.Text;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Properties;
using Bloom.web;
using Microsoft.CSharp.RuntimeBinder;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace Bloom.WebLibraryIntegration
{
	public class BloomParseClient
	{
		 RestClient _client;
		protected string _sessionToken = String.Empty;
		private string _userId;

		public BloomParseClient()
		{
			var keys = AccessKeys.GetAccessKeys(BookTransfer.UploadBucketNameForCurrentEnvironment);

			RestApiKey = keys.ParseApiKey;
			ApplicationId = keys.ParseApplicationKey;
		}

		public void SetLoginData(string account, string userId, string sessionToken)
		{
			Account = account;
			Settings.Default.WebUserId = account;
			Settings.Default.Save();
			_userId = userId;
			_sessionToken = sessionToken;
		}

		protected BloomParseClient(RestClient client)
		{
			_client = client;
		}

		protected RestClient Client
		{
			get
			{
				if (_client == null)
				{
					_client = new RestClient(GetRealUrl());
				}
				return _client;
			}
		}

		// REST key. Unit tests update these.
		public string RestApiKey { get; private set; }
		public string ApplicationId { get; protected set; }

		// Don't even THINK of making this mutable so each unit test uses a different class.
		// Those classes hang around, can only be deleted manually, and eventually use up a fixed quota of classes.
		protected const string ClassesLanguagePath = "classes/language";

		public string UserId {get { return _userId; }}

		public string Account { get; private set; }

		public bool LoggedIn
		{
			get
			{
				return !string.IsNullOrEmpty(_sessionToken);
			}
		}

		public string GetRealUrl()
		{
			return UrlLookup.LookupUrl(UrlType.Parse, BookTransfer.UseSandbox);
		}

		private RestRequest MakeRequest(string path, Method requestType)
		{
			// client.Authenticator = new HttpBasicAuthenticator(username, password);
			var request = new RestRequest(path, requestType);
			SetCommonHeaders(request);
			if (!string.IsNullOrEmpty(_sessionToken))
				request.AddHeader("X-Parse-Session-Token", _sessionToken);
			return request;
		}


		private RestRequest MakeGetRequest(string path)
		{
			return MakeRequest(path, Method.GET);
		}

		private void SetCommonHeaders(RestRequest request)
		{
			request.AddHeader("X-Parse-Application-Id", ApplicationId);
			request.AddHeader("X-Parse-REST-API-Key", RestApiKey); // REVIEW: Is this actually needed/used by our own parse-server? parse-server index.js suggests it is optional.
		}

		private RestRequest MakePostRequest(string path)
		{
			return MakeRequest(path, Method.POST);
		}

		private RestRequest MakePutRequest(string path)
		{
			return MakeRequest(path, Method.PUT);
		}
		private RestRequest MakeDeleteRequest(string path)
		{
			return MakeRequest(path, Method.DELETE);
		}

		public int GetBookCount()
		{
			var request = MakeGetRequest("classes/books");
			request.AddParameter("count", "1");
			request.AddParameter("limit", "0");
			var response = Client.Execute(request);
			var dy = JsonConvert.DeserializeObject<dynamic>(response.Content);
			return dy.count;
		}

		public IRestResponse GetBookRecordsByQuery(string query)
		{
			var request = MakeGetRequest("classes/books");
			request.AddParameter("where",query, ParameterType.QueryString);
			return Client.Execute(request);
		}

		public dynamic GetSingleBookRecord(string id)
		{
			var json = GetBookRecords(id);
			if (json == null || json.Count < 1)
				return null;

			return json[0];
		}

		/// <summary>
		/// The string that needs to be embedded in json, either to query for books uploaded by this user,
		/// or to specify that a book is. (But see the code in BookMetaData which is also involved in upload.)
		/// </summary>
		public string UploaderJsonString
		{
			get
			{
				return "\"uploader\":{\"__type\":\"Pointer\",\"className\":\"_User\",\"objectId\":\"" + UserId + "\"}";
			}
		}

		internal dynamic GetBookRecords(string id)
		{
			var response = GetBookRecordsByQuery("{\"bookInstanceId\":\"" + id + "\"," + UploaderJsonString + "}");
			if (response.StatusCode != HttpStatusCode.OK)
				return null;
			dynamic json = JObject.Parse(response.Content);
			if (json == null)
				return null;
			return json.results;
		}


		// Log in directly to parse server with name and password. This strategy is largely no longer used,
		// but some unit tests need it, since we haven't been able to get the new firebase login to work
		// except when Bloom is actually running. For the same reason, this is still used for command-line
		// uploads.
		public bool LegacyLogIn(string account, string password)
		{
			_sessionToken = String.Empty;
			Account = string.Empty;
			var request = MakeGetRequest("login");
			request.AddParameter("username", account.ToLowerInvariant());
			request.AddParameter("password", password);
			// a problem here is that behind the sil.org.pg captive portal, this hangs for a long while
			var response = Client.Execute(request);
			var dy = JsonConvert.DeserializeObject<dynamic>(response.Content);
			// behind sil.org.png, if the user isn't logged in to the captive portal,
			// at this point dy is null. Response.Content is "". repsonse.ErrorMessage is "Unable to connect to the remote server".
			try
			{
				_sessionToken = dy.sessionToken;//there's also an "error" in there if it fails, but a null sessionToken tells us all we need to know
			}
			catch (RuntimeBinderException)
			{
				// We are seeing this sometimes while running unit tests.
				// This is simply an attempt to diagnose what is happening.
				Console.WriteLine("Attempt to deserialize response content session token failed while attempting log in to parse (BL-4099).");
				Console.WriteLine("response.Content: {0}", response.Content);
				Console.WriteLine("deserialized: {0}", dy);
				throw;
			}
			_userId = dy.objectId;
			Account = account;
			return LoggedIn;
		}

		public void Logout()
		{
			Settings.Default.WebUserId = ""; // Should not be able to log in again just by restarting
			_sessionToken = null;
			Account = "";
			_userId = "";
			FirebaseLoginDialog.FirebaseLogout();
		}

		public IRestResponse CreateBookRecord(string metadataJson)
		{
			if (!LoggedIn)
				throw new ApplicationException();
			if (BookTransfer.IsDryRun)
				throw new ApplicationException("Should not call CreateBookRecord during dry run!");
			var request = MakePostRequest("classes/books");
			request.AddParameter("application/json", metadataJson, ParameterType.RequestBody);
			var response = Client.Execute(request);
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

		public IRestResponse SetBookRecord(string metadataJson)
		{
			if (!LoggedIn)
				throw new ApplicationException();
			if (BookTransfer.IsDryRun)
				throw new ApplicationException("Should not call SetBookRecord during dry run!");
			var metadata = BookMetaData.FromString(metadataJson);
			var book = GetSingleBookRecord(metadata.Id);
			metadataJson = ChangeJsonBeforeCreatingOrModifyingBook(metadataJson);
			if (book == null)
				return CreateBookRecord(metadataJson);

			var request = MakePutRequest("classes/books/" + book.objectId);
			request.AddParameter("application/json", metadataJson, ParameterType.RequestBody);
			var response = Client.Execute(request);
			if (response.StatusCode != HttpStatusCode.OK)
				throw new ApplicationException(response.StatusDescription + " " + response.Content);
			return response;
		}

		// Currently (April 2020), this is only used by unit tests
		public virtual string ChangeJsonBeforeCreatingOrModifyingBook(string json)
		{
			// No-op for base class
			return json;
		}

		/// <summary>
		/// Delete a book record in the parse server database
		/// Currently (April 2020), this is only used by unit tests
		/// </summary>
		public void DeleteBookRecord(string bookObjectId)
		{
			if (!LoggedIn)
				throw new ApplicationException("Must be logged in to delete book");
			if (BookTransfer.IsDryRun)
				throw new ApplicationException("Should not call DeleteBookRecord during dry run!");
			var request = MakeDeleteRequest("classes/books/" + bookObjectId);
			var response = Client.Execute(request);
			if (response.StatusCode != HttpStatusCode.OK)
				throw new ApplicationException(response.StatusDescription + " " + response.Content);
		}

		public void DeleteLanguages()
		{
			if (!LoggedIn)
				throw new ApplicationException();
			if (BookTransfer.IsDryRun)
				throw new ApplicationException("Should not call DeleteLanguages during dry run!");
			var getLangs = MakeGetRequest(ClassesLanguagePath);
			var response1 = Client.Execute(getLangs);
			dynamic json = JObject.Parse(response1.Content);
			if (json == null || response1.StatusCode != HttpStatusCode.OK)
				return;
			foreach (var obj in json.results)
			{
				var request = MakeDeleteRequest(ClassesLanguagePath + "/" + obj.objectId);
				var response = Client.Execute(request);
				if (response.StatusCode != HttpStatusCode.OK)
					throw new ApplicationException(response.StatusDescription + " " + response.Content);
			}
		}

		public dynamic CreateLanguage(LanguageDescriptor lang)
		{
			if (!LoggedIn)
				throw new ApplicationException();
			if (BookTransfer.IsDryRun)
				throw new ApplicationException("Should not call CreateLanguage during dry run!");
			var request = MakePostRequest(ClassesLanguagePath);
			var langjson = lang.Json;
			request.AddParameter("application/json", langjson, ParameterType.RequestBody);
			var response = Client.Execute(request);
			if (response.StatusCode != HttpStatusCode.Created)
			{
				var message = new StringBuilder();

				message.AppendLine("Request.Json: " + langjson);
				message.AppendLine("Response.Code: " + response.StatusCode);
				message.AppendLine("Response.Uri: " + response.ResponseUri);
				message.AppendLine("Response.Description: " + response.StatusDescription);
				message.AppendLine("Response.Content: " + response.Content);
				throw new ApplicationException(message.ToString());
			}
			return JObject.Parse(response.Content);
		}

		public bool LanguageExists(LanguageDescriptor lang)
		{
			return LanguageCount(lang) > 0;
		}

		internal int LanguageCount(LanguageDescriptor lang)
		{
			var getLang = MakeGetRequest(ClassesLanguagePath);
			getLang.AddParameter("where", lang.Json, ParameterType.QueryString);
			var response = Client.Execute(getLang);
			if (response.StatusCode != HttpStatusCode.OK)
				return 0;
			dynamic json = JObject.Parse(response.Content);
			if (json == null)
				return 0;
			var results = json.results;
			return results.Count;
		}

		internal string LanguageId(LanguageDescriptor lang)
		{
			var getLang = MakeGetRequest(ClassesLanguagePath);
			getLang.AddParameter("where", lang.Json, ParameterType.QueryString);
			var response = Client.Execute(getLang);
			if (response.StatusCode != HttpStatusCode.OK)
				return null;
			dynamic json = JObject.Parse(response.Content);
			if (json == null || json.results.Count < 1)
				return null;
			return json.results[0].objectId;
		}

		internal dynamic GetLanguage(string objectId)
		{
			var getLang = MakeGetRequest(ClassesLanguagePath + "/" + objectId);
			var response = Client.Execute(getLang);
			if (response.StatusCode != HttpStatusCode.OK)
				return null;
			return JObject.Parse(response.Content);
		}

		internal void SendResetPassword(string account)
		{
			if (BookTransfer.IsDryRun)
				throw new ApplicationException("Should not call SendResetPassword during dry run!");
			var request = MakePostRequest("requestPasswordReset");
			request.AddParameter("application/json; charset=utf-8", "{\"email\":\""+account+ "\"}", ParameterType.RequestBody);
			request.RequestFormat = DataFormat.Json;
			Client.Execute(request);
		}

		internal bool UserExists(string account)
		{
			var request = MakeGetRequest("users");
			request.AddParameter("where", "{\"username\":\"" + account.ToLowerInvariant() + "\"}");
			var response = Client.Execute(request);
			var dy = JsonConvert.DeserializeObject<dynamic>(response.Content);
			// Todo
			return dy.results.Count > 0;
		}

		internal bool IsThisVersionAllowedToUpload()
		{
			var request = MakeGetRequest("classes/version");
			var response = Client.Execute(request);
			var dy = JsonConvert.DeserializeObject<dynamic>(response.Content);
			var row = dy.results[0];
			string versionString = row.minDesktopVersion;
			var parts = versionString.Split('.');
			var requiredMajorVersion = int.Parse(parts[0]);
			var requiredMinorVersion = int.Parse(parts[1]);
			parts = Application.ProductVersion.Split('.');
			var ourMajorVersion = int.Parse(parts[0]);
			var ourMinorVersion = int.Parse(parts[1]);
			if (ourMajorVersion == requiredMajorVersion)
				return ourMinorVersion >= requiredMinorVersion;
			return ourMajorVersion >= requiredMajorVersion;
		}

		/// <summary>
		/// Get the language pointers we need to refer to a sequence of languages.
		/// If matching languages don't exist they will be created (requires user to be logged in)
		/// </summary>
		/// <param name="languages"></param>
		/// <returns></returns>
		internal ParseServerObjectPointer[] GetLanguagePointers(LanguageDescriptor[] languages)
		{
			var result = new ParseServerObjectPointer[languages.Length];
			for (int i = 0; i < languages.Length; i++)
			{
				var lang = languages[i];
				var id = LanguageId(lang);
				if (id == null)
				{
					var language = CreateLanguage(lang);
					id = language["objectId"].Value;
				}
				result[i] = new ParseServerObjectPointer() {ClassName = "language", ObjectId = id};
			}
			return result;
		}
	}
}
