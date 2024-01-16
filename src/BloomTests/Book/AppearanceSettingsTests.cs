using System;
using System.Collections.Generic;
using System.IO;
using Bloom.Book;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;

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
        }

        [Test]
        public void NewDoesNotGetDefaultsForNulls()
        {
            var appearance = new AppearanceSettings();
            Assert.That(
                ((IDictionary<string, object>)appearance.TestOnlyPropertiesAccess).Keys,
                Does.Not.Contain("marginBox-border-color")
            );
        }

        [Test]
        public void GetCssOwnPropsDeclaration_HasCorrectTitleFieldsVariableValues()
        {
            var appearance = new AppearanceSettings();
            // This comes from a default in the propertyDefinitions. If we switch to making the
            // default unspecified, this test will need to change.
            Assert.That(
                appearance.GetCssOwnPropsDeclaration(),
                Does.Contain($"--cover-title-L2-show: doShow-css-will-ignore-this-and-use-default")
            );
            //appearance.Update(new { cover-title-L2-show = false, foo = "blah" });
            appearance.UpdateFromJson("{\"cover-title-L2-show\":false}");
            Assert.That(
                appearance.GetCssOwnPropsDeclaration(),
                Does.Contain("--cover-title-L2-show: none;")
            );
        }

        [Test]
        public void GetCssOwnPropsDeclaration_ItemVisibility_ChildOverrides_UsesChildValue()
        {
            var collectionAppearance = new AppearanceSettings();
            collectionAppearance.UpdateFromJson("{\"cover-title-L2-show\":false}");
            var bookAppearance = new AppearanceSettings();
            bookAppearance.UpdateFromJson(
                "{\"groupsToOverrideFromParent\":[\"coverFields\"], \"cover-title-L2-show\":true}"
            );
            Assert.IsTrue(
                bookAppearance
                    .GetCssOwnPropsDeclaration(collectionAppearance)
                    .IndexOf($"--cover-title-L2-show: {AppearanceSettings.kDoShowValueForDisplay};")
                    > -1
            );

            bookAppearance.UpdateFromJson(
                "{\"groupsToOverrideFromParent\":[\"coverFields\"],\"cover-title-L2-show\":false}"
            );

            Assert.IsTrue(
                bookAppearance
                    .GetCssOwnPropsDeclaration(collectionAppearance)
                    .IndexOf($"--cover-title-L2-show: {AppearanceSettings.kHideValueForDisplay};")
                    > -1,
                bookAppearance.GetCssOwnPropsDeclaration(collectionAppearance).ToString()
            );
        }

        [Test]
        public void GetCssOwnPropsDeclaration_ItemVisibility_NoOverride_UsesParentValue()
        {
            var collectionAppearance = new AppearanceSettings();

            var bookAppearance = new AppearanceSettings();
            bookAppearance.UpdateFromJson(
                "{\"groupsToOverrideFromParent\":[], \"cover-title-L2-show\":true}"
            );
            collectionAppearance.UpdateFromJson("{\"cover-title-L2-show\":false}");
            Assert.IsTrue(
                bookAppearance
                    .GetCssOwnPropsDeclaration(collectionAppearance)
                    .IndexOf($"--cover-title-L2-show: {AppearanceSettings.kHideValueForDisplay};")
                    > -1
            );

            collectionAppearance.UpdateFromJson("{\"cover-title-L2-show\":true}");
            bookAppearance.UpdateFromJson(
                "{\"groupsToOverrideFromParent\":[\"some-randome-thing\"],\"cover-title-L2-show\":false}"
            );
            Assert.IsTrue(
                bookAppearance
                    .GetCssOwnPropsDeclaration(collectionAppearance)
                    .IndexOf($"--cover-title-L2-show: {AppearanceSettings.kDoShowValueForDisplay};")
                    > -1,
                bookAppearance.GetCssOwnPropsDeclaration(collectionAppearance).ToString()
            );
        }

        /* [Test]
        public void GetCssOwnPropsDeclaration_ItemVisibility_ChildOverridesButDoesnotHaveProperty_UsesParentValue()
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

        [Test]
        public void ToCss_ContainsSettingsFromJson()
        {
            var settings = new AppearanceSettings();
            settings.UpdateFromJson(
                @"
{
  ""cssThemeName"": ""default"",
  ""cover-title-L2-show"": false,
  ""cover-title-L3-show"": true,
  ""cover-topic-show"": true,
  ""cover-languageName-show"": false
}"
            );
            var css = settings.ToCss();
            var fromSettings = css.Split(
                new string[] { "/* From this book's appearance settings */" },
                StringSplitOptions.None
            )[1];
            Assert.That(fromSettings, Does.Contain("--cover-title-L2-show: none;"));
            Assert.That(
                fromSettings,
                Does.Contain("--cover-title-L3-show: doShow-css-will-ignore-this-and-use-default;")
            );
            Assert.That(
                fromSettings,
                Does.Contain("--cover-topic-show: doShow-css-will-ignore-this-and-use-default;")
            );
            Assert.That(fromSettings, Does.Contain("--cover-languageName-show: none;"));
        }
    }

    /// <summary>
    /// Test primarily the GetThemeAndSubstituteCss method, though in the process, a good deal of
    /// the ToCss method of AppearanceSettings is also tested.
    /// </summary>
    public class GetThemeAndSubstituteCssSuccessTests
    {
        TemporaryFolder _tempFolder;
        string _bookFolder;
        AppearanceSettings _settings;
        private string _pathToCustomCss;
        AppearanceSettings _resultingAppearance;
        private string _generatedAppearanceCss;
        private string _cssOfDefaultTheme;
        private string _cssOfPurpleRoundedTheme;
        private string _cssOfSettingsObject;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _tempFolder = new TemporaryFolder("GetThemeAndSubstituteCssSuccessTests");
            _bookFolder = _tempFolder.Combine("book");
            Directory.CreateDirectory(_bookFolder);
            //RobustFile.WriteAllText(Path.Combine(_bookFolder, "customBookStyles.css"),
            //    AppearanceMigratorTests.cssThatTriggersPurpleRoundedTheme
            //);
            _settings = new AppearanceSettings();
            var cssFilesToCheck = new[]
            {
                Tuple.Create(
                    "customBookStyles.css",
                    AppearanceMigratorTests.cssThatTriggersPurpleRoundedTheme
                ),
                Tuple.Create(
                    "customCollectionStyles.css",
                    AppearanceMigratorTests.cssThatTriggersPurpleRoundedTheme
                )
            };
            _pathToCustomCss = _settings.GetThemeAndSubstituteCss(cssFilesToCheck);
            var jsonSettings = Newtonsoft.Json.Linq.JObject.Parse("{\"page-margin-top\":\"15mm\"}");
            _settings.UpdateFromJson(jsonSettings.ToString());
            _settings.WriteToFolder(_bookFolder);
            _resultingAppearance = new AppearanceSettings();
            _resultingAppearance.UpdateFromFolder(_bookFolder);
            _resultingAppearance.Initialize(
                new[]
                {
                    cssFilesToCheck[0],
                    cssFilesToCheck[1],
                    Tuple.Create("customBookStyles2.css", RobustFile.ReadAllText(_pathToCustomCss))
                }
            );

            _generatedAppearanceCss = RobustFile.ReadAllText(
                Path.Combine(_bookFolder, "appearance.css")
            );
            var splits = _generatedAppearanceCss.Split(
                new[]
                {
                    "from the current appearance theme, 'purple-rounded'",
                    "/* From this book's appearance settings */"
                },
                StringSplitOptions.None
            );
            _cssOfDefaultTheme = splits[0];
            _cssOfPurpleRoundedTheme = splits[1];
            _cssOfSettingsObject = splits[2];
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _tempFolder.Dispose();
        }

        [Test]
        public void ReturnsCorrectCss()
        {
            Assert.That(
                _pathToCustomCss,
                Is.EqualTo(
                    Path.Combine(
                        AppearanceMigrator.GetFolderContainingAppearanceMigrations(),
                        "purpleRounded",
                        "customBookStyles.css"
                    )
                )
            );
        }

        [Test]
        public void GetsRightTheme()
        {
            Assert.That(_resultingAppearance.CssThemeName, Is.EqualTo("purple-rounded"));
        }

        [Test]
        public void LoadsAppearanceSettings()
        {
            Assert.That(_resultingAppearance.ShouldUseAppearanceCss, Is.True);
        }

        [Test]
        public void DoesNotLoadOldCustomCss()
        {
            Assert.That(_resultingAppearance.ShouldUseCustomBookStyles, Is.False);
            Assert.That(_resultingAppearance.ShouldUseCustomCollectionStyles, Is.False);
        }

        [Test]
        public void LoadsNewCustomCss()
        {
            Assert.That(_resultingAppearance.ShouldUseCustomBookStyles2, Is.True);
        }

        [Test]
        public void UsesNewBasePage()
        {
            Assert.That(_resultingAppearance.BasePageCssName, Is.EqualTo("basePage.css"));
        }

        [Test]
        public void AppearanceCss_HasDefaultSettings()
        {
            // One that is not overridden in purple-rounded
            Assert.That(_generatedAppearanceCss, Does.Contain("--cover-margin-top: 12mm;"));
        }

        [Test]
        public void SettingsCss_DoesNotHaveSettingsWithNullDefaults()
        {
            Assert.That(_cssOfSettingsObject, Does.Not.Contain("--marginBox-border-color:"));
        }

        [Test]
        public void AppearanceCss_HasThemeSettings()
        {
            // from purple-rounded
            Assert.That(_generatedAppearanceCss, Does.Contain("--marginBox-border-radius: 25px;"));
        }

        [Test]
        public void AppearanceCss_HasMigrationSettings()
        {
            // from purple-rounded
            Assert.That(_generatedAppearanceCss, Does.Contain("--page-background-color: red"));
        }

        /// <summary>
        /// Regression test, we should never create rules in bloom books that target :root because they
        /// don't work in Bloom Player.
        /// </summary>
        [Test]
        public void AppearanceCss_HasNoRootRules()
        {
            // from purple-rounded
            Assert.That(_generatedAppearanceCss, Does.Not.Contain(":root"));
        }

        /// <summary>
        /// The following several tests are partly to test that the order of the css is as expected:
        /// default theme, then chosen theme, then settings object.
        /// </summary>
        [Test]
        public void CssOfDefaultTheme_HasExpectedMargin()
        {
            // we expect the default settings before the theme ones
            Assert.That(_cssOfDefaultTheme, Does.Contain("--page-margin-top: 12mm;"));
        }

        [Test]
        public void CssOfDefaultTheme_SetsDefaultTitleVisibility()
        {
            Assert.That(
                _cssOfDefaultTheme,
                Does.Contain("--cover-title-L2-show: doShow-css-will-ignore-this-and-use-default")
            );
        }

        [Test]
        public void CssOfChosenTheme_OverridesMargin()
        {
            Assert.That(_cssOfPurpleRoundedTheme, Does.Contain("--page-margin-top: 7mm;"));
        }

        [Test]
        public void CssOfChosenTheme_DoesNotOverrideTitleVisibility()
        {
            Assert.That(_cssOfPurpleRoundedTheme, Does.Not.Contain("--cover-title-L2-show:")); // not overridden in the purple-rounded theme
        }

        [Test]
        public void CssOfSettingsObject_OverridesMargin()
        {
            Assert.That(_cssOfSettingsObject, Does.Contain("--page-margin-top: 15mm;"));
        }

        [Test]
        public void CssOfSettingsObject_OverridesTitleVisibility()
        {
            // This property is only set by initialization of the AppearanceSettings object from the propertyDefinitions.
            // If we decide to support a "not specified" value, we'll need to change this test.
            Assert.That(
                _cssOfSettingsObject,
                Does.Contain("--cover-title-L2-show: doShow-css-will-ignore-this-and-use-default")
            );
        }
    }
}
