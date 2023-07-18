using NUnit.Framework;

namespace BloomTests.Book
{
	public class AppearanceSettingsTests
	{
		[SetUp]
		public void SetupFixture()
		{

		}

		[Test]
		public void NewGetsDefaults()
		{
			var appearance = new AppearanceSettings();
			Assert.AreEqual("default", appearance.TestOnlyPropertiesAccess.cssThemeName);
			Assert.AreEqual("yellow", appearance.TestOnlyPropertiesAccess.coverColor);
			Assert.IsTrue(appearance.TestOnlyPropertiesAccess.coverShowTitleL2);
			Assert.IsFalse(appearance.TestOnlyPropertiesAccess.coverShowTitleL3);
		}

		[Test]
		public void GetCssRootDeclaration_HasCoverColorVariable()
		{
			var appearance = new AppearanceSettings();
			Assert.IsTrue(appearance.GetCssRootDeclaration().IndexOf("--coverColor: yellow;") > -1);
		}

		[Test]
		public void GetCssRootDeclaration_HasCorrectTitleFieldsVariableValues()
		{
			var appearance = new AppearanceSettings();
			Assert.IsTrue(appearance.GetCssRootDeclaration().IndexOf("--coverShowTitleL2: ignore-this;") > -1);
			//appearance.Update(new { coverShowTitleL2 = false, foo = "blah" });
			appearance.UpdateFromJson("{coverShowTitleL2:false}");
			Assert.IsTrue(appearance.GetCssRootDeclaration().IndexOf("--coverShowTitleL2: none;") > -1);
		}

		[Test]
		public void GetCssRootDeclaration_ParentOverridesByDefault()
		{
			var collectionAppearance = new AppearanceSettings();
			collectionAppearance.UpdateFromJson("{coverColor:\"theCollectionsColor\"}");
			var bookAppearance = new AppearanceSettings();
			bookAppearance.UpdateFromJson("{coverColor:\"ourCustomBookColor\", overrides:[]}");
			Assert.IsTrue(bookAppearance.GetCssRootDeclaration(collectionAppearance).IndexOf("--coverColor: theCollectionsColor;") > -1);
			bookAppearance.UpdateFromJson("{coverColor:\"ourCustomBookColor\", overrides:[\"colors\" ]}");
			var css = bookAppearance.GetCssRootDeclaration(collectionAppearance);
			Assert.IsTrue(css.IndexOf("--coverColor: ourCustomBookColor;") > -1);
		}
	}
}
