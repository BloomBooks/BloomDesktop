using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Properties;
using Bloom.web;
using L10NSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using SIL.Progress;

namespace Bloom.WebLibraryIntegration
{
    // To bubble up the fact that the exception was caused by the client being out of date.
    public class VersionCannotUploadException : ApplicationException
    {
        public VersionCannotUploadException(string message)
            : base(message) { }
    }

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
        const string kBookApiUrlPrefix = $"{kHost}/{kVersion}/books/";

        protected RestClient _azureRestClient;
        protected string _authenticationToken = String.Empty;
        protected string _userId;

        public BloomLibraryBookApiClient() { }

        private void LogApiError(
            IRestRequest request,
            IRestResponse response,
            IRestRequest originalRequest = null
        )
        {
            // Beware of logging the whole request, it could contain sensitive data (API keys)
            var logMessage = new StringBuilder();
            logMessage.AppendLine(
                $@"BloomLibraryBookApiClient call failed:
  Request:
    Resource: {request.Resource}
    Body: {request.Body?.Value}
  Response:
    StatusCode: {response.StatusCode}
    Content: {response.Content}
    ResponseStatus: {response.ResponseStatus}
    StatusDescription: {response.StatusDescription}
    ErrorMessage: {response.ErrorMessage}
    ErrorException: {response.ErrorException}
    Headers: "
            );
            foreach (var header in response.Headers)
                logMessage.AppendLine($"      {header.Name}: {header.Value}");

            if (originalRequest != null)
            {
                logMessage.AppendLine(
                    $@"  Original Request:
    Resource: {originalRequest.Resource}
    Body: {originalRequest.Body?.Value}"
                );
            }
            SIL.Reporting.Logger.WriteEvent(logMessage.ToString());
        }

        public dynamic CallLongRunningAction(
            RestRequest request,
            IProgress progress,
            string messageToShowUserOnFailure
        )
        {
            var originalEndpoint = request.Resource;

            // Make the initial call. If all goes well, we get an Accepted (202) response with a URL to poll.
            var response = AzureRestClient.Execute(request);
            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                LogApiError(request, response);
                throw new ApplicationException(messageToShowUserOnFailure);
            }

            var operationLocation = response.Headers.FirstOrDefault(h =>
                h.Name == "Operation-Location"
            );
            if (operationLocation == null)
            {
                LogApiError(request, response);
                throw new ApplicationException(messageToShowUserOnFailure);
            }

            // Poll the status URL until we get a terminal status
            string status = null;
            dynamic result = null;
            dynamic responseContentError = null;
            var statusRequest = new RestRequest(operationLocation.Value.ToString(), Method.GET);
            SIL.Reporting.Logger.WriteEvent(
                $@"BloomLibraryBookApiClient is beginning to poll for long-running {originalEndpoint}."
            );
            while (!progress.CancelRequested && !IsStatusTerminal(status))
            {
                response = AzureRestClient.Execute(statusRequest);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    LogApiError(statusRequest, response, originalRequest: request);
                    throw new ApplicationException(messageToShowUserOnFailure);
                }

                try
                {
                    dynamic responseContent = JObject.Parse(response.Content);
                    status = responseContent.status;
                    result = responseContent.result;
                    responseContentError = responseContent.error;
                }
                catch (Exception e)
                {
                    LogApiError(statusRequest, response, originalRequest: request);
                    SIL.Reporting.Logger.WriteEvent("Failed to parse response content.");
                    SIL.Reporting.Logger.WriteError(e);
                    throw new ApplicationException(messageToShowUserOnFailure);
                }

