using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
			first.AddLanguageString("one", "a value", "en", false);
			Assert.That(first.SameAs(second), Is.False);
		}

		[Test]
		public void SameAs_DifferentKeys_False()
		{
			var first = new DataSet();
			var second = new DataSet();
			first.AddLanguageString("one", "a value", "en", false);
			second.AddLanguageString("two", "a value", "en", false);
			Assert.That(first.SameAs(second), Is.False);
		}

		[Test]
		public void SameAs_OneHasDifferentTextValue_False()
		{
			var first = new DataSet();
			var second = new DataSet();
			first.AddLanguageString("one", "a value", "en", false);
			second.AddLanguageString("one", "another value", "en", false);
			Assert.That(first.SameAs(second), Is.False);
		}

		[Test]
		public void SameAs_OneHasMoreTextValues_False()
		{
			var first = new DataSet();
			var second = new DataSet();
			first.AddLanguageString("one", "a value", "en", false);
			first.AddLanguageString("one", "another value", "fr", false);
			second.AddLanguageString("one", "a value", "en", false);
			Assert.That(first.SameAs(second), Is.False);
		}

		[Test]
		public void SameAs_OneHasDifferentLanguage_False()
		{
			var first = new DataSet();
			var second = new DataSet();
			first.AddLanguageString("one", "a value", "en", false);
			second.AddLanguageString("one", "a value", "de", false);
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

		private static DataSet MakeComplexDataSet()
		{
			var ds = new DataSet();
			ds.AddLanguageString("one", "a value", "en", false);
			ds.AddLanguageString("one", "another value", "de", false);
			ds.AddLanguageString("two", "another value", "fr", false);
			var values = new HashSet<KeyValuePair<string, string>>();
			values.Add(new KeyValuePair<string, string>("key", "value"));
			ds.UpdateXmatterPageDataAttributeSet("one", values);
			return ds;
		}
	}
}
