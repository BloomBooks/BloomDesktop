
using System;
using System.Collections.Generic;
using System.IO;
using Bloom.web;
using SIL.Code;
using SIL.IO;
using SIL.Reporting;
using YouTrackSharp;
using YouTrackSharp.Issues;

namespace Bloom
{
	/// <summary>
	/// Simplifies the process of submitting issues to our issue tracking system.
	/// Usage: 1) initialize the process via the ctor, 2) add attachments, 3) submit.
	/// Exception: We don't get an issue id until we submit, so we have a way to add an attachment after submitting
	/// so that we can name our book zip file after the issue.
	/// </summary>
	public class YouTrackIssueSubmitter
	{
		// protected in case someone figures out how to test this class (use 'AUT' for tests and 'BL' for production)
		protected string _youTrackProjectKey;

		private readonly BearerTokenConnection _youTrackConnection;
		private readonly List<string> _filesToAttach;
		private readonly IIssuesService _issuesService;
		private const string TokenPiece1 = @"YXV0b19yZXBvcnRfY3JlYXRvcg==.NzQtMA==.V9k0yNUN7Df5eqo4QEk5N4BBKqmEHV";

		/// <summary>
		/// A new instance should be constructed for every issue.
		/// </summary>
		public YouTrackIssueSubmitter(string projectKey)
		{
			_youTrackProjectKey = projectKey;
			_filesToAttach = new List<string>();
			var baseUrl = UrlLookup.LookupUrl(UrlType.IssueTrackingSystemBackend, false, true);
			_youTrackConnection = new BearerTokenConnection($"https://{baseUrl}/youtrack/", $"perm:{TokenPiece1}");
			_issuesService = _youTrackConnection.CreateIssuesService();
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
		/// account we created just for this purpose, "auto_report_creator".
		/// </summary>
		public async System.Threading.Tasks.Task<string> SubmitToYouTrackAsync(string summary, string description)
		{
			string youTrackIssueId = "failed";

			try
			{
				var youTrackIssue = new Issue {Summary = summary, Description = description};
				youTrackIssue.SetField("Type", "Awaiting Classification");
				youTrackIssueId = await _issuesService.CreateIssue(_youTrackProjectKey, youTrackIssue);
				// Now that we have an issue Id, attach any files that were added earlier.
				await AttachFilesAsync(youTrackIssueId);
			}
			catch (Exception e)
			{
				// Probably some sort of internet failure
				Console.WriteLine(e);
			}
			return youTrackIssueId;
		}

		private async System.Threading.Tasks.Task AttachFilesAsync(string youTrackIssueId)
		{
			foreach (var filename in _filesToAttach)
			{
				using (var stream = new FileStream(filename, FileMode.Open))
				{
					await _issuesService.AttachFileToIssue(youTrackIssueId, Path.GetFileName(filename), stream);
				}
			}
		}

		/// <summary>
		/// This is the only way to add a zip file of the book's contents that is named after the issue.
		/// </summary>
		/// <param name="youTrackIssueId"></param>
		/// <param name="filePath"></param>
		public async System.Threading.Tasks.Task<bool> AttachFileToExistingIssueAsync(string youTrackIssueId, string filePath)
		{
			Guard.Against(_issuesService == null, "_issuesService should have been created by YouTrackIssueSubmission()");
			if (!RobustFile.Exists(filePath))
			{
				Logger.WriteEvent("YouTrack issue submitter failed to attach non-existent file: " + filePath);
				return false;
			}
			using (var stream = new FileStream(filePath, FileMode.Open))
			{
				await _issuesService.AttachFileToIssue(youTrackIssueId, Path.GetFileName(filePath), stream);
			}
			return true;
		}

#region Unit test methods

		/// <summary>
		/// Delete an issue.  Only unit test issues can be deleted: other issues are silently left in place.
		/// </summary>
		public async System.Threading.Tasks.Task<bool> DeleteIssueAsync(string youTrackIssueId)
		{
			Guard.Against(_issuesService == null, "_issuesService should have been created by YouTrackIssueSubmission()");
			if (!youTrackIssueId.StartsWith("AUT-"))
			{
				return false;
			}
			await _issuesService.DeleteIssue(youTrackIssueId);
			return true;
		}

		/// <summary>
		/// Get the names of the attachments for the given issue.  Only unit test issues can be queried: other issues return null.
		/// </summary>
		public async System.Threading.Tasks.Task<List<string>> GetAttachmentNamesForIssue(string issueId)
		{
			Guard.Against(_issuesService == null, "_issuesService should have been created by YouTrackIssueSubmission()");
			if (!issueId.StartsWith("AUT-"))
			{
				return null;
			}
			var attachments = await _issuesService.GetAttachmentsForIssue(issueId);
			var names = new List<string>();
			foreach (var attach in attachments)
				names.Add(attach.Name);
			return names;
		}

#endregion
	}
}
