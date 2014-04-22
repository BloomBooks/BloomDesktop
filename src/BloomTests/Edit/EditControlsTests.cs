using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloom.Edit;
using Gecko;
using NUnit.Framework;

namespace BloomTests.Edit
{
	/// <summary>
	/// Tests functions of the EditControlsModel
	/// </summary>
	[TestFixture]
	public class EditControlsTests
	{
		private ModelStub _model;

		[Test]
		public void ClickRightStageButton_IncrementsStage_NotAboveStages()
		{
			_model = new ModelStub();
			_model.Stages.Clear();
			_model.AddStage("cat sat rat");
			_model.AddStage("bob fob");
			_model.AddStage("big wig fig rig");

			_model.ControlClicked("incStage");
			VerifyOneElementUpdated("stageNumber", "2");

			ClearElementsUpdated();
			_model.ControlClicked("incStage");
			VerifyOneElementUpdated("stageNumber", "3");

			ClearElementsUpdated();
			_model.ControlClicked("incStage");

			Assert.That(_model.ElementsUpdated, Is.Empty);
		}

		[Test]
		public void PostNavigationInitialize_UpdatesWordList()
		{
			_model = new ModelStub();
			_model.Stages.Clear();
			_model.AddStage("bob fob");

			_model.PostNavigationInitialize();
			VerifyOneElementUpdated("wordList", "<tr><td>bob</td><td>fob</td></tr>");
		}

		[Test]
		public void PostNavigationInitialize_UpdatesStageCountAndButtons()
		{
			_model = new ModelStub();
			_model.Stages.Clear();
			_model.AddStage("cat sat rat");
			_model.AddStage("bob fob");
			_model.AddStage("big wig fig rig");
			_model.AddStage("the fourth stage is massive");
			_model.UpdateElementAttribute("decStage", "class", "something");

			_model.PostNavigationInitialize();
			VerifyOneElementUpdated("numberOfStages", "4");
			Assert.That(_model.GetElementAttribute("decStage", "class"), Is.EqualTo("something disabledIcon"));
		}

		[Test]
		public void PostNavigationInitialize_UpdatesLevelButtons()
		{
			_model = new ModelStub();
			_model.UpdateElementAttribute("decLevel", "class", "something");

			_model.PostNavigationInitialize();
			Assert.That(_model.GetElementAttribute("decLevel", "class"), Is.EqualTo("something disabledIcon"));
		}

		[Test]
		public void ChangingStage_UpdatesWordList()
		{
			_model = new ModelStub();
			_model.Stages.Clear();
			_model.AddStage("cat sat rat"); // exactly fills row
			_model.AddStage("bob fob"); // less than one row
			_model.AddStage("big wig fig rig"); // second partial row

			_model.StageNumber = 2;
			VerifyOneElementUpdated("wordList", "<tr><td>bob</td><td>fob</td></tr>");

			ClearElementsUpdated();
			_model.StageNumber = 1;
			VerifyOneElementUpdated("wordList", "<tr><td>cat</td><td>rat</td><td>sat</td></tr>");

			ClearElementsUpdated();
			_model.StageNumber = 3;
			VerifyOneElementUpdated("wordList", "<tr><td>big</td><td>fig</td><td>rig</td></tr><tr><td>wig</td></tr>");
		}

		[Test]
		public void ClickingSortButtons_SetsSelectedClass()
		{
			_model = new ModelStub();
			_model.Stages.Clear();
			_model.AddStage("catty sat rate");

			_model.UpdateElementAttribute("sortAlphabetic", "class", "sortItem sortIconSelected");
			_model.UpdateElementAttribute("sortLength", "class", "sortItem");
			_model.UpdateElementAttribute("sortFrequency", "class", "sortItem");

			_model.ControlClicked("sortLength");
			Assert.That(_model.GetElementAttribute("sortAlphabetic", "class"), Is.EqualTo("sortItem"));
			Assert.That(_model.GetElementAttribute("sortLength", "class"), Is.EqualTo("sortItem sortIconSelected"));

			_model.ControlClicked("sortFrequency");
			Assert.That(_model.GetElementAttribute("sortFrequency", "class"), Is.EqualTo("sortItem sortIconSelected"));
			Assert.That(_model.GetElementAttribute("sortLength", "class"), Is.EqualTo("sortItem"));

			_model.ControlClicked("sortAlphabetic");
			Assert.That(_model.GetElementAttribute("sortAlphabetic", "class"), Is.EqualTo("sortItem sortIconSelected"));
			Assert.That(_model.GetElementAttribute("sortFrequency", "class"), Is.EqualTo("sortItem"));

			_model.UpdateElementAttribute("sortLength", "class", "sortItem sortIconSelected"); // anomolous...length is also selected, though not properly current.
			_model.UpdateElementAttribute("sortAlphabetic", "class", "sortItem"); // anomolous...doesn't have property, though it is current.
			// Should still get the correct final state.
			_model.ControlClicked("sortLength");
			Assert.That(_model.GetElementAttribute("sortAlphabetic", "class"), Is.EqualTo("sortItem"));
			Assert.That(_model.GetElementAttribute("sortLength", "class"), Is.EqualTo("sortItem sortIconSelected"));
		}

