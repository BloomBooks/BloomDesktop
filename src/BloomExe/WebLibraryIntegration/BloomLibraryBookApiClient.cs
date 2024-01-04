using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Properties;
using Bloom.web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using SIL.Progress;

namespace Bloom.WebLibraryIntegration
{
    // This class began its life as BloomParseClient, and it encapsulated all the interactions with parse server.
    // But we are trying to move away from any direct interaction with parse server to favor our own bloomlibrary.org/api calls.
    // This will help facilitate moving away from parse server altogether in the future.
    // Why not just a new class which we start using now and gradually move things over to? Because this class already contains
    // things we need like logged-in status and the session token.
    // So we'll start with this and replace the parse-server-specific bits as we go.
    public class BloomLibraryBookApiClient
    {
        const string kHost = "https://api.bloomlibrary.org";

        //const string kHost = "http://localhost:7071"; // For local testing
        const string kVersion = "v1";
        const string kBookApiUrlPrefix = $"{kHost}/{kVersion}/book/";

        protected RestClient _azureRestClient;
        protected string _authenticationToken = String.Empty;
        protected string _userId;

        public BloomLibraryBookApiClient()
        {
            var keys = AccessKeys.GetAccessKeys(BookUpload.UploadBucketNameForCurrentEnvironment);
            _parseApplicationId = keys.ParseApplicationKey;
        }

        private void LogApiError(string apiEndpoint, IRestResponse response)
        {
            SIL.Reporting.Logger.WriteEvent(
                $@"BloomLibraryBookApiClient call to {apiEndpoint} failed
  StatusCode: {response.StatusCode}
  Content: {response.Content}
  ResponseStatus: {response.ResponseStatus}
  StatusDescription: {response.StatusDescription}
  ErrorMessage: {response.ErrorMessage}
  ErrorException: {response.ErrorException}
  Headers:{response.Headers}"
            );
        }

        public dynamic CallLongRunningAction(
            RestRequest request,
            IProgress progress,
            string messageToShowUserOnFailure,
            string endpointForFailureLog
        )
        {
            // Make the initial call. If all goes well, we get an Accepted (202) response with a URL to poll.
            var response = AzureRestClient.Execute(request);
            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                LogApiError(endpointForFailureLog, response);
                throw new ApplicationException(messageToShowUserOnFailure);
            }

            var operationLocation = response.Headers.FirstOrDefault(
                h => h.Name == "Operation-Location"
            );
            if (operationLocation == null)
            {
                LogApiError(endpointForFailureLog, response);
                throw new ApplicationException(messageToShowUserOnFailure);
            }

            // Poll the status URL until we get a terminal status
            string status = null;
            dynamic result = null;
            var statusRequest = new RestRequest(operationLocation.Value.ToString(), Method.GET);
            while (!progress.CancelRequested && !IsStatusTerminal(status))
            {
                response = AzureRestClient.Execute(statusRequest);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    LogApiError(endpointForFailureLog, response);
                    throw new ApplicationException(messageToShowUserOnFailure);
                }

                try
                {
                    dynamic responseContent = JObject.Parse(response.Content);
                    status = responseContent.status;
                    result = responseContent.result;
                }
                catch (Exception e)
                {
                    LogApiError(endpointForFailureLog, response);
                    SIL.Reporting.Logger.WriteEvent("Failed to parse response content.");
                    SIL.Reporting.Logger.WriteError(e);
                    throw new ApplicationException(messageToShowUserOnFailure);
                }

                if (status == "Succeeded")
                    return result;
                else if (status == "Failed" || status == "Cancelled")
                {
                    LogApiError(endpointForFailureLog, response);
                    throw new ApplicationException(messageToShowUserOnFailure);
                }

                int retryMilliseconds = 1000;
                try
                {
                    var retryAfter = response.Headers.FirstOrDefault(h => h.Name == "Retry-After");
                    if (retryAfter != null)
                        retryMilliseconds = int.Parse(retryAfter.Value.ToString()) * 1000;
                }
                catch
                {
                    // Just use the default.
                }

                Thread.Sleep(retryMilliseconds);
            }