                if (status == "Succeeded")
                {
                    SIL.Reporting.Logger.WriteEvent(
                        $@"BloomLibraryBookApiClient call to {originalEndpoint} succeeded."
                    );
                    return result;
                }
                else if (status == "Failed" || status == "Cancelled")
                {
                    LogApiError(statusRequest, response, originalRequest: request);

                    // As of March 2024, this is not used, but we wanted to have a mechanism by which the API could give messages directly to the user.
                    // For example, we might shut down uploads for alpha temporarily. This might be used with or without the ClientOutOfDate error code.
                    // Note, we do not anticipate this message would be localized.
                    string messageIntendedForUser = responseContentError?.messageIntendedForUser;
                    if (!string.IsNullOrEmpty(messageIntendedForUser))
                    {
                        progress.WriteError(messageIntendedForUser);
                    }

                    string errorMessage;
                    string errorCode = responseContentError?.code;
                    if (errorCode == "ClientOutOfDate")
                    {
                        errorMessage = LocalizationManager.GetString(
                            "PublishTab.Upload.OldVersion",
                            "Sorry, this version of Bloom Desktop is not compatible with the current version of BloomLibrary.org. Please upgrade to a newer version."
                        );
                        throw new VersionCannotUploadException(errorMessage);
                    }
                    else
                    {
                        errorMessage = messageToShowUserOnFailure;
                    }
                    throw new ApplicationException(errorMessage);
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
        //  - Creates an empty `books` record in database with an uploadPendingTimestamp
        // Existing book:
        //  - Verifies the user has permission to update the book (using session object in the database)
        //  - Sets uploadPendingTimestamp on the `books` record in the database
        //  - Using the provided file paths and hashes, determines which files need to be copied from the existing
        //     S3 location and which need to be uploaded by the client
        //  - Copies book files from existing S3 location to a new S3 location based on bookObjectId/timestamp;
        //     sets public-read permissions on these files
        // New and existing books:
        //  - Generates temporary credentials for the client to upload the book files to the new S3 location
        public (
            string transactionId,
            AmazonS3Credentials uploadCredentials,
            string s3PrefixToUploadTo,
            string[] filesToUpload
        ) InitiateBookUpload(
            IProgress progress,
            List<FilePathAndHash> bookFiles,
            string bookTitle,
            string existingBookObjectId = null
        )
        {
            if (!LoggedIn)
                throw new ApplicationException("Must be logged in to upload a book");

            var request = MakePostRequest($"{existingBookObjectId ?? "new"}:upload-start");

            // We give the server a list of files and their hashes so it can be the controller of which files to upload.
            // body format is
            // { name: "My title", files: [{ "path": "abc", "hash": "123" }, ...], ... }
            var obj = new Dictionary<string, object>
            {
                // Because the title could change between uploads, we can't rely on the existing
                // path on S3 (which includes the title). We have to provide it ourselves.
                { "name", bookTitle },
                { "files", JsonConvert.SerializeObject(bookFiles) },
                { "clientName", $"Bloom {ApplicationUpdateSupport.ChannelName}" },
                { "clientVersion", Application.ProductVersion },
            };
            request.AddJsonBody(obj);

            progress.WriteStatus(LocalizationManager.GetString("Common.Preparing", "Preparing..."));
            var result = CallLongRunningAction(
                request,
                progress,
                messageToShowUserOnFailure: "Unable to initiate book upload on the server."
            );

            if (progress.CancelRequested)
                return (null, null, null, null);

            var s3PrefixToUploadTo = BloomS3Client.GetPrefixFromBookFileUploadUrl(
                (string)result.url
            );

            if (string.IsNullOrEmpty(s3PrefixToUploadTo))
                throw new ApplicationException("Unable to initiate book upload on the server.");

            if (!s3PrefixToUploadTo.EndsWith("/"))
                s3PrefixToUploadTo += "/";

            return (
                result["transactionId"],
                new AmazonS3Credentials
                {
                    AccessKey = result.credentials.AccessKeyId,
                    SecretAccessKey = result.credentials.SecretAccessKey,
                    SessionToken = result.credentials.SessionToken,
                },
                s3PrefixToUploadTo,
                result["filesToUpload"].ToObject<string[]>()
            );
        }

        // This calls an azure function which does the following:
        //  - Verifies the user has permission to update the book (using session object in the database)
        //  - Verifies the baseUrl includes the expected S3 location
        //  - Updates the `books` record in the database with all fields from the client,
        //     including the new baseUrl which points to the new S3 location. Sets uploadPendingTimestamp to null.
        //  - Deletes the book files from the old S3 location
        public void FinishBookUpload(
            IProgress progress,
            string transactionId,
            string metadataJsonAsString,
            bool becomeUploader
        )
        {
            if (!LoggedIn)
                throw new ApplicationException("Must be logged in to upload a book");

            // At this point in our implementation, the transaction ID is the same as the book ID.
            // That could change at some point in the future.
            // We use the book ID in the url; we send the transaction ID in the body.
            var bookId = transactionId;
            var request = MakePostRequest($"{bookId}:upload-finish");

            dynamic metadata = JsonConvert.DeserializeObject<ExpandoObject>(metadataJsonAsString);
            request.AddJsonBody(
                new
                {
                    transactionId,
                    metadata,
                    becomeUploader,
                }
            );

            progress.WriteStatus(LocalizationManager.GetString("Common.Finishing", "Finishing..."));
            CallLongRunningAction(
                request,
                progress,
                messageToShowUserOnFailure: "Unable to finalize book upload on the server."
            );
            progress.WriteStatus(LocalizationManager.GetString("Common.Finished", "Finished"));
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

        private RestRequest MakeGetRequest(string endpoint = "")
        {
            return MakeRequest(endpoint, Method.GET);
        }

        private RestRequest MakePostRequest(
            string endpoint = "",
            bool forceUseProductionData = false
        )
        {
            return MakeRequest(endpoint, Method.POST, forceUseProductionData);
        }

        // used by unit tests to clean up
        protected RestRequest MakeDeleteRequest(string endpoint = "")
        {
            return MakeRequest(endpoint, Method.DELETE);
        }

        private RestRequest MakeRequest(
            string endpoint,
            Method requestType,
            bool forceUseProductionData = false
        )
        {
            string path = kBookApiUrlPrefix + endpoint;
            var request = new RestRequest(path, requestType);
            SetCommonHeadersAndParameters(request, forceUseProductionData);
            return request;
        }

        private void SetCommonHeadersAndParameters(
            RestRequest request,
            bool forceUseProductionData = false
        )
        {
            if (!string.IsNullOrEmpty(_authenticationToken))
                request.AddHeader("Authentication-Token", _authenticationToken);

            if (Program.RunningUnitTests)
                request.AddQueryParameter("env", "unit-test");
            else if (BookUpload.UseSandbox && !forceUseProductionData)
                request.AddQueryParameter("env", "dev");
        }

        public void SetLoginData(
            string account,
            string userId,
            string sessionToken,
            string destination
        )
        {
            Account = account;
            Settings.Default.WebUserId = account;
            Settings.Default.LastLoginSessionToken = sessionToken;
            Settings.Default.LastLoginDest = destination;
            Settings.Default.LastLoginUserId = userId;
            Settings.Default.Save();
            _userId = userId;
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
            if (string.IsNullOrEmpty(Settings.Default.LastLoginUserId))
            {
                progress.WriteError(
                    "Please first log in from Bloom:Publish:Web, then quit and try again. (LastLoginUserId)"
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
                Settings.Default.LastLoginUserId,
                Settings.Default.LastLoginSessionToken,
                destination
            );

            return true;
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

        /// <summary>
        /// Get the number of books on bloomlibrary.org that are in the given language.
        /// </summary>
        /// <remarks>Query should get all books where the 'isoCode' matches the given languageTag
        /// and 'rebrand' is not true and 'inCirculation' is not false and 'draft' is not true.</remarks>
        public int GetBookCountByLanguage(string languageTag)
        {
            if (!UrlLookup.CheckGeneralInternetAvailability(false))
                return -1;
            var request = MakeGetRequest();
            request.AddQueryParameter("lang", languageTag);
            request.AddQueryParameter("limit", "0");
            request.AddQueryParameter("count", "true");
            var response = AzureRestClient.Execute(request);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                LogApiError(request, response);
                return -1;
            }
            try
            {
                dynamic rawResult = JObject.Parse(response.Content);
                return rawResult.count;
            }
            catch (Exception e)
            {
                SIL.Reporting.Logger.WriteEvent("GetBookCountByLanguage failed: " + e.Message);
                return -1;
            }
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

        public dynamic GetBookPermissions(string existingBookObjectId)
        {
            var request = MakeGetRequest($"{existingBookObjectId}:permissions");
            var response = AzureRestClient.Execute(request);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                LogApiError(request, response);
                throw new ApplicationException("Unable to get permissions.");
            }
            dynamic json = JObject.Parse(response.Content);
            if (json == null)
            {
                LogApiError(request, response);
                throw new ApplicationException("Unable to parse book permission response.");
            }
            return json;
        }

        // Query the API for books matching a particular ID.
        // Will throw an exception if there is any reason we can't make a successful query, including if there is no internet.
        public dynamic GetBookRecords(string bookInstanceId, bool includeLanguageInfo)
        {
            // For current usage of this method, we really need to know the difference between "no books found" and "we couldn't check".
            // So all paths which don't allow us to check need to throw.
            // Note that all this gets completely reworked in 6.0, so we don't have to live with this very long.

            if (!UrlLookup.CheckGeneralInternetAvailability(true))
            {
                SIL.Reporting.Logger.WriteEvent(
                    "Internet was unavailable when trying to get book records."
                );
                throw new ApplicationException(
                    "Unable to look up book records because there is no internet connection."
                );
            }

            var request = MakePostRequest();
            var requestBody = new { instanceIds = new[] { bookInstanceId } };

            // We can also ask to expand the uploader, but this just adds an id, and currently we don't need it
            // for anything.
            if (includeLanguageInfo)
                request.AddQueryParameter("expand", "languages");
            request.AddJsonBody(requestBody);
            var response = AzureRestClient.Execute(request);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                LogApiError(request, response);
                throw new ApplicationException("Unable to look up book records.");
            }
            dynamic rawResult = JObject.Parse(response.Content);
            if (rawResult == null)
            {
                LogApiError(request, response);
                throw new ApplicationException("Unable to parse book records.");
            }
            return rawResult.results;
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

        /// <summary>
        /// Query the API for the status of the given books. The returned dictionary will have
        /// an entry for each book that has been uploaded to Bloom Library. The keys are the book instance ids
        /// from the BookInfo objects.
        /// Books with no entry in the dictionary have not been uploaded to Bloom Library. Books that have
        /// multiple uploads with the same bookInstanceId are flagged as having a problem by having an empty
        /// string for the BloomLibraryStatus.BloomLibraryBookUrl field. (The other fields are meaningless
        /// in that case.)
        /// </summary>
        /// <remarks>
        /// We want to minimize the number of queries we make to the API, so we batch up the book instance
        /// ids as much as possible.
        /// </remarks>
        public Dictionary<string, BloomLibraryStatus> GetLibraryStatusForBooks(
            List<BookInfo> bookInfos,
            bool forceUseProductionData = false
        )
        {
            System.Diagnostics.Debug.WriteLine(
                $"DEBUG BloomLibraryBookApiClient.GetLibraryStatusForBooks(): {bookInfos.Count} books"
            );
            var bloomLibraryStatusesById = new Dictionary<string, BloomLibraryStatus>();
            if (!UrlLookup.CheckGeneralInternetAvailability(true))
                return bloomLibraryStatusesById;

            List<string> bookInstanceIds = bookInfos.Select(book => book.Id).ToList();
            var request = MakePostRequest("", forceUseProductionData);
            var requestBody = new { instanceIds = bookInstanceIds };
            request.AddJsonBody(requestBody);
            var response = AzureRestClient.Execute(request);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                LogApiError(request, response);
                return bloomLibraryStatusesById;
            }

            dynamic rawResult = JObject.Parse(response.Content);
            dynamic bookRecords = rawResult.results;
            for (int i = 0; i < bookRecords.Count; ++i)
            {
                var bookRecord = bookRecords[i];
                string bookInstanceId = bookRecord.instanceId;
                if (bookInstanceId != null)
                {
                    if (bloomLibraryStatusesById.ContainsKey(bookInstanceId))
                    {
                        bloomLibraryStatusesById[bookInstanceId] = new BloomLibraryStatus(
                            false,
                            false,
                            HarvesterState.Multiple,
                            BloomLibraryUrls.BloomLibraryBooksWithMatchingIdListingUrl(
                                bookInstanceId,
                                forceUseProductionData
                            )
                        );
                    }
                    else
                    {
                        bloomLibraryStatusesById[bookInstanceId] =
                            BloomLibraryStatus.FromDynamicJson(bookRecord, forceUseProductionData);
                    }
                }
            }
            return bloomLibraryStatusesById;
        }
    }
}
