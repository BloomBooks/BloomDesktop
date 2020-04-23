using System;
using System.IO;
using Bloom;
using NUnit.Framework;
using System.Threading.Tasks;

namespace BloomTests.web
{
	[TestFixture]
	public class YouTrackIssueSubmitterTests
	{
		[Test]
		[Category("SkipOnTeamCity")] //I don't know why this is blocked, probably we need a firewall opening
		public void CanSubmitToYouTrack()
		{
			var tempfile1 = Path.GetTempFileName();
			string tempfile2 = null;
			try
			{
				var submitter = new YouTrackIssueSubmitter("AUT");

				File.WriteAllLines(tempfile1, new[] { @"This is a test.  This is only a test." });
				submitter.AddAttachmentWhenWeHaveAnIssue(tempfile1);
				var taskCreate = Task.Run(async () =>
					await submitter.SubmitToYouTrackAsync("Test submission to YouTrack", "Test issue description"));
				var issueId = taskCreate.Result;
				Assert.That(issueId, Does.StartWith("AUT-"));

				var taskAttach1 = Task.Run(async () => await submitter.GetAttachmentNamesForIssue(issueId));
				var names1 = taskAttach1.Result;
				Assert.That(names1.Count, Is.EqualTo(1), "unit test issue starts with one file attached");
				Assert.That(names1.Contains(Path.GetFileName(tempfile1)), Is.True, "Initial file was attached initially");

				tempfile2 = Path.Combine(Path.GetTempPath(), issueId + ".tmp");
				File.WriteAllLines(tempfile2, new[] { @"This is the second test file, named after the issue.", issueId });
				var taskAddFile = Task.Run(async () =>
					await submitter.AttachFileToExistingIssueAsync(issueId, tempfile2));
				var added = taskAddFile.Result;
				Assert.That(added, Is.True, "file added to issue");

				var taskAttach2 = Task.Run(async () => await submitter.GetAttachmentNamesForIssue(issueId));
				var names2 = taskAttach2.Result;
				Assert.That(names2.Count, Is.EqualTo(2), "unit test issue ends up with two files attached");
				Assert.That(names2.Contains(Path.GetFileName(tempfile1)), Is.True, "Initial file is still attached");
				Assert.That(names2.Contains(Path.GetFileName(tempfile2)), Is.True, "Second file was attached");

				// no need to keep adding test issues indefinitely: clean up after ourselves.
				var taskDelete = Task.Run(async () => await submitter.DeleteIssueAsync(issueId));
				var deleted = taskDelete.Result;
				Assert.That(deleted, Is.True, "unit test issue deleted");
			}
			finally
			{
				// clean up the disk after ourselves.
				File.Delete(tempfile1);
				if (tempfile2 != null)
					File.Delete(tempfile2);
			}
		}
	}
}
