using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloom.Book;
using Bloom.Collection;
using BloomTemp;
using NUnit.Framework;
using Palaso.Text;

namespace BloomTests.Collection
{
	[TestFixture]
	public class CollectionSettingsTests
	{
		[Test]
		public void Save_NoCustomVariables_Fine()
		{
			var c = new CollectionSettings();
			using(var f = new TempFile())
			{
				c.SettingsFilePath = f.Path;
				c.Save();
			}
		}
		[Test]
		public void Load_NoCustomVariables_Fine()
		{
			var c = new CollectionSettings();
			using (var f = new TempFile())
			{
				c.SettingsFilePath = f.Path;
				c.Save();
				var n = new CollectionSettings();
				n.SettingsFilePath=f.Path;
				n.Load();
				Assert.AreEqual(0, n.CustomCollectionVariables.Count);
			}
		}
		[Test]
		public void SaveAndLoad_HasCustomVariables_RoundTripped()
		{
			var c = new CollectionSettings();
			using (var f = new TempFile())
			{
				c.SettingsFilePath = f.Path;
				var value = new MultiTextBase();
				value.SetAlternative("en","bread");
				value.SetAlternative("fr","pain");
				c.CustomCollectionVariables.Add("bread",value);
				c.Save();
				var n = new CollectionSettings();
				n.SettingsFilePath = f.Path;
				n.Load();
				Assert.AreEqual(1, n.CustomCollectionVariables.Count);
				Assert.AreEqual("bread", n.CustomCollectionVariables["bread"].GetExactAlternative("en"));
				Assert.AreEqual("pain", n.CustomCollectionVariables["bread"].GetExactAlternative("fr"));
			}
		}
	}
}