		[Test]
		public void ClickingSortButtons_SortsWordListCorrectly()
		{
			_model = new ModelStub();
			_model.Stages.Clear();
			_model.AddStage("catty sat rate");
			_model.AddStage("bob fob cob job hope");
			_model.Stages[0].SetFrequency("rate", 5);
			_model.Stages[0].SetFrequency("catty", 3);
			_model.Stages[1].SetFrequency("hope", 3);

			_model.StageNumber = 2;

			// Default is currently alphabetic
			ClearElementsUpdated();
			_model.StageNumber = 1;
			VerifyOneElementUpdated("wordList", "<tr><td>catty</td><td>rate</td><td>sat</td></tr>");

			ClearElementsUpdated();
			_model.ControlClicked("sortLength");
			VerifyOneElementUpdated("wordList", "<tr><td>sat</td><td>rate</td><td>catty</td></tr>");

			ClearElementsUpdated();
			_model.ControlClicked("sortFrequency");
			VerifyOneElementUpdated("wordList", "<tr><td>rate</td><td>catty</td><td>sat</td></tr>");

			ClearElementsUpdated();
			_model.ControlClicked("sortAlphabetic");
			VerifyOneElementUpdated("wordList", "<tr><td>catty</td><td>rate</td><td>sat</td></tr>");

			ClearElementsUpdated();
			_model.StageNumber = 2;
			VerifyOneElementUpdated("wordList", "<tr><td>bob</td><td>cob</td><td>fob</td></tr><tr><td>hope</td><td>job</td></tr>");

			ClearElementsUpdated();
			_model.ControlClicked("sortLength"); // We want the same-length ones to be alphabetic
			VerifyOneElementUpdated("wordList", "<tr><td>bob</td><td>cob</td><td>fob</td></tr><tr><td>job</td><td>hope</td></tr>");

			ClearElementsUpdated();
			_model.ControlClicked("sortFrequency"); // We want the same-frequency ones to be alphabetic
			VerifyOneElementUpdated("wordList", "<tr><td>hope</td><td>bob</td><td>cob</td></tr><tr><td>fob</td><td>job</td></tr>");
		}

		[Test]
		public void ClickLeftStageButton_DecrementsStage_NotBelow1()
		{
			_model = new ModelStub();
			_model.StageNumber = 3;
			ClearElementsUpdated();

			_model.ControlClicked("decStage");
			VerifyOneElementUpdated("stageNumber", "2");

			ClearElementsUpdated();
			_model.ControlClicked("decStage");
			VerifyOneElementUpdated("stageNumber", "1");

			ClearElementsUpdated();
			_model.ControlClicked("decStage");
			Assert.That(_model.ElementsUpdated, Is.Empty);
		}

		[Test]
		public void ClickStageButtons_HidesInvalidButtons()
		{
			_model = new ModelStub();
			_model.Stages.Clear();
			_model.AddStage("cat sat rat");
			_model.AddStage("bob fob");
			_model.AddStage("big wig fig rig");
			_model.UpdateElementAttribute("decStage", "class", "something");
			_model.UpdateElementAttribute("incStage", "class", "something");
			_model.StageNumber = 3;
			Assert.That(_model.GetElementAttribute("decStage", "class"), Is.EqualTo("something"));
			Assert.That(_model.GetElementAttribute("incStage", "class"), Is.EqualTo("something disabledIcon"));

			_model.ControlClicked("decStage");
			Assert.That(_model.GetElementAttribute("decStage", "class"), Is.EqualTo("something"));
			Assert.That(_model.GetElementAttribute("incStage", "class"), Is.EqualTo("something"));

			_model.ControlClicked("decStage");
			Assert.That(_model.GetElementAttribute("decStage", "class"), Is.EqualTo("something disabledIcon"));
			Assert.That(_model.GetElementAttribute("incStage", "class"), Is.EqualTo("something"));

			_model.ControlClicked("incStage");
			Assert.That(_model.GetElementAttribute("decStage", "class"), Is.EqualTo("something"));
			Assert.That(_model.GetElementAttribute("incStage", "class"), Is.EqualTo("something"));

			_model.ControlClicked("incStage");
			Assert.That(_model.GetElementAttribute("decStage", "class"), Is.EqualTo("something"));
			Assert.That(_model.GetElementAttribute("incStage", "class"), Is.EqualTo("something disabledIcon"));
		}

