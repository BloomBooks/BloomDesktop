
using System;
using System.Collections.Generic;
using System.Net;
using Bloom.web;
using SIL.IO;
using SIL.Reporting;
using YouTrackSharp.Infrastructure;
using YouTrackSharp.Issues;


namespace Bloom
{
	// TODO: Are all these member variables needed? How about making a static method instead?
	// Well, I could see YouTrackConnection and the list of files to attach as being pretty legit non-method variables
	public class YouTrackIssueSubmitter
	{
		protected string _youTrackProjectKey;

		private readonly Connection _youTrackConnection = new Connection(UrlLookup.LookupUrl(UrlType.IssueTrackingSystemBackend, false, true), 0 /* BL-5500 don't specify port */, true, "youtrack");
		private readonly List<string> _filesToAttach;

		/// <summary>
		/// A new instance should be constructed for every issue.
		/// </summary>
		public YouTrackIssueSubmitter(string projectKey)
		{
			_youTrackProjectKey = projectKey;
			_filesToAttach = new List<string>();
		}

		/// <summary>
		/// Verify a file's existence and store the filename to attach later.
		/// We may not be able to attach now, because we don't have an actual issue Id until we submit the issue
		/// and we can't guarantee that the caller will not try to AddAttachment() before calling SubmitToYouTrack().
		/// </summary>
		/// <param name="file"></param>
		public void AddAttachment(string file)
		{
			if (!RobustFile.Exists(file))
			{
				Logger.WriteEvent("YouTrack issue submitter failed to attach non-existent file: " + file);
				return;
			}
			_filesToAttach.Add(file);
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
				var issueManagement = new IssueManagement(_youTrackConnection);
				dynamic youTrackIssue = new Issue();
				youTrackIssue.ProjectShortName = _youTrackProjectKey;
				youTrackIssue.Type = "Awaiting Classification";
				youTrackIssue.Summary = summary;
				youTrackIssue.Description = description;
				youTrackIssueId = issueManagement.CreateIssue(youTrackIssue);

				// Now that we have an issue Id, attach any files.
				AttachFiles(issueManagement, youTrackIssueId);
			}
			catch (WebException e)
			{
				// Some sort of internet failure
				Console.WriteLine(e);
			}
			return youTrackIssueId;
		}

		private void AttachFiles(IssueManagement management, string youTrackIssueId)
		{
			foreach (var filename in _filesToAttach)
			{
				management.AttachFileToIssue(youTrackIssueId, filename);
			}
		}
	}
}
