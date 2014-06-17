using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Properties;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using RestSharp.Deserializers;

namespace Bloom.WebLibraryIntegration
{
    public class BloomParseClient
    {
        private string kBaseUrl="https://api.parse.com/1/";
        private readonly RestClient _client;
        private string _sessionToken;
	    private string _userId;

        public BloomParseClient()
        {
            _sessionToken = String.Empty;
             _client = new RestClient(kBaseUrl);
        }

		// REST key. Unit tests update these.
		public string ApiKey = KeyManager.ParseApiKey;
		public string ApplicationKey = KeyManager.ParseApplicationKey;

		public string UserId {get { return _userId; }}

		public string Account { get; private set; }

        public bool LoggedIn
        {
            get
            {
                return !string.IsNullOrEmpty(_sessionToken);
            }
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
		    request.AddHeader("X-Parse-Application-Id", ApplicationKey);
		    request.AddHeader("X-Parse-REST-API-Key", ApiKey);
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
            var response = _client.Execute(request);
            var dy = JsonConvert.DeserializeObject<dynamic>(response.Content);
            return dy.count;
        }

        public IRestResponse GetBookRecordsByQuery(string query)
        {
            var request = MakeGetRequest("classes/books");
            request.AddParameter("where",query, ParameterType.QueryString);
            return _client.Execute(request);
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


	    public bool LogIn(string account, string password)
        {
            _sessionToken = String.Empty;
		    Account = string.Empty;
            var request = MakeGetRequest("login");
            request.AddParameter("username", account);
            request.AddParameter("password", password);
            var response = _client.Execute(request);
            var dy = JsonConvert.DeserializeObject<dynamic>(response.Content);
            _sessionToken = dy.sessionToken;//there's also an "error" in there if it fails, but a null sessionToken tells us all we need to know
	        _userId = dy.objectId;
		    Account = account;
            return LoggedIn;
        }

	    public void Logout()
	    {
		    Settings.Default.WebUserId = ""; // Should not be able to log in again just by restarting
		    Settings.Default.WebPassword = "";
		    _sessionToken = null;
		    Account = "";
		    _userId = "";
	    }

        public IRestResponse CreateBookRecord(string metadataJson)
        {
            if (!LoggedIn)
                throw new ApplicationException();
            var request = MakePostRequest("classes/books");
            request.AddParameter("application/json", metadataJson, ParameterType.RequestBody);
            var response = _client.Execute(request);
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
			var metadata = BookMetaData.FromString(metadataJson);
			var book = GetSingleBookRecord(metadata.Id);
			if (book == null)
				return CreateBookRecord(metadataJson);

			var request = MakePutRequest("classes/books/" + book.objectId);
			request.AddParameter("application/json", metadataJson, ParameterType.RequestBody);
			var response = _client.Execute(request);
			if (response.StatusCode != HttpStatusCode.OK)
				throw new ApplicationException(response.StatusDescription + " " + response.Content);
			return response;
		}

	    public void CreateUser(string account, string password)
	    {
		    var request = MakePostRequest("users");
		    var metadataJson =
			    "{\"username\":\"" + account + "\",\"password\":\"" + password + "\",\"email\":\"" + account + "\"}";
			request.AddParameter("application/json", metadataJson, ParameterType.RequestBody);
			var response = _client.Execute(request);
			if (response.StatusCode != HttpStatusCode.Created)
				throw new ApplicationException(response.StatusDescription + " " + response.Content);
		}

	    public void DeleteCurrentUser()
	    {
			if (!LoggedIn)
				throw new ApplicationException("Must be logged in to delete current user");
			var request = MakeDeleteRequest("users/" + _userId);
			var response = _client.Execute(request);
			if (response.StatusCode != HttpStatusCode.OK)
				throw new ApplicationException(response.StatusDescription + " " + response.Content);
		    _sessionToken = null;
		    _userId = null;
	    }

        public void DeleteLanguages()
        {
            if (!LoggedIn)
                throw new ApplicationException();
            var getLangs = MakeGetRequest("classes/language");
            var response1 = _client.Execute(getLangs);
            dynamic json = JObject.Parse(response1.Content);
            if (json == null)
                return;
            foreach (var obj in json.results)
            {
                var request = MakeDeleteRequest("classes/language/" + obj.objectId);
                var response = _client.Execute(request);
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new ApplicationException(response.StatusDescription + " " + response.Content);
            }
        }

        public dynamic CreateLanguage(LanguageDescriptor lang)
        {
            if (!LoggedIn)
                throw new ApplicationException();
            var request = MakePostRequest("classes/language");
            var langjson = lang.Json;
            request.AddParameter("application/json", langjson, ParameterType.RequestBody);
            var response = _client.Execute(request);
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
            var getLang = MakeGetRequest("classes/language");
            getLang.AddParameter("where", lang.Json, ParameterType.QueryString);
            var response = _client.Execute(getLang);
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
            var getLang = MakeGetRequest("classes/language");
            getLang.AddParameter("where", lang.Json, ParameterType.QueryString);
            var response = _client.Execute(getLang);
            if (response.StatusCode != HttpStatusCode.OK)
                return null;
            dynamic json = JObject.Parse(response.Content);
            if (json == null || json.results.Count < 1)
                return null;
            return json.results[0].objectId;
        }

        internal dynamic GetLanguage(string objectId)
        {
            var getLang = MakeGetRequest("classes/language/" + objectId);
            var response = _client.Execute(getLang);
            if (response.StatusCode != HttpStatusCode.OK)
                return null;
            return JObject.Parse(response.Content);
        }

        internal void SendResetPassword(string account)
		{
			var request = MakePostRequest("requestPasswordReset");
			request.AddParameter("application/json; charset=utf-8", "{\"email\":\""+account+ "\"}", ParameterType.RequestBody);
			request.RequestFormat = DataFormat.Json;
			_client.Execute(request);
		}

	    internal bool UserExists(string account)
	    {
		    var request = MakeGetRequest("users");
			request.AddParameter("where", "{\"username\":\"" + account + "\"}");
			var response = _client.Execute(request);
			var dy = JsonConvert.DeserializeObject<dynamic>(response.Content);
			// Todo
		    return dy.results.Count > 0;
	    }

        internal bool IsThisVersionAllowedToUpload()
        {
            var request = MakeGetRequest("classes/version");
            var response = _client.Execute(request);
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
        internal ParseDotComObjectPointer[] GetLanguagePointers(LanguageDescriptor[] languages)
        {
            var result = new ParseDotComObjectPointer[languages.Length];
            for (int i = 0; i < languages.Length; i++)
            {
                var lang = languages[i];
                var id = LanguageId(lang);
                if (id == null)
                {
                    var language = CreateLanguage(lang);
                    id = language["objectId"].Value;
                }
                result[i] = new ParseDotComObjectPointer() {ClassName = "language", ObjectId = id};
            }
            return result;
        }
    }
}
