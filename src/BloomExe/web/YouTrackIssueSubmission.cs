using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Bloom.web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIL.IO;
using SIL.Reporting;
using HttpClient = System.Net.Http.HttpClient;

namespace Bloom
{
	/// <summary>
	/// Simplifies the process of submitting issues to our issue tracking system.
	/// Usage: 1) initialize the process via the ctor, 2) add attachments, 3) submit.
	/// Exception: We don't get an issue id until we submit, so we have a way to add an attachment after submitting
	/// so that we can name our book zip file after the issue.
	/// </summary>
	/// <remarks>
	/// This has been updated to use the REST API documented for version 2020.1 of YouTrack standalone.
	/// Unfortunately, YouTrackSharp 2020.1 does not use the current REST API in its methods, and chokes
	/// on the long description fields that we send for new issues.  So the code here uses raw HttpClient
	/// accesses to implement the parts of the REST API that we need to use.
	///
	/// Comments around the web indicate that it's usually best to use a static instance of HttpClient.
	/// </remarks>
	public class YouTrackIssueSubmitter
	{
		private const string TokenPiece1 = @"YXV0b19yZXBvcnRfY3JlYXRvcg==.NzQtMA==.V9k0yNUN7Df5eqo4QEk5N4BBKqmEHV";
		private static readonly HttpClient _client = new HttpClient();

		private readonly string _youTrackProjectKey;
		private readonly string _youTrackBaseSite;
		private readonly List<string> _filesToAttach = new List<string>();

		/// <summary>
		/// A new instance should be constructed for every issue.
		/// </summary>
		public YouTrackIssueSubmitter(string projectKey)
		{
			_youTrackProjectKey = projectKey;
			// Note that the headers set here apply to all following calls using _client.
			// So this is the only place we need to set the authorization value and accept value.
			// (Authorization applies to all GET, POST, and DELETE requests.  Accept applies to
			// all GET requests, which are always JSON the way the REST API works in what we use.)
			var permission = $"Bearer perm:{TokenPiece1}";
			IEnumerable<string> authorizations;
			lock (_client)
			{
				var hasAuthorization = _client.DefaultRequestHeaders.TryGetValues("Authorization", out authorizations);
				if (!hasAuthorization || !authorizations.Contains(permission))
					_client.DefaultRequestHeaders.Add("Authorization", permission);
				var appJson = "application/json";
				IEnumerable<string> accepts;
				var hasAccept = _client.DefaultRequestHeaders.TryGetValues("Accept", out accepts);
				if (!hasAccept || !accepts.Contains(appJson))
					_client.DefaultRequestHeaders.Add("Accept", appJson);
			}
			// Get the base website address for our YouTrack instance.
			_youTrackBaseSite = UrlLookup.LookupUrl(UrlType.IssueTrackingSystemBackend, false, true);
		}

		/// <summary>
		/// Verify a file's existence and store the filename to attach later.
		/// We may not be able to attach now, because we don't have an actual issue Id until we submit the issue
		/// and we can't guarantee that the caller will not try to AddAttachment() before calling SubmitToYouTrack().
		/// </summary>
		/// <param name="filePath"></param>
		public void AddAttachmentWhenWeHaveAnIssue(string filePath)
		{
			if (!RobustFile.Exists(filePath))
			{
				Logger.WriteEvent("YouTrack issue submitter failed to attach non-existent file: " + filePath);
				return;
			}
			_filesToAttach.Add(filePath);
		}

		/// <summary>
		/// Using YouTrackSharp here. We can't submit
		/// the report as if it were from this person, even if they have an account (well, not without
		/// asking them for credentials, which is just not gonna happen). So we submit with an
		/// account we created just for this purpose, "auto_report_creator".  (This account is the owner
		/// of the permission token that we now use for authentication.)
		/// </summary>
		/// <remarks>
		/// https://www.jetbrains.com/help/youtrack/standalone/api-howto-create-issue.html
		/// </remarks>
		public string SubmitToYouTrack(string summary, string description)
		{
			string youTrackIssueId = "failed";
			try
			{
				// 1. Get the numeric id of the project whose key we've been given in the form needed for
				//    the JSON data used in creating the new issue.
				var projectInfoString = _client.GetStringAsync($"https://{_youTrackBaseSite}/youtrack/api/admin/projects/{_youTrackProjectKey}").Result;
				dynamic projectInfo = JsonConvert.DeserializeObject(projectInfoString);
				projectInfo.Remove("$type");	// remove $type since we don't need it, but it's always given to us.
				// 2. Create the dynamic object to send as JSON to youtrack.
				dynamic youTrackIssue = new JObject();
				youTrackIssue.project = projectInfo;
				youTrackIssue.summary = summary;
				youTrackIssue.description = description;
				youTrackIssue.customFields = new JArray();
				dynamic type = new JObject();
				type.name = "Type";
				type.value = new JObject();
				type.value.name = "Awaiting Classification";
				type.value["$type"] = "EnumBundleElement";
				type["$type"] = "SingleEnumIssueCustomField";
				youTrackIssue.customFields.Add(type);
				// 3. Convert the dynamic JObject to a JSON string and send it to youtrack to create
				//    the new issue.
				var issueJson = JsonConvert.SerializeObject(youTrackIssue);
				HttpContent content = new StringContent(issueJson);
				content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
				var response = _client.PostAsync($"https://{_youTrackBaseSite}/youtrack/api/issues?fields=idReadable,id", content).Result;
				if (response.IsSuccessStatusCode)
				{
					var issueSettingString = response.Content.ReadAsStringAsync().Result;
					dynamic issueSetting = JsonConvert.DeserializeObject(issueSettingString);
					if (issueSetting.idReadable != null)
					{
						youTrackIssueId = issueSetting.idReadable;
						// Now that we have an issue Id, attach any files.
						AttachFiles(youTrackIssueId);
					}
				}
			}
			catch (WebException e)
			{
				// Some sort of internet failure
				Console.WriteLine(e);
			}
			return youTrackIssueId;
		}

