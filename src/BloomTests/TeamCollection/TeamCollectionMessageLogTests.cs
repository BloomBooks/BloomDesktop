using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.TeamCollection;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.TeamCollection
{
	public class TeamCollectionMessageLogTests
	{
		private TempFile _logFile;
		private TeamCollectionMessageLog _messageLog;
		[SetUp]
		public void Setup()
		{
			_logFile = new TempFile();
			_messageLog = new TeamCollectionMessageLog(_logFile.Path);
		}

		[TearDown]
		public void TearDown()
		{
			_logFile.Dispose();
		}
		[Test]
		public void WriteMessage_ReadMessages_RecoversMessages()
		{
			MakeHistory1();
			MakeError1();
			var messages = _messageLog.Messages;
			AssertHistory1(messages, 0);
			AssertError1(messages,1);
			Assert.That(messages, Has.Count.EqualTo(2));
		}

		private void MakeHistory1()
		{
			_messageLog.WriteMessage(MessageAndMilestoneType.History,
				"TeamCollection.CheckoutMsg",
				"{0} checked out the book {1}",
				"joe@somewhere.org",
				"Joe hunts pigs");
		}

		private void AssertHistory1(List<TeamCollectionMessage> messages, int index)
		{
			AssertMessage(messages, index, MessageAndMilestoneType.History,
				"TeamCollection.CheckoutMsg",
				"{0} checked out the book {1}",
				"joe@somewhere.org",
				"Joe hunts pigs");
		}

		private void MakeHistory2(string title="Ducks and Geese")
		{
			_messageLog.WriteMessage(MessageAndMilestoneType.History,
				"TeamCollection.BookCheckedIn",
				"You checked in the book {0}",
				title,
				null);
		}

		private void AssertHistory2(List<TeamCollectionMessage> messages, int index, string title = "Ducks and Geese")
		{
			AssertMessage(messages, index, MessageAndMilestoneType.History,
				"TeamCollection.BookCheckedIn",
				"You checked in the book {0}",
				title);
		}

		private void MakeError1(string title = "Joe hunts pigs")
		{
			_messageLog.WriteMessage(MessageAndMilestoneType.Error,
				"TeamCollection.ConflictingCheckout",
				"The book '{0}' is checked out to someone else. Your changes are saved to Lost-and-found.",
				title,
				null);
		}

		private void AssertError1(List<TeamCollectionMessage> messages, int index, string title="Joe hunts pigs")
		{
			AssertMessage(messages, index, MessageAndMilestoneType.Error,
				"TeamCollection.ConflictingCheckout",
				"The book '{0}' is checked out to someone else. Your changes are saved to Lost-and-found.",
				title,
				null);
		}

		private void MakeError2()
		{
			_messageLog.WriteMessage(MessageAndMilestoneType.Error,
				"TeamCollection.CheckedOutOn",
				"{0} checked out this book on {1}.",
				"fred@nowhere.org",
				"Feb 25, 2021");
		}

		private void AssertError2(List<TeamCollectionMessage> messages, int index)
		{
			AssertMessage(messages, index, MessageAndMilestoneType.Error,
				"TeamCollection.CheckedOutOn",
				"{0} checked out this book on {1}.",
				"fred@nowhere.org",
				"Feb 25, 2021");
		}

		private void AssertMessage(List<TeamCollectionMessage> messages, int index,
			MessageAndMilestoneType expectedType, string expectedL10n, string expectedMsg, string expectedParam0 = null,
			string expectedParam1= null)
		{
			Assert.That(messages.Count, Is.GreaterThan(index));
			var msg = messages[index];
			AssertSingleMessage(msg, expectedType, expectedL10n, expectedMsg, expectedParam0, expectedParam1);
		}

		private static void AssertSingleMessage(TeamCollectionMessage msg, MessageAndMilestoneType expectedType,
			string expectedL10n, string expectedMsg, string expectedParam0, string expectedParam1)
		{
			Assert.That(msg.MessageType, Is.EqualTo(expectedType));
			Assert.That(msg.L10NId, Is.EqualTo(expectedL10n));
			Assert.That(msg.Message, Is.EqualTo(expectedMsg));
			Assert.That(msg.Param0 ?? "", Is.EqualTo(expectedParam0 ?? ""));
			Assert.That(msg.Param1 ?? "", Is.EqualTo(expectedParam1 ?? ""));
		}

		[Test]
		public void ReadCurrentErrors_NoMilestones_RecoversErrors()
		{
			MakeHistory1();
			MakeError1();
			MakeError2();
			var messages = _messageLog.CurrentErrors;
			AssertError1(messages, 0);
			AssertError2(messages, 1);
			Assert.That(messages, Has.Count.EqualTo(2));
		}

		[Test]
		public void ReadCurrentErrors_Milestones_RecoversErrorsSinceLogDisplayed()
		{
			MakeHistory1();
			MakeError1();
			_messageLog.WriteMilestone(MessageAndMilestoneType.LogDisplayed);
			MakeError2();
			_messageLog.WriteMilestone(MessageAndMilestoneType.LogDisplayed);
			MakeError1("Fred chases buffalo");
			MakeHistory2();
			MakeError1("Dogs and Cats");
			var messages = _messageLog.CurrentErrors;
			Assert.That(messages, Has.Count.EqualTo(2));
			AssertError1(messages, 0, "Fred chases buffalo");
			AssertError1(messages, 1, "Dogs and Cats");
		}

		[Test]
		public void ReadCurrentErrors_Milestones_RecoversErrorsSinceReloaded()
		{
			MakeHistory1();
			MakeError1();
			_messageLog.WriteMilestone(MessageAndMilestoneType.LogDisplayed);
			MakeError2();
			_messageLog.WriteMilestone(MessageAndMilestoneType.Reloaded);
			MakeError1("Fred chases buffalo");
			MakeHistory2();
			MakeError1("Dogs and Cats");
			var messages = _messageLog.CurrentErrors;
			Assert.That(messages, Has.Count.EqualTo(2));
			AssertError1(messages, 0, "Fred chases buffalo");
			AssertError1(messages, 1, "Dogs and Cats");
		}

		private void MakeNewBookMessage(string title = "I am new")
		{
			_messageLog.WriteMessage(MessageAndMilestoneType.NewStuff,
				"TeamCollection.BookArrived",
				"A new book called {0} was added to the collection",
				title,
				null);
		}

		private void AssertNewBookMessage(List<TeamCollectionMessage> messages, int index, string title = "I am new")
		{
			AssertMessage(messages, index, MessageAndMilestoneType.NewStuff,
				"TeamCollection.BookArrived",
				"A new book called {0} was added to the collection",
				title);
		}

		private void MakeChangedBookMessage(string title = "I am different")
		{
			_messageLog.WriteMessage(MessageAndMilestoneType.NewStuff,
				"TeamCollection.BookChanged",
				"The book called {0} was changed",
				title,
				null);
		}

		private void AssertChangedBookMessage(List<TeamCollectionMessage> messages, int index, string title = "I am different")
		{
			AssertMessage(messages, index, MessageAndMilestoneType.NewStuff,
				"TeamCollection.BookChanged",
				"The book called {0} was changed",
				title);
		}

		private void MakeClobberMessage(string title = "I was clobbered")
		{
			_messageLog.WriteMessage(MessageAndMilestoneType.ClobberPending,
				"TeamCollection.BookClobbered",
				"The book called {0} was changed remotely, and your version has been clobbered",
				title,
				null);
		}

		private void AssertClobberMessage(TeamCollectionMessage message, string title = "I was clobbered")
		{
			Assert.That(message, Is.Not.Null);
			AssertSingleMessage(message, MessageAndMilestoneType.ClobberPending,
				"TeamCollection.BookClobbered",
				"The book called {0} was changed remotely, and your version has been clobbered",
				title,
				null);
		}

		[Test]
		public void ReadCurrentNewStuff_NoMilestones_RecoversNewStuff()
		{
			MakeNewBookMessage();
			MakeHistory1();
			MakeChangedBookMessage();
			var messages = _messageLog.CurrentNewStuff;
			AssertNewBookMessage(messages, 0);
			AssertChangedBookMessage(messages, 1);
			Assert.That(messages, Has.Count.EqualTo(2));
		}

		[Test]
		public void ReadCurrentNewStuff_ReloadMilestones_RecoversNewStuff()
		{
			MakeNewBookMessage();
			MakeHistory1();
			MakeChangedBookMessage();
			_messageLog.WriteMilestone(MessageAndMilestoneType.Reloaded);
			MakeNewBookMessage("Boys and girls");
			MakeHistory1();
			_messageLog.WriteMilestone(MessageAndMilestoneType.Reloaded);
			MakeNewBookMessage("This is new since reload");
			MakeHistory1();
			MakeChangedBookMessage("This is changed since reload");
			var messages = _messageLog.CurrentNewStuff;
			AssertNewBookMessage(messages, 0, "This is new since reload");
			AssertChangedBookMessage(messages, 1, "This is changed since reload");
			Assert.That(messages, Has.Count.EqualTo(2));
		}

		[Test]
		public void WriteMessage_RaisesStatusChanged()
		{
			bool notificationRecieved = false;
			EventHandler handler = (sender, args) => notificationRecieved = true;
			TeamCollectionManager.TeamCollectionStatusChanged += handler;
			MakeNewBookMessage();
			TeamCollectionManager.TeamCollectionStatusChanged -= handler;
			Assert.That(notificationRecieved, Is.True, "TeamCollectionStatusChanged should have been raised");
		}

		[Test]
		public void WriteMilestone_RaisesStatusChanged()
		{
			bool notificationRecieved = false;
			EventHandler handler = (sender, args) => notificationRecieved = true;
			TeamCollectionManager.TeamCollectionStatusChanged += handler;
			_messageLog.WriteMilestone(MessageAndMilestoneType.Reloaded);
			TeamCollectionManager.TeamCollectionStatusChanged -= handler;
			Assert.That(notificationRecieved, Is.True, "TeamCollectionStatusChanged should have been raised");
		}

		[Test]
		public void WriteMessage_GetsCurrentDateTime()
		{
			MakeNewBookMessage();
			var msg = _messageLog.Messages[0];
			Assert.That(msg.When, Is.InRange(DateTime.UtcNow - new TimeSpan(0, 0, 0, 1), DateTime.UtcNow));
		}

		// Reinstate this test if we reintroduce the LoadSavedMessages method.
		//[Test]
		//public void LoadSavedMessages_RecoversEarlierMessages()
		//{
		//	MakeNewBookMessage();
		//	MakeError1();
		//	_messageLog.WriteMilestone(MessageAndMilestoneType.LogDisplayed);
		//	MakeError1("after error milestone");
		//	MakeChangedBookMessage();

		//	_messageLog = new TeamCollectionMessageLog(_logFile.Path);
		//	Assert.That(_messageLog.Messages, Has.Count.EqualTo(0),
		//		"should not automatically load from file");
		//	MakeNewBookMessage("another new book");
		//	MakeError1("Problem after save");
		//	Assert.That(_messageLog.Messages, Has.Count.EqualTo(2));
		//	var timeAfterCreation = DateTime.Now; // reloaded messages should show as created before this
		//	_messageLog.LoadSavedMessages();
		//	var allMessages = _messageLog.Messages;
		//	Assert.That(allMessages, Has.Count.EqualTo(7));
		//	AssertNewBookMessage(allMessages, 0);
		//	AssertError1(allMessages, 1);
		//	AssertNewBookMessage(allMessages, 5, "another new book");

		//	Assert.That(allMessages[0].When, Is.LessThan(timeAfterCreation));

		//	var errors = _messageLog.CurrentErrors;
		//	Assert.That(errors, Has.Count.EqualTo(2));
		//	AssertError1(errors, 0, "after error milestone");
		//	AssertError1(errors, 1, "Problem after save");

		//	_messageLog.LoadSavedMessages(); // should not insert them again
		//	Assert.That(_messageLog.Messages, Has.Count.EqualTo(7));
		//}

		[Test]
		public void ClobberMessage_NoneInList_ReturnsNull()
		{
			MakeNewBookMessage();
			MakeError1();
			Assert.That(_messageLog.CurrentClobberMessage, Is.Null);
		}

		[Test]
		public void ClobberMessage_TwoInList_ReturnsLast()
		{
			MakeNewBookMessage();
			MakeError1();
			MakeClobberMessage();
			MakeError2();
			MakeClobberMessage("last clobber message");
			MakeHistory1();
			AssertClobberMessage(_messageLog.CurrentClobberMessage, "last clobber message");
		}

		[Test]
		public void ClobberMessage_OnlyBeforeShowedClobbered_ReturnsNull()
		{
			MakeNewBookMessage();
			MakeError1();
			MakeClobberMessage();
			MakeError2();
			_messageLog.WriteMilestone(MessageAndMilestoneType.ShowedClobbered);
			MakeHistory1();
			Assert.That(_messageLog.CurrentClobberMessage, Is.Null);
		}

		[Test]
		public void TeamCollectionStatus_HistoryOnly_Nominal()
		{
			MakeHistory1();
			Assert.That(_messageLog.TeamCollectionStatus, Is.EqualTo(TeamCollectionStatus.Nominal));
		}

		[Test]
		public void TeamCollectionStatus_NewStuffNoErrors_NewStuff()
		{
			MakeHistory1();
			MakeNewBookMessage();
			MakeChangedBookMessage();
			MakeHistory2();
			Assert.That(_messageLog.TeamCollectionStatus, Is.EqualTo(TeamCollectionStatus.NewStuff));
		}

		[Test]
		public void TeamCollectionStatus_OtherMessagesOnlyBeforeReload_Nominal()
		{
			MakeHistory1();
			MakeNewBookMessage();
			MakeError1();
			MakeClobberMessage();
			_messageLog.WriteMilestone(MessageAndMilestoneType.Reloaded);
			Assert.That(_messageLog.TeamCollectionStatus, Is.EqualTo(TeamCollectionStatus.Nominal));
		}

		[Test]
		public void TeamCollectionStatus_NewStuffAndErrorsSinceLogDisplayed_Error()
		{
			MakeHistory1();
			MakeNewBookMessage();
			_messageLog.WriteMilestone(MessageAndMilestoneType.LogDisplayed);
			MakeError1();
			MakeChangedBookMessage();
			MakeHistory2();
			Assert.That(_messageLog.TeamCollectionStatus, Is.EqualTo(TeamCollectionStatus.Error));
		}

		[Test]
		public void TeamCollectionStatus_ClobberPending_ShowsThat()
		{
			MakeHistory1();
			MakeNewBookMessage();
			MakeClobberMessage();
			MakeError1();
			MakeChangedBookMessage();
			MakeHistory2();
			Assert.That(_messageLog.TeamCollectionStatus, Is.EqualTo(TeamCollectionStatus.ClobberPending));
		}

		[Test]
		public void PrettyMessages_ShowsExpectedResults()
		{
			MakeHistory1();
			MakeNewBookMessage();
			_messageLog.WriteMilestone(MessageAndMilestoneType.Reloaded);
			MakeError1();
			_messageLog.WriteMilestone(MessageAndMilestoneType.LogDisplayed);
			MakeChangedBookMessage();
			_messageLog.WriteMilestone(MessageAndMilestoneType.ShowedClobbered);
			var prettyMessages = _messageLog.PrettyPrintMessages;
			var today = DateTime.Now.ToShortDateString();
			VerifyPrettyMessage(prettyMessages[0], MessageAndMilestoneType.History,today + ": joe@somewhere.org checked out the book Joe hunts pigs");
			VerifyPrettyMessage(prettyMessages[1], MessageAndMilestoneType.NewStuff, today + ": A new book called I am new was added to the collection");
			VerifyPrettyMessage(prettyMessages[2], MessageAndMilestoneType.Error, today + ": The book 'Joe hunts pigs' is checked out to someone else. Your changes are saved to Lost-and-found.");
			VerifyPrettyMessage(prettyMessages[3], MessageAndMilestoneType.NewStuff, today + ": The book called I am different was changed");
			VerifyPrettyMessage(prettyMessages[4], MessageAndMilestoneType.ShowedClobbered,today + ": Repaired conflict");
			// Enhance: Do we want colors? If so, should the output of this be HTML,
			// or something else that supports that? Or should some other component handle formatting?
		}

		void VerifyPrettyMessage(Tuple<MessageAndMilestoneType, String> pretty, MessageAndMilestoneType type, string message)
		{
			Assert.That(pretty.Item1, Is.EqualTo(type));
			Assert.That(pretty.Item2, Is.EqualTo(message));
		}
	}
}
