
using System;
using System.Collections.Generic;
using System.Net;
using Bloom.web;
using SIL.Code;
using SIL.IO;
using SIL.Reporting;
using YouTrackSharp.Infrastructure;
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

		private readonly Connection _youTrackConnection = new Connection(UrlLookup.LookupUrl(UrlType.IssueTrackingSystemBackend, false, true), 0 /* BL-5500 don't specify port */, true, "youtrack");
		private readonly List<string> _filesToAttach;
		private IssueManagement _issueManagement;

		/// <summary>
		/// A new instance should be constructed for every issue.
		/// </summary>
		public YouTrackIssueSubmitter(string projectKey)
		{
			_youTrackProjectKey = projectKey;
			_filesToAttach = new List<string>();
			_issueManagement = null;
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
		public string SubmitToYouTrack(string summary, string description)
		{
			string youTrackIssueId = "failed";
			try
			{
				_youTrackConnection.Authenticate("auto_report_creator", "thisIsInOpenSourceCode");
				_issueManagement = new IssueManagement(_youTrackConnection);
				dynamic youTrackIssue = new Issue();
				youTrackIssue.ProjectShortName = _youTrackProjectKey;
				youTrackIssue.Type = "Awaiting Classification";
				youTrackIssue.Summary = summary;
				youTrackIssue.Description = description;
				youTrackIssueId = _issueManagement.CreateIssue(youTrackIssue);

				// Now that we have an issue Id, attach any files.
				AttachFiles(youTrackIssueId);
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
				_issueManagement.AttachFileToIssue(youTrackIssueId, filename);
			}
		}

		/// <summary>
		/// This is the only way to add a zip file of the book's contents that is named after the issue.
		/// </summary>
		/// <param name="youTrackIssueId"></param>
		/// <param name="filePath"></param>
		public void AttachFileToExistingIssue(string youTrackIssueId, string filePath)
		{
			Guard.Against(_issueManagement == null, "_issueManagement should have been created by SubmitToYouTrack()");
			if (!RobustFile.Exists(filePath))
			{
				Logger.WriteEvent("YouTrack issue submitter failed to attach non-existent file: " + filePath);
				return;
			}
			_issueManagement.AttachFileToIssue(youTrackIssueId, filePath);
		}
	}
}
