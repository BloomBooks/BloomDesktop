using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloom;
using Bloom.Book;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace BloomTests.Book
{
	[TestFixture()]
	public class DataSetTests
	{
		[Test]
		public void SameAs_EmptyDataSets_True()
		{
			var first = new DataSet();
			var second = new DataSet();
			Assert.That(first.SameAs(second));
		}

		[Test]
		public void SameAs_OneHasExtraTextValue_False()
		{
			var first = new DataSet();
			var second = new DataSet();
			first.AddLanguageString("one", XmlString.FromXml("a value"), "en", false);
			Assert.That(first.SameAs(second), Is.False);
		}

		[Test]
		public void SameAs_DifferentKeys_False()
		{
			var first = new DataSet();
			var second = new DataSet();
			first.AddLanguageString("one", XmlString.FromXml("a value"), "en", false);
			second.AddLanguageString("two", XmlString.FromXml("a value"), "en", false);
			Assert.That(first.SameAs(second), Is.False);
		}

		[Test]
		public void SameAs_OneHasDifferentTextValue_False()
		{
			var first = new DataSet();
			var second = new DataSet();
			first.AddLanguageString("one", XmlString.FromXml("a value"), "en", false);
			second.AddLanguageString("one", XmlString.FromXml("another value"), "en", false);
			Assert.That(first.SameAs(second), Is.False);
		}

		[Test]
		public void SameAs_OneHasMoreTextValues_False()
		{
			var first = new DataSet();
			var second = new DataSet();
			first.AddLanguageString("one", XmlString.FromXml("a value"), "en", false);
			first.AddLanguageString("one", XmlString.FromXml("another value"), "fr", false);
			second.AddLanguageString("one", XmlString.FromXml("a value"), "en", false);
			Assert.That(first.SameAs(second), Is.False);
		}

		[Test]
		public void SameAs_OneHasDifferentLanguage_False()
		{
			var first = new DataSet();
			var second = new DataSet();
			first.AddLanguageString("one", XmlString.FromXml("a value"), "en", false);
			second.AddLanguageString("one", XmlString.FromXml("a value"), "de", false);
			Assert.That(first.SameAs(second), Is.False);
		}

		[Test]
		public void SameAs_FirstHasExtraXMatterValue_False()
		{
			var first = new DataSet();
			var second = new DataSet();
			var values = new HashSet<KeyValuePair<string, string>>();
			first.UpdateXmatterPageDataAttributeSet("one", values);
			Assert.That(first.SameAs(second), Is.False);
		}

		[Test]
		public void SameAs_SecondHasExtraXMatterValue_False()
		{
			var first = new DataSet();
			var second = new DataSet();
			var values = new HashSet<KeyValuePair<string, string>>();
			first.UpdateXmatterPageDataAttributeSet("one", values);
			var values2 = new HashSet<KeyValuePair<string, string>>();
			second.UpdateXmatterPageDataAttributeSet("one", values2);
			var values3 = new HashSet<KeyValuePair<string, string>>();
			second.UpdateXmatterPageDataAttributeSet("two", values3);
			Assert.That(first.SameAs(second), Is.False);
		}

		[Test]
		public void SameAs_DifferentXMatterKeys_False()
		{
			var first = new DataSet();
			var second = new DataSet();
			var values = new HashSet<KeyValuePair<string, string>>();
			first.UpdateXmatterPageDataAttributeSet("one", values);
			var secondValues = new HashSet<KeyValuePair<string, string>>();
			second.UpdateXmatterPageDataAttributeSet("two", secondValues);
			Assert.That(first.SameAs(second), Is.False);
		}

		[Test]
		public void SameAs_ExtraXMatterValue_False()
		{
			var first = new DataSet();
			var second = new DataSet();
			var values = new HashSet<KeyValuePair<string, string>>();
			values.Add(new KeyValuePair<string, string>("key", "value"));
			first.UpdateXmatterPageDataAttributeSet("one", values);
			var secondValues = new HashSet<KeyValuePair<string, string>>();
			second.UpdateXmatterPageDataAttributeSet("one", secondValues);
			Assert.That(first.SameAs(second), Is.False);
		}

		[Test]
		public void SameAs_DifferentXmatterKey_False()
		{
			var first = new DataSet();
			var second = new DataSet();
			var values = new HashSet<KeyValuePair<string, string>>();
			values.Add(new KeyValuePair<string, string>("key", "value"));
			first.UpdateXmatterPageDataAttributeSet("one", values);
			var secondValues = new HashSet<KeyValuePair<string, string>>();
			secondValues.Add(new KeyValuePair<string, string>("otherkey", "value"));
			second.UpdateXmatterPageDataAttributeSet("one", secondValues);
			Assert.That(first.SameAs(second), Is.False);
		}

		[Test]
		public void SameAs_DifferentXmatterValue_False()
		{
			var first = new DataSet();
			var second = new DataSet();
			var values = new HashSet<KeyValuePair<string, string>>();
			values.Add(new KeyValuePair<string, string>("key", "value"));
			first.UpdateXmatterPageDataAttributeSet("one", values);
			var secondValues = new HashSet<KeyValuePair<string, string>>();
			secondValues.Add(new KeyValuePair<string, string>("key", "value2"));
			second.UpdateXmatterPageDataAttributeSet("one", secondValues);
			Assert.That(first.SameAs(second), Is.False);
		}

		[Test]
		public void SameAs_ComplexSameValues_True()
		{
			var first = MakeComplexDataSet();
			var second = MakeComplexDataSet();
			Assert.That(first.SameAs(second), Is.True);
		}

		[Test]
		public void SameAs_OneHasAttrVals_False()
		{
			var first = MakeComplexDataSet();
			var second = MakeComplexDataSet();
			DataSetElementValue dsv2 = second.TextVariables["one"];
			dsv2.SetAttributeList("fr", null);
			Assert.That(first.SameAs(second), Is.False);
			Assert.That(second.SameAs(first), Is.False);
		}

		[Test]
		public void SameAs_OneHasMoreAttrVals_False()
		{
			var first = MakeComplexDataSet();
			var second = MakeComplexDataSet();
			DataSetElementValue dsv2 = second.TextVariables["one"];
			dsv2.SetAttributeList("fr", MakeList("attr1", "val1fr"));
			Assert.That(first.SameAs(second), Is.False);
			Assert.That(second.SameAs(first), Is.False);
		}

		[Test]
		public void SameAs_OneHasDifferentAttrVals_False()
		{
			var first = MakeComplexDataSet();
			var second = MakeComplexDataSet();
			DataSetElementValue dsv2 = second.TextVariables["one"];
			dsv2.SetAttributeList("fr", MakeList("attr1", "val1fr", "attr4", "val4modifed"));
			Assert.That(first.SameAs(second), Is.False);
			Assert.That(second.SameAs(first), Is.False);
		}

		[Test]
		public void SameAs_OneHasDifferentAttrOrder_True()
		{
			var first = MakeComplexDataSet();
			var second = MakeComplexDataSet();
			DataSetElementValue dsv2 = second.TextVariables["one"];
			dsv2.SetAttributeList("fr", MakeList("attr4", "val4", "attr1", "val1fr"));
			Assert.That(first.SameAs(second), Is.True);
			Assert.That(second.SameAs(first), Is.True);
		}

		[Test]
		public void SameAs_OneHasDifferentAttrKeys_False()
		{
			var first = MakeComplexDataSet();
			var second = MakeComplexDataSet();
			DataSetElementValue dsv2 = second.TextVariables["one"];
			dsv2.SetAttributeList("fr", MakeList("attr1", "val1fr", "attrModified", "val4"));
			Assert.That(first.SameAs(second), Is.False);
			Assert.That(second.SameAs(first), Is.False);
		}

		private static DataSet MakeComplexDataSet()
		{
			var ds = new DataSet();
			ds.AddLanguageString("one", XmlString.FromXml("a value"), "en", false);
			ds.AddLanguageString("one", XmlString.FromXml("another value"), "de", false);
			ds.AddLanguageString("two", XmlString.FromXml("another value"), "fr", false);
			var values = new HashSet<KeyValuePair<string, string>>();
			values.Add(new KeyValuePair<string, string>("key", "value"));
			ds.UpdateXmatterPageDataAttributeSet("one", values);
			DataSetElementValue dsv = ds.TextVariables["one"];
			dsv.SetAttributeList("en", MakeList("attr1", "val1", "attr2", "val2"));
			dsv.SetAttributeList("de", MakeList("attr1", "val1de", "attr3", "val3de"));
			DataSetElementValue dsv2 = ds.TextVariables["one"];
			dsv2.SetAttributeList("fr", MakeList("attr1", "val1fr", "attr4", "val4"));
			return ds;
		}

		static List<Tuple<string, XmlString>> MakeList(params string[] args)
		{
			var result = new List<Tuple<string, XmlString>>();
			Assert.That(args.Length %2, Is.EqualTo(0));
			for (var i = 0; i < args.Length / 2; i++)
			{
				result.Add(Tuple.Create(args[i * 2], XmlString.FromUnencoded(args[i * 2 + 1])));
			}
			return result;
		}
	}
}
