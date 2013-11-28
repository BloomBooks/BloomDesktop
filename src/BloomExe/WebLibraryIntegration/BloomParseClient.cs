using System;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using RestSharp.Deserializers;

namespace Bloom.WebLibraryIntegration
{
    public class BloomParseClient
    {
        private string _apiKey;
        private string kBaseUrl="https://api.parse.com/1/";
        private readonly RestClient _client;
        private string _sessionToken;

        public BloomParseClient()
        {
            _sessionToken = String.Empty; 
            string kEnvironmentVariableName = "BLOOM_PARSE_API_KEY_TESTING";
            _apiKey = Environment.GetEnvironmentVariable(kEnvironmentVariableName, EnvironmentVariableTarget.User);
            if (string.IsNullOrEmpty(_apiKey))
                throw new ApplicationException("For now, developers, we need to put the parse api key in an *user* environment variable named " + kEnvironmentVariableName);
            _client = new RestClient(kBaseUrl);
        }

        public bool LoggedIn
        {
            get
            {
                return !string.IsNullOrEmpty(_sessionToken);
            }
        }


        private RestRequest MakeGetRequest(string path)
        {
            // client.Authenticator = new HttpBasicAuthenticator(username, password);
            var request = new RestRequest(path, Method.GET);
            request.AddHeader("X-Parse-Application-Id", "R6qNTeumQXjJCMutAJYAwPtip1qBulkFyLefkCE5");
            request.AddHeader("X-Parse-REST-API-Key", _apiKey);
            if(!string.IsNullOrEmpty(_sessionToken))
                request.AddHeader("X-Parse-Session-Token", _sessionToken);
            return request;
        }
        private RestRequest MakePostRequest(string path)
        {
            // client.Authenticator = new HttpBasicAuthenticator(username, password);
            var request = new RestRequest(path, Method.POST);
            request.RequestFormat = DataFormat.Json;
            request.AddHeader("X-Parse-Application-Id", "R6qNTeumQXjJCMutAJYAwPtip1qBulkFyLefkCE5");
            request.AddHeader("X-Parse-REST-API-Key", _apiKey);
            if (!string.IsNullOrEmpty(_sessionToken))
                request.AddHeader("X-Parse-Session-Token", _sessionToken);
            return request;
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

        public IRestResponse GetBookRecords(string query)
        {
            var request = MakeGetRequest("classes/books");
            request.AddParameter("where",query, ParameterType.QueryString);
            return _client.Execute(request);
        }

        public dynamic GetSingleBookRecord(string bookInstanceId)
        {
            var response= GetBookRecords("{\"bookInstanceId\":\"" + bookInstanceId + "\"}");
            if (response.StatusCode != HttpStatusCode.OK)
                return null;

            dynamic json = JObject.Parse(response.Content);
            if (json.results.Count == 0)
                return null;
            return json.results[0];
        }


        public bool LogIn(string account, string password)
        {
            _sessionToken = String.Empty;
            var request = MakeGetRequest("login");
            request.AddParameter("username", account);
            request.AddParameter("password", password);
            var response = _client.Execute(request);
            var dy = JsonConvert.DeserializeObject<dynamic>(response.Content);
            _sessionToken = dy.sessionToken;//there's also an "error" in there if it fails, but a null sessionToken tells us all we need to know
            return LoggedIn;
        }

        public IRestResponse CreateBookRecord(string metadataJson)
        {
            if (!LoggedIn)
                throw new ApplicationException();
            var request = MakePostRequest("classes/books");
            request.AddParameter("application/json", metadataJson, ParameterType.RequestBody);
            var response = _client.Execute(request);
            if(response.StatusCode!=HttpStatusCode.Created)
                throw new ApplicationException(response.StatusDescription+" "+response.Content);
            return response;
        }
    }
}
