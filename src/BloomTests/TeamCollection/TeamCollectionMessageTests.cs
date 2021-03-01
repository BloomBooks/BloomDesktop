using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.TeamCollection;
using NUnit.Framework;

namespace BloomTests.TeamCollection
{
	public class TeamCollectionMessageTests
	{
		private static string goodParts = "\tTeamCollection.NewBook\tA new book {0} arrived from {1}\tMy book\tFred";
		[Test]
		public void FromPersistedForm_ValidForm_GetsData()
		{
			var msg = TeamCollectionMessage.FromPersistedForm("2009-06-15T13:45:30.0000000-07:00\tError" + goodParts);
			Assert.That(msg.When.Year, Is.EqualTo(2009));
			Assert.That(msg.MessageType, Is.EqualTo(MessageAndMilestoneType.Error));
			Assert.That(msg.Param1, Is.EqualTo("Fred"));
			Assert.That(msg.Param0, Is.EqualTo("My book"));
			Assert.That(msg.Message, Is.EqualTo("A new book {0} arrived from {1}"));
			Assert.That(msg.L10NId, Is.EqualTo("TeamCollection.NewBook"));
		}

		[Test]
		public void FromPersistedForm_ValidMilestone_GetsData()
		{
			var msg = TeamCollectionMessage.FromPersistedForm("2010-06-15T13:45:30.0000000-07:00\tReloaded");
			Assert.That(msg.When.Month, Is.EqualTo(6));
			Assert.That(msg.MessageType, Is.EqualTo(MessageAndMilestoneType.Reloaded));
			Assert.That(msg.Param1, Is.Null);
		}

		[TestCase("rubbish\tHistory", true)] // bad date
		[TestCase("", false)] // nothing at all
		[TestCase("2009-06-15T13:45:30.0000000-07:00", false)] // date with no type
		[TestCase("2009-06-15T13:45:30.0000000-07:00\tnonsence", true)] // invalid type
		public void FromPersistedForm_InvalidForm_ReturnsNull(string badForm, bool appendGoodParts)
		{
			var msg = TeamCollectionMessage.FromPersistedForm(badForm + (appendGoodParts ? goodParts : ""));
			Assert.That(msg, Is.Null);
		}
	}
}