		private void AttachFiles(string youTrackIssueId)
		{
			foreach (var filename in _filesToAttach)
			{
				AttachFileToExistingIssue(youTrackIssueId, filename);
			}
		}

		/// <summary>
		/// This is the only way to add a zip file of the book's contents that is named after the issue.
		/// </summary>
		/// <remarks>
		/// https://www.jetbrains.com/help/youtrack/standalone/api-usecase-attach-files.html
		/// </remarks>
		public bool AttachFileToExistingIssue(string youTrackIssueId, string filePath)
		{
			if (!RobustFile.Exists(filePath))
			{
				Logger.WriteEvent("YouTrack issue submitter failed to attach non-existent file: " + filePath);
				return false;
			}

			var fileName = Path.GetFileName(filePath);
			HttpContent content = new MultipartFormDataContent
			{
				{new ByteArrayContent(File.ReadAllBytes(filePath)), fileName, fileName}
			};
			HttpResponseMessage response = null;
			string problem = null;
			try
			{
				response = _client
					.PostAsync(
						$"https://{_youTrackBaseSite}/youtrack/api/issues/{youTrackIssueId}/attachments?fields=id,name",
						content).Result;
				if (!response.IsSuccessStatusCode)
				{
					problem = response.ReasonPhrase;
				}
			}
			catch (Exception e)
			{
				problem = e.Message;
			}

			if (!string.IsNullOrEmpty(problem))
			{
				var msg = "***Error as ProblemReportApi attempted to upload the file: " + filePath
					+ Environment.NewLine + problem;
				Logger.WriteEvent(msg);
			}

			return string.IsNullOrEmpty(problem);
		}

		public bool UpdateSummaryAndDescription(string youTrackIssueId, string summary, string description)
		{
			HttpResponseMessage response = null;
			string problem = null;
			try
			{
				dynamic data = new JObject();
				data.summary = summary;
				data.description = description;
				var dataJson = JsonConvert.SerializeObject(data);
				HttpContent content = new StringContent(dataJson);
				content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
				response = _client
					.PostAsync(
						$"https://{_youTrackBaseSite}/youtrack/api/issues/{youTrackIssueId}?fields=summary,description",
						content).Result;
				if (!response.IsSuccessStatusCode)
				{
					problem = response.ReasonPhrase;
				}
			}
			catch (Exception e)
			{
				problem = e.Message;
			}

			if (!string.IsNullOrEmpty(problem))
			{
				var msg = "***Error as ProblemReportApi attempted to update summary and description: " + problem;
				Logger.WriteEvent(msg);
			}

			return string.IsNullOrEmpty(problem);
		}

		#region Unit test methods

		/// <summary>
		/// Delete an issue.  Only unit test issues can be deleted: other issues are silently left in place.
		/// </summary>
		/// <remarks>
		/// https://www.jetbrains.com/help/youtrack/standalone/operations-api-issues.html#delete-Issue-method
		/// </remarks>
		public bool DeleteIssue(string issueId)
		{
			if (!issueId.StartsWith("AUT-"))
			{
				return false;
			}
			var response = _client.DeleteAsync($"https://{_youTrackBaseSite}/youtrack/api/issues/{issueId}").Result;
			return response.IsSuccessStatusCode;
		}

		/// <summary>
		/// Get the names of the attachments for the given issue.  Only unit test issues can be queried: other issues return null.
		/// </summary>
		/// <remarks>
		/// https://www.jetbrains.com/help/youtrack/standalone/operations-api-issues.html#get-Issue-method
		/// https://www.jetbrains.com/help/youtrack/standalone/api-entity-IssueAttachment.html
		/// </remarks>
		public Dictionary<string,int> GetAttachmentDataForIssue(string issueId)
		{
			if (!issueId.StartsWith("AUT-"))
			{
				return null;
			}
			var response = _client
				.GetAsync(
					$"https://{_youTrackBaseSite}/youtrack/api/issues/{issueId}?fields=idReadable,attachments(id,name,size)")
				.Result;
			if (!response.IsSuccessStatusCode)
				return null;
			var resultString  = response.Content.ReadAsStringAsync().Result;
			dynamic result = JsonConvert.DeserializeObject(resultString);
			var filesAndSizes = new Dictionary<string,int>();
			foreach (dynamic attach in result.attachments)
			{
				string name = attach.name;
				int size = attach.size;
				filesAndSizes.Add(name, size);
			}
			return filesAndSizes;
		}

		#endregion
	}
}