		[Test]
		public void ClickLevelButtons_HidesInvalidButtons()
		{
			_model = new ModelStub();
			_model.NumberOfLevels = 3; // Eventually we may have to do something trickier to simulate 3 levels.

			_model.UpdateElementAttribute("decLevel", "class", "something");
			_model.UpdateElementAttribute("incLevel", "class", "something");
			_model.LevelNumber = 3;
			Assert.That(_model.GetElementAttribute("decLevel", "class"), Is.EqualTo("something"));
			Assert.That(_model.GetElementAttribute("incLevel", "class"), Is.EqualTo("something disabledIcon"));

			_model.ControlClicked("decLevel");
			Assert.That(_model.GetElementAttribute("decLevel", "class"), Is.EqualTo("something"));
			Assert.That(_model.GetElementAttribute("incLevel", "class"), Is.EqualTo("something"));

			_model.ControlClicked("decLevel");
			Assert.That(_model.GetElementAttribute("decLevel", "class"), Is.EqualTo("something disabledIcon"));
			Assert.That(_model.GetElementAttribute("incLevel", "class"), Is.EqualTo("something"));

			_model.ControlClicked("incLevel");
			Assert.That(_model.GetElementAttribute("decLevel", "class"), Is.EqualTo("something"));
			Assert.That(_model.GetElementAttribute("incLevel", "class"), Is.EqualTo("something"));

			_model.ControlClicked("incLevel");
			Assert.That(_model.GetElementAttribute("decLevel", "class"), Is.EqualTo("something"));
			Assert.That(_model.GetElementAttribute("incLevel", "class"), Is.EqualTo("something disabledIcon"));
		}

		[Test]
		public void ClickRightLevelButton_IncrementsLevel()
		{
			_model = new ModelStub();
			_model.ControlClicked("incLevel");
			VerifyOneElementUpdated("levelNumber", "2");

			ClearElementsUpdated();
			_model.ControlClicked("incLevel");
			VerifyOneElementUpdated("levelNumber", "3");
		}

		[Test]
		public void ClickLeftLevelButton_DecrementsLevel_NotBelow1()
		{
			_model = new ModelStub();
			_model.LevelNumber = 3;
			ClearElementsUpdated();

			_model.ControlClicked("decLevel");
			VerifyOneElementUpdated("levelNumber", "2");

			ClearElementsUpdated();
			_model.ControlClicked("decLevel");
			VerifyOneElementUpdated("levelNumber", "1");

			ClearElementsUpdated();
			_model.ControlClicked("decLevel");
			Assert.That(_model.ElementsUpdated, Is.Empty);
		}

		void VerifyOneElementUpdated(string id, string val)
		{
			int hits = 0;
			foreach (var row in _model.ElementsUpdated)
			{
				if (row.Item1 == id)
				{
					Assert.That(row.Item2, Is.EqualTo(val));
					hits++;
				}
			}
			Assert.That(hits, Is.EqualTo(1));
		}

		void ClearElementsUpdated()
		{
			_model.ElementsUpdated.Clear();
		}
	}

	public class MockView : IEditControlsView
	{
		public GeckoWebBrowser Browser { get; private set; }
	}

	public class ModelStub : EditControlsModel
	{
		public List<Tuple<string, string>> ElementsUpdated = new List<Tuple<string, string>>();
		internal override void UpdateElementContent(string id, string val)
		{
			ElementsUpdated.Add(Tuple.Create(id, val));
		}

		public Dictionary<string, string> ElementContent = new Dictionary<string, string>();

		internal override string GetElementContent(string id)
		{
			string result;
			ElementContent.TryGetValue(id, out result);
			return result;
		}

		public Dictionary<Tuple<string, string>, string> ElementAttributes = new Dictionary<Tuple<string, string>, string>();

		internal override string GetElementAttribute(string elementId, string attrName)
		{
			string result;
			ElementAttributes.TryGetValue(Tuple.Create(elementId, attrName), out result);
			return result ?? "";
		}

		internal override void UpdateElementAttribute(string elementId, string attrName, string val)
		{
			ElementAttributes[Tuple.Create(elementId, attrName)] = val;
		}
	}
}
