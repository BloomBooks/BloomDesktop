using Bloom;
using NUnit.Framework;

namespace BloomTests.web
{
	[TestFixture]
	public class YouTrackIssueSubmitterTests
	{
		[Test]
		[Category("SkipOnTeamCity")] //I don't know why this is blocked, probably we need a firewall opening
		[Platform(Exclude = "Linux", Reason = "YouTrackSharp is too Windows-centric")]
		public void CanSubmitToYouTrack()
		{
			var submitter = new YouTrackIssueSubmitter("AUT");
			var issueId = submitter.SubmitToYouTrack("Test submission to YouTrack", "Test issue description");
			Assert.That(issueId, Does.StartWith("AUT-"));
		}
	}
}
