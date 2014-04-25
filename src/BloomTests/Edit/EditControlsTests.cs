using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloom.Collection;
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

		public ModelStub(CollectionSettings settings) : base(settings)
		{
		}

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
