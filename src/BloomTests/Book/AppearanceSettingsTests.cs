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
			Assert.AreEqual("default", appearance.TestOnlyPropertyAccess("cssThemeName"));
			Assert.AreEqual("yellow", appearance.TestOnlyPropertyAccess("coverColor"));
			Assert.IsTrue(appearance.TestOnlyPropertyAccess("coverShowTitleL2"));
			Assert.IsFalse(appearance.TestOnlyPropertyAccess("coverShowTitleL3"));
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
			appearance.Update(new { coverShowTitleL2 = false });
			Assert.IsTrue(appearance.GetCssRootDeclaration().IndexOf("--coverShowTitleL2: none;") > -1);
		}

		[Test]
		public void GetCssRootDeclaration_ParentOverridesByDefault()
		{
			var collectionAppearance = new AppearanceSettings();
			collectionAppearance.Update(new { coverColor = "theCollectionsColor" });
			var bookAppearance = new AppearanceSettings();
			bookAppearance.Update(new { coverColor = "ourCustomBookColor", overrides = new[] { "" } });
			Assert.IsTrue(bookAppearance.GetCssRootDeclaration().IndexOf("--coverColor: theCollectionsColor;") > -1);
			bookAppearance.Update(new { coverColor = "ourCustomBookColor", overrides = new[] { "colors" } });
			Assert.IsTrue(bookAppearance.GetCssRootDeclaration().IndexOf("--coverColor: ourCustomBookColor;") > -1);
		}
	}
}