            return null;
        }

        private bool IsStatusTerminal(string status)
        {
            // See https://github.com/microsoft/api-guidelines/blob/vNext/azure/ConsiderationsForServiceDesign.md#long-running-operations
            return new[] { "Succeeded", "Failed", "Canceled" }.Contains(status);
        }

        // This calls an azure function which does the following:
        // New book:
        //  - Creates an empty `books` record in parse-server with an uploadPendingTimestamp
        // Existing book:
        //  - Verifies the user has permission to update the book (using parse-server session)
        //  - Sets uploadPendingTimestamp on the `books` record in parse-server
        //  - Copies book files from existing S3 location to a new S3 location based on bookObjectId/timestamp
        // New and existing books:
        //  - Generates temporary credentials for the client to upload the book files to the new S3 location
        public (
            string transactionId,
            string storageKeyOfBookFolderParentOnS3,
            AmazonS3Credentials uploadCredentials
        ) InitiateBookUpload(IProgress progress, string existingBookObjectId = null)
        {
            if (!LoggedIn)
                throw new ApplicationException("Must be logged in to upload a book");

            var request = MakePostRequest("upload-start");

            if (!string.IsNullOrEmpty(existingBookObjectId))
                request.AddQueryParameter("existing-book-object-id", existingBookObjectId);

            var result = CallLongRunningAction(
                request,
                progress,
                messageToShowUserOnFailure: "Unable to initiate book upload on the server.",
                endpointForFailureLog: "upload-start"
            );

            if (progress.CancelRequested)
                return (null, null, null);

            return (
                result["transaction-id"],
                BloomS3Client.GetStorageKeyOfBookFolderParentFromUrl((string)result.url),
                new AmazonS3Credentials
                {
                    AccessKey = result.credentials.AccessKeyId,
                    SecretAccessKey = result.credentials.SecretAccessKey,
                    SessionToken = result.credentials.SessionToken
                }
            );
        }

        // This calls an azure function which does the following:
        //  - Verifies the user has permission to update the book (using parse-server session)
        //  - Verifies the baseUrl includes the expected S3 location
        //  - Sets up read permission on the files in the new S3 location
        //  - Updates the `books` record in parse-server with all fields from the client,
        //     including the new baseUrl which points to the new S3 location. Sets uploadPendingTimestamp to null.
        //  - Deletes the book files from the old S3 location
        public void FinishBookUpload(IProgress progress, string transactionId, string metadataJson)
        {
            if (!LoggedIn)
                throw new ApplicationException("Must be logged in to upload a book");

            var request = MakePostRequest("upload-finish");

            request.AddQueryParameter("transaction-id", transactionId);

            request.AddJsonBody(metadataJson);

            CallLongRunningAction(
                request,
                progress,
                messageToShowUserOnFailure: "Unable to finalize book upload on the server.",
                endpointForFailureLog: "upload-finish"
            );
        }

        protected RestClient AzureRestClient
        {
            get
            {
                if (_azureRestClient == null)
                {
                    _azureRestClient = new RestClient();
                }
                return _azureRestClient;
            }
        }

        private RestRequest MakeGetRequest(string action)
        {
            return MakeRequest(action, Method.GET);
        }

        private RestRequest MakePostRequest(string action)
        {
            return MakeRequest(action, Method.POST);
        }

        private RestRequest MakeRequest(string action, Method requestType)
        {
            string path = kBookApiUrlPrefix + action;
            var request = new RestRequest(path, requestType);
            SetCommonHeadersAndParameters(request);
            return request;
        }

        private void SetCommonHeadersAndParameters(RestRequest request)
        {
            if (!string.IsNullOrEmpty(_authenticationToken))
                request.AddHeader("Authentication-Token", _authenticationToken);

            if (Program.RunningUnitTests)
                request.AddQueryParameter("env", "unit-test");
            else if (BookUpload.UseSandbox)
                request.AddQueryParameter("env", "dev");
        }

        public void SetLoginData(
            string account,
            string parseUserObjectId,
            string sessionToken,
            string destination
        )
        {
            Account = account;
            Settings.Default.WebUserId = account;
            Settings.Default.LastLoginSessionToken = sessionToken;
            Settings.Default.LastLoginDest = destination;
            Settings.Default.LastLoginParseObjectId = parseUserObjectId;
            Settings.Default.Save();
            _userId = parseUserObjectId;
            _authenticationToken = sessionToken;
        }

        public bool AttemptSignInAgainForCommandLine(
            string userEmail,
            string destination,
            IProgress progress
        )
        {
            if (string.IsNullOrEmpty(Settings.Default.LastLoginSessionToken))
            {
                progress.WriteError(
                    "Please first log in from Bloom:Publish:Web, then quit and try again. (LastLoginSessionToken)"
                );
                return false;
            }
            if (string.IsNullOrEmpty(Settings.Default.LastLoginParseObjectId))
            {
                progress.WriteError(
                    "Please first log in from Bloom:Publish:Web, then quit and try again. (LastLoginParseObjectId)"
                );
                return false;
            }
            if (Settings.Default.WebUserId != userEmail)
            {
                progress.WriteError(
                    "The email from the last login from the Bloom UI does not match the -u argument."
                );
                return false;
            }
            if (Settings.Default.LastLoginDest != destination)
            {
                // this is important because the user settings we're going to read are from the version of Bloom, and so the
                // token will be whatever we logged into last here, and it won't work if it is from one Parse server and
                // we're using the other.
                progress.WriteError(
                    $"The destination of the last login from Bloom {ApplicationUpdateSupport.ChannelName} was '{Settings.Default.LastLoginDest}' which does not match the -d argument, '{destination}'"
                );
                return false;
            }

            SetLoginData(
                Settings.Default.WebUserId,
                Settings.Default.LastLoginParseObjectId,
                Settings.Default.LastLoginSessionToken,
                destination
            );

            return true;
        }

        protected RestClient _parseRestClient;
        protected RestClient ParseRestClient
        {
            get
            {
                if (_parseRestClient == null)
                {
                    _parseRestClient = new RestClient(GetRealUrl());
                }
                return _parseRestClient;
            }
        }

        // Don't even THINK of making this mutable so each unit test uses a different class.
        // Those classes hang around, can only be deleted manually, and eventually use up a fixed quota of classes.
        protected const string ClassesLanguagePath = "classes/language";

        public string UserId
        {
            get { return _userId; }
        }

        public string Account { get; protected set; }

        public bool LoggedIn => !string.IsNullOrEmpty(_authenticationToken);

        public string GetRealUrl()
        {
            return UrlLookup.LookupUrl(UrlType.Parse, null, BookUpload.UseSandbox);
        }

        protected RestRequest MakeParseRequest(string path, Method requestType)
        {
            // client.Authenticator = new HttpBasicAuthenticator(username, password);
            var request = new RestRequest(path, requestType);
            SetParseCommonHeaders(request);
            if (!string.IsNullOrEmpty(_authenticationToken))
                request.AddHeader("X-Parse-Session-Token", _authenticationToken);
            return request;
        }

        protected RestRequest MakeParseGetRequest(string path)
        {
            return MakeParseRequest(path, Method.GET);
        }

        private string _parseApplicationId;

        private void SetParseCommonHeaders(RestRequest request)
        {
            request.AddHeader("X-Parse-Application-Id", _parseApplicationId);
        }

        protected RestRequest MakeParsePostRequest(string path)
        {
            return MakeParseRequest(path, Method.POST);
        }

        /// <summary>
        /// Get the number of books on bloomlibrary.org that are in the given language.
        /// </summary>
        /// <remarks>Query should get all books where the isoCode matches the given languageCode
        /// and 'rebrand' is not true and 'inCirculation' is not false and 'draft' is not true.</remarks>
        public int GetBookCountByLanguage(string languageCode)
        {
            if (!UrlLookup.CheckGeneralInternetAvailability(false))
                return -1;
            var request = MakeGetRequest("get-book-count-by-language");
            request.AddQueryParameter("language-tag", languageCode);
            var response = AzureRestClient.Execute(request);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                LogApiError("get-book-count-by-language", response);
                return -1;
            }
            try
            {
                return int.Parse(response.Content);
            }
            catch (Exception e)
            {
                SIL.Reporting.Logger.WriteEvent("get-book-count-by-language failed: " + e.Message);
                return -1;
            }
        }

        // Setting param 'includeLanguageInfo' to true adds a param to the query that causes it to fold in
        // useful language information instead of only having the arcane langPointers object.
        private IRestResponse GetBookRecordsByQuery(string query, bool includeLanguageInfo)
        {
            var request = MakeParseGetRequest("classes/books");
            request.AddParameter("where", query, ParameterType.QueryString);
            if (includeLanguageInfo)
            {
                request.AddParameter("include", "langPointers", ParameterType.QueryString);
            }
            return ParseRestClient.Execute(request);
        }

        // Will throw an exception if there is any reason we can't make a successful query, including if there is no internet.
        public dynamic GetSingleBookRecord(string id, bool includeLanguageInfo = false)
        {
            var json = GetBookRecords(id, includeLanguageInfo);
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
                return "\"uploader\":{\"__type\":\"Pointer\",\"className\":\"_User\",\"objectId\":\""
                    + UserId
                    + "\"}";
            }
        }

        // Query parse for books.
        // Will throw an exception if there is any reason we can't make a successful query, including if there is no internet.
        public dynamic GetBookRecords(
            string bookInstanceId,
            bool includeLanguageInfo,
            bool includeBooksFromOtherUploaders = false
        )
        {
            // For current usage of this method, we really need to know the difference between "no books found" and "we couldn't check".
            // So all paths which don't allow us to check need to throw.
            // Note that all this gets completely reworked in 5.7, so we don't have to live with this very long.

            if (!UrlLookup.CheckGeneralInternetAvailability(false))
            {
                SIL.Reporting.Logger.WriteEvent(
                    "Internet was unavailable when trying to get book records."
                );
                throw new ApplicationException(
                    "Unable to look up book records because there is no internet connection."
                );
            }

            var query = "{\"bookInstanceId\":\"" + bookInstanceId + "\"";
            if (!includeBooksFromOtherUploaders)
            {
                query += "," + UploaderJsonString;
            }
            query += "}";
            var response = GetBookRecordsByQuery(query, includeLanguageInfo);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                SIL.Reporting.Logger.WriteEvent(
                    $"Unable to query book records on parse.\n"
                        + $"query = {query}\n"
                        + $"response.StatusCode = {response.StatusCode}\n"
                        + $"response.Content = {response.Content}"
                );
                throw new ApplicationException("Unable to look up book records.");
            }

            dynamic json = JObject.Parse(response.Content);
            if (json == null)
            {
                SIL.Reporting.Logger.WriteEvent(
                    $"Unable to parse book records query result.\n"
                        + $"response.Content = {response.Content}"
                );
                throw new ApplicationException("Unable to look up book records.");
            }

            return json.results;
        }

        public void Logout(bool includeFirebaseLogout = true)
        {
            Settings.Default.WebUserId = ""; // Should not be able to log in again just by restarting
            _authenticationToken = null;
            Account = "";
            _userId = "";
            if (includeFirebaseLogout)
                BloomLibraryAuthentication.Logout();
        }

        internal bool IsThisVersionAllowedToUpload()
        {
            var request = MakeParseGetRequest("classes/version");
            var response = ParseRestClient.Execute(request);
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
        /// Query the parse server for the status of the given books.  The returned dictionary will have
        /// an entry for each book that has been uploaded to the parse server.  The keys are the book ids
        /// from the BookInfo objects.
        /// Books with no entry in the dictionary have not been uploaded to Bloom Library.  Books that have
        /// multiple uploads with the same bookInstanceId are flagged as having a problem by having an empty
        /// string for the BloomLibraryStatus.BloomLibraryBookUrl field.  (The other fields are meaningless
        /// in that case.)
        /// </summary>
        /// <remarks>
        /// We want to minimize the number of queries we make to the parse server, so we batch up the book
        /// ids as much as possible.
        /// </remarks>
        public Dictionary<string, BloomLibraryStatus> GetLibraryStatusForBooks(
            List<BookInfo> bookInfos
        )
        {
            System.Diagnostics.Debug.WriteLine(
                $"DEBUG BloomParseClient.GetLibraryStatusForBooks(): {bookInfos.Count} books"
            );
            var bloomLibraryStatusesById = new Dictionary<string, BloomLibraryStatus>();
            if (!UrlLookup.CheckGeneralInternetAvailability(false))
                return bloomLibraryStatusesById;

            List<string> bookInstanceIds = bookInfos.Select(book => book.Id).ToList();
            var request = MakePostRequest("get-books");
            var requestBody = new { bookInstanceIds };
            request.AddJsonBody(requestBody);
            var response = AzureRestClient.Execute(request);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                LogApiError("get-books", response);
                return bloomLibraryStatusesById;
            }

            dynamic result = JObject.Parse(response.Content);
            for (int i = 0; i < result.bookRecords.Count; ++i)
            {
                var bookRecord = result.bookRecords[i];
                string bookInstanceId = bookRecord.bookInstanceId;
                if (bloomLibraryStatusesById.ContainsKey(bookInstanceId))
                {
                    bloomLibraryStatusesById[bookInstanceId] = new BloomLibraryStatus(
                        false,
                        false,
                        HarvesterState.Multiple,
                        BloomLibraryUrls.BloomLibraryBooksWithMatchingIdListingUrl(bookInstanceId)
                    );
                }
                else
                {
                    bloomLibraryStatusesById[bookInstanceId] = BloomLibraryStatus.FromDynamicJson(
                        bookRecord
                    );
                }
            }
            return bloomLibraryStatusesById;
        }
    }
}
