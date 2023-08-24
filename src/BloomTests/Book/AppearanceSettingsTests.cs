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
			Assert.IsTrue(appearance.TestOnlyPropertiesAccess.coverShowTitleL2);
			Assert.IsFalse(appearance.TestOnlyPropertiesAccess.coverShowTitleL3);
		}


		[Test]
		public void GetCssRootDeclaration_HasCorrectTitleFieldsVariableValues()
		{
			var appearance = new AppearanceSettings();
			Assert.IsTrue(appearance.GetCssRootDeclaration().IndexOf($"--coverShowTitleL2: {AppearanceSettings.kDoShowValueForDisplay};") > -1);
			//appearance.Update(new { coverShowTitleL2 = false, foo = "blah" });
			appearance.UpdateFromJson("{coverShowTitleL2:false}");
			Assert.IsTrue(appearance.GetCssRootDeclaration().IndexOf("--coverShowTitleL2: none;") > -1);
		}

		[Test]
		public void GetCssRootDeclaration_ItemVisibility_ChildOverrides_UsesChildValue()
		{
			var collectionAppearance = new AppearanceSettings();
			collectionAppearance.UpdateFromJson("{\"coverShowTitleL2\":false}");
			var bookAppearance = new AppearanceSettings();
			bookAppearance.UpdateFromJson("{\"groupsToOverrideFromParent\":[\"coverFields\"], \"coverShowTitleL2\":true}");
			Assert.IsTrue(bookAppearance.GetCssRootDeclaration(collectionAppearance).IndexOf($"--coverShowTitleL2: {AppearanceSettings.kDoShowValueForDisplay};") > -1);

			bookAppearance.UpdateFromJson("{\"groupsToOverrideFromParent\":[\"coverFields\"],\"coverShowTitleL2\":false}");

			Assert.IsTrue(bookAppearance.GetCssRootDeclaration(collectionAppearance).IndexOf($"--coverShowTitleL2: {AppearanceSettings.kHideValueForDisplay};") > -1, bookAppearance.GetCssRootDeclaration(collectionAppearance).ToString());
		}
		[Test]
		public void GetCssRootDeclaration_ItemVisibility_NoOverride_UsesParentValue()
		{
			var collectionAppearance = new AppearanceSettings();
			
			var bookAppearance = new AppearanceSettings();
			bookAppearance.UpdateFromJson("{\"groupsToOverrideFromParent\":[], \"coverShowTitleL2\":true}");
			collectionAppearance.UpdateFromJson("{\"coverShowTitleL2\":false}");
			Assert.IsTrue(bookAppearance.GetCssRootDeclaration(collectionAppearance).IndexOf($"--coverShowTitleL2: {AppearanceSettings.kHideValueForDisplay};") > -1);

			collectionAppearance.UpdateFromJson("{\"coverShowTitleL2\":true}");
			bookAppearance.UpdateFromJson("{\"groupsToOverrideFromParent\":[\"some-randome-thing\"],\"coverShowTitleL2\":false}");
			Assert.IsTrue(bookAppearance.GetCssRootDeclaration(collectionAppearance).IndexOf($"--coverShowTitleL2: {AppearanceSettings.kDoShowValueForDisplay};") > -1, bookAppearance.GetCssRootDeclaration(collectionAppearance).ToString());
		}


		/* [Test]
		public void GetCssRootDeclaration_ItemVisibility_ChildOverridesButDoesnotHaveProperty_UsesParentValue()
		{
			// I'm leaving this empty test here just to remind us that actually if a child doesn't have a property at all, currently the code will not notice that the parent has
			// such a property, and at will not be emitted at all. This may not be sufficient for our needs, but until that is clear, this case just isn't handled.
		}
		*/
	}
}
