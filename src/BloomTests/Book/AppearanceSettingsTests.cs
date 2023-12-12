using NUnit.Framework;

namespace BloomTests.Book
{
    public class AppearanceSettingsTests
    {
        [SetUp]
        public void SetupFixture() { }

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
            Assert.IsTrue(
                appearance
                    .GetCssRootDeclaration()
                    .IndexOf($"--coverShowTitleL2: {AppearanceSettings.kDoShowValueForDisplay};")
                    > -1
            );
            //appearance.Update(new { coverShowTitleL2 = false, foo = "blah" });
            appearance.UpdateFromJson("{coverShowTitleL2:false}");
            Assert.IsTrue(
                appearance.GetCssRootDeclaration().IndexOf("--coverShowTitleL2: none;") > -1
            );
        }

        [Test]
        public void GetCssRootDeclaration_ItemVisibility_ChildOverrides_UsesChildValue()
        {
            var collectionAppearance = new AppearanceSettings();
            collectionAppearance.UpdateFromJson("{\"coverShowTitleL2\":false}");
            var bookAppearance = new AppearanceSettings();
            bookAppearance.UpdateFromJson(
                "{\"groupsToOverrideFromParent\":[\"coverFields\"], \"coverShowTitleL2\":true}"
            );
            Assert.IsTrue(
                bookAppearance
                    .GetCssRootDeclaration(collectionAppearance)
                    .IndexOf($"--coverShowTitleL2: {AppearanceSettings.kDoShowValueForDisplay};")
                    > -1
            );

            bookAppearance.UpdateFromJson(
                "{\"groupsToOverrideFromParent\":[\"coverFields\"],\"coverShowTitleL2\":false}"
            );

            Assert.IsTrue(
                bookAppearance
                    .GetCssRootDeclaration(collectionAppearance)
                    .IndexOf($"--coverShowTitleL2: {AppearanceSettings.kHideValueForDisplay};")
                    > -1,
                bookAppearance.GetCssRootDeclaration(collectionAppearance).ToString()
            );
        }

        [Test]
        public void GetCssRootDeclaration_ItemVisibility_NoOverride_UsesParentValue()
        {
            var collectionAppearance = new AppearanceSettings();

            var bookAppearance = new AppearanceSettings();
            bookAppearance.UpdateFromJson(
                "{\"groupsToOverrideFromParent\":[], \"coverShowTitleL2\":true}"
            );
            collectionAppearance.UpdateFromJson("{\"coverShowTitleL2\":false}");
            Assert.IsTrue(
                bookAppearance
                    .GetCssRootDeclaration(collectionAppearance)
                    .IndexOf($"--coverShowTitleL2: {AppearanceSettings.kHideValueForDisplay};") > -1
            );

            collectionAppearance.UpdateFromJson("{\"coverShowTitleL2\":true}");
            bookAppearance.UpdateFromJson(
                "{\"groupsToOverrideFromParent\":[\"some-randome-thing\"],\"coverShowTitleL2\":false}"
            );
            Assert.IsTrue(
                bookAppearance
                    .GetCssRootDeclaration(collectionAppearance)
                    .IndexOf($"--coverShowTitleL2: {AppearanceSettings.kDoShowValueForDisplay};")
                    > -1,
                bookAppearance.GetCssRootDeclaration(collectionAppearance).ToString()
            );
        }

        /* [Test]
        public void GetCssRootDeclaration_ItemVisibility_ChildOverridesButDoesnotHaveProperty_UsesParentValue()
        {
            // I'm leaving this empty test here just to remind us that actually if a child doesn't have a property at all, currently the code will not notice that the parent has
            // such a property, and at will not be emitted at all. This may not be sufficient for our needs, but until that is clear, this case just isn't handled.
        }
        */


        static bool MayBeIncompatible(string css)
        {
            string unused;
            return AppearanceSettings.MayBeIncompatible("test", css, out unused);
        }

        [Test]
        public void MayBeIncompatible_null_False()
        {
            Assert.IsFalse(MayBeIncompatible(null));
        }

        [Test]
        public void MayBeIncompatible_empty_False()
        {
            Assert.IsFalse(MayBeIncompatible(""));
        }

        [Test]
        public void MayBeIncompatible_NotRelatedToMarginBox_False()
        {
            Assert.IsFalse(MayBeIncompatible(".foo{ background-color:yellow}"));
        }

        [Test]
        public void MayBeIncompatible_MarginBoxLeft_True()
        {
            Assert.IsTrue(MayBeIncompatible(".marginBox{ left: 2mm}"));
            // with more spaces
            Assert.IsTrue(MayBeIncompatible(" .marginBox {\r\n left: 2mm}"));
        }

        [Test]
        public void MayBeIncompatible_MarginBoxWidth_True()
        {
            Assert.IsTrue(MayBeIncompatible(".marginBox{ width: 2mm}"));
        }

        [Test]
        public void MayBeIncompatible_PaperSizeMarginBoxTop_True()
        {
            Assert.IsTrue(MayBeIncompatible(".A4Landscape .marginBox{ top: 2mm}"));
        }

        [Test]
        public void MayBeIncompatible_PaperSizeMarginBoxHeight_True()
        {
            Assert.IsTrue(MayBeIncompatible(".A4Landscape .marginBox{ height: 2mm}"));
        }

        [Test]
        public void MayBeIncompatible_MarginBoxMargin_True()
        {
            Assert.IsTrue(MayBeIncompatible(".marginBox{ margin: 2mm}"));
        }

        [Test]
        public void MayBeIncompatible_Variables_False()
        {
            Assert.IsFalse(MayBeIncompatible(".marginBox { --page-margin-left: 2mm}"));
        }

        [Test]
        public void MayBeIncompatible_InsideMarginBoxWidth_False()
        {
            Assert.IsFalse(MayBeIncompatible(".marginBox .foo{ width: 2mm}"));
        }

        [Test]
        public void MayBeIncompatible_compatibleWithAppearanceVersion_False()
        {
            Assert.IsFalse(
                MayBeIncompatible(
                    "/*{label: \"some test\";\r\n compatibleWithAppearanceVersion: 5.7;\r\n}*/ .marginBox{ left: 2mm}"
                )
            );
        }
    }
}
