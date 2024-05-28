using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using Bloom.Book;
using BloomTemp;
using Newtonsoft.Json;
using NUnit.Framework;
using SIL.Extensions;
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
            var appearance = new AppearanceSettingsTest();
            // This comes from a default in the propertyDefinitions. If we switch to making the
            // default unspecified, this test will need to change.
            Assert.That(
                appearance.GetCssOwnPropsDeclaration(),
                Does.Contain($"--boolean-test-L2-show: doShow-css-will-ignore-this-and-use-default")
            );
            //appearance.Update(new { boolean-test-L2-show = false, foo = "blah" });
            appearance.UpdateFromJson("{\"boolean-test-L2-show\":false}");
            Assert.That(
                appearance.GetCssOwnPropsDeclaration(),
                Does.Contain("--boolean-test-L2-show: none;")
            );
        }

        [Test]
        public void GetCssOwnPropsDeclaration_WithBrandingAndXmatter_ProducesCorrectCss()
        {
            var appearance = new AppearanceSettingsTest();
            dynamic brandingSettings = JsonConvert.DeserializeObject<ExpandoObject>(
                @"{
  ""boolean-test-L2-show"": false,
  ""boolean-test-L3-show"": true,
  ""cover-topic-show"": false,
  ""cover-languageName-show"": false
}"
            );

            dynamic xmatterSettings = JsonConvert.DeserializeObject<ExpandoObject>(
                @"{
  ""cover-topic-show"": true
}"
            );

            var css = appearance.ToCss(
                brandingJson: brandingSettings,
                xmatterJson: xmatterSettings
            );
            var parts = css.Split(
                new[] { "/* From xmatter.json */" },
                StringSplitOptions.RemoveEmptyEntries
            );
            var xmatterCss = parts[1];
            parts = parts[0].Split(
                new[] { "/* From branding.json */" },
                StringSplitOptions.RemoveEmptyEntries
            );
            var brandingCss = parts[1];
            parts = parts[0].Split(
                new[] { "/* From this book's appearance settings */" },
                StringSplitOptions.RemoveEmptyEntries
            );
            ;
            var ownCss = parts[1];

            Assert.That(
                ownCss,
                Does.Contain("--boolean-test-L2-show: doShow-css-will-ignore-this-and-use-default;")
            );
            Assert.That(ownCss, Does.Contain($"--boolean-test-L3-show: none"));

            Assert.That(brandingCss, Does.Contain($"--boolean-test-L2-show: none"));

            Assert.That(
                brandingCss,
                Does.Contain("--boolean-test-L3-show: doShow-css-will-ignore-this-and-use-default;")
            );
            Assert.That(brandingCss, Does.Contain("--cover-topic-show: none;"));

            Assert.That(
                xmatterCss,
                Does.Contain("--cover-topic-show: doShow-css-will-ignore-this-and-use-default;")
            );
        }

        [Test]
        public void GetCssOwnPropsDeclaration_ItemVisibility_ChildOverrides_UsesChildValue()
        {
            var collectionAppearance = new AppearanceSettingsTest();
            collectionAppearance.UpdateFromJson("{\"boolean-test-L2-show\":false}");
            var bookAppearance = new AppearanceSettingsTest();
            bookAppearance.UpdateFromJson(
                "{\"groupsToOverrideFromParent\":[\"coverFields\"], \"boolean-test-L2-show\":true}"
            );
            Assert.IsTrue(
                bookAppearance
                    .GetCssOwnPropsDeclaration(collectionAppearance)
                    .IndexOf(
                        $"--boolean-test-L2-show: {AppearanceSettings.kDoShowValueForDisplay};"
                    ) > -1
            );

            bookAppearance.UpdateFromJson(
                "{\"groupsToOverrideFromParent\":[\"coverFields\"],\"boolean-test-L2-show\":false}"
            );

            Assert.IsTrue(
                bookAppearance
                    .GetCssOwnPropsDeclaration(collectionAppearance)
                    .IndexOf($"--boolean-test-L2-show: {AppearanceSettings.kHideValueForDisplay};")
                    > -1,
                bookAppearance.GetCssOwnPropsDeclaration(collectionAppearance).ToString()
            );
        }

        [Test]
        public void GetCssOwnPropsDeclaration_ItemVisibility_NoOverride_UsesParentValue()
        {
            var collectionAppearance = new AppearanceSettingsTest();

            var bookAppearance = new AppearanceSettingsTest();
            bookAppearance.UpdateFromJson(
                "{\"groupsToOverrideFromParent\":[], \"boolean-test-L2-show\":true}"
            );
            collectionAppearance.UpdateFromJson("{\"boolean-test-L2-show\":false}");
            Assert.IsTrue(
                bookAppearance
                    .GetCssOwnPropsDeclaration(collectionAppearance)
                    .IndexOf($"--boolean-test-L2-show: {AppearanceSettings.kHideValueForDisplay};")
                    > -1
            );

            collectionAppearance.UpdateFromJson("{\"boolean-test-L2-show\":true}");
            bookAppearance.UpdateFromJson(
                "{\"groupsToOverrideFromParent\":[\"some-randome-thing\"],\"boolean-test-L2-show\":false}"
            );
            Assert.IsTrue(
                bookAppearance
                    .GetCssOwnPropsDeclaration(collectionAppearance)
                    .IndexOf(
                        $"--boolean-test-L2-show: {AppearanceSettings.kDoShowValueForDisplay};"
                    ) > -1,
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
                    "/*{label: \"some test\";\r\n compatibleWithAppearanceVersion: 6.0;\r\n}*/ .marginBox{ left: 2mm}"
                )
            );
        }

        [Test]
        public void ToCss_ContainsSettingsFromJson()
        {
            var settings = new AppearanceSettingsTest();
            settings.UpdateFromJson(
                @"
{
  ""cssThemeName"": ""default"",
  ""boolean-test-L2-show"": false,
  ""boolean-test-L3-show"": true,
  ""cover-topic-show"": true,
  ""cover-languageName-show"": false
}"
            );
            var css = settings.ToCss();
            var fromSettings = css.Split(
                new string[] { "/* From this book's appearance settings */" },
                StringSplitOptions.None
            )[1];
            Assert.That(fromSettings, Does.Contain("--boolean-test-L2-show: none;"));
            Assert.That(
                fromSettings,
                Does.Contain("--boolean-test-L3-show: doShow-css-will-ignore-this-and-use-default;")
            );
            Assert.That(
                fromSettings,
                Does.Contain("--cover-topic-show: doShow-css-will-ignore-this-and-use-default;")
            );
            Assert.That(fromSettings, Does.Contain("--cover-languageName-show: none;"));
        }

        [Test]
        public void ToCss_EmitsSpecialPageMargin_property()
        {
            var settings = new AppearanceSettings();
            settings.UpdateFromJson(
                @"
{
  ""cssThemeName"": ""default"",
  ""pageNumber-show"": false
}"
            );
            var css = settings.ToCss();
            Assert.That(css, Does.Contain("--pageNumber-show-multiplicand: 0;"));
            settings.UpdateFromJson("{\"pageNumber-show\": true}");
            css = settings.ToCss();
            Assert.That(css, Does.Contain("--pageNumber-show-multiplicand: 1;"));
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
        private string _cssOfEbookZeroMarginTheme;
        private string _cssOfSettingsObject;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _tempFolder = new TemporaryFolder("GetThemeAndSubstituteCssSuccessTests");
            _bookFolder = _tempFolder.Combine("book");
            Directory.CreateDirectory(_bookFolder);
            _settings = new AppearanceSettingsTest();
            var cssFilesToCheck = new[]
            {
                Tuple.Create(
                    "customBookStyles.css",
                    AppearanceMigratorTests.cssThatTriggersEbookZeroMarginTheme
                ),
                Tuple.Create(
                    "customCollectionStyles.css",
                    AppearanceMigratorTests.cssThatTriggersEbookZeroMarginTheme
                )
            };
            _pathToCustomCss = _settings.GetThemeAndSubstituteCss(cssFilesToCheck);
            var jsonSettings = Newtonsoft.Json.Linq.JObject.Parse("{\"page-margin-top\":\"15mm\"}");
            _settings.UpdateFromJson(jsonSettings.ToString());
            _settings.WriteToFolder(_bookFolder);
            _settings.WriteCssToFolder(_bookFolder);
            _resultingAppearance = new AppearanceSettingsTest();
            _resultingAppearance.UpdateFromFolder(_bookFolder);
            _resultingAppearance.CheckCssFilesForCompatibility(
                new[]
                {
                    cssFilesToCheck[0],
                    cssFilesToCheck[1],
                    Tuple.Create("customBookStyles2.css", RobustFile.ReadAllText(_pathToCustomCss))
                },
                true
            );

            _generatedAppearanceCss = RobustFile.ReadAllText(
                Path.Combine(_bookFolder, "appearance.css")
            );
            var splits = _generatedAppearanceCss.Split(
                new[]
                {
                    "from the current appearance theme, 'zero-margin-ebook'",
                    "/* From this book's appearance settings */"
                },
                StringSplitOptions.None
            );
            _cssOfDefaultTheme = splits[0];
            _cssOfEbookZeroMarginTheme = splits[1];
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
                        "efl-ebook-1",
                        "customBookStyles.css"
                    )
                )
            );
        }

        [Test]
        public void GetsRightTheme()
        {
            Assert.That(_resultingAppearance.CssThemeName, Is.EqualTo("zero-margin-ebook"));
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
            // One that is not overridden in zero-margin-ebook
            Assert.That(
                _generatedAppearanceCss,
                Does.Contain("--cover-margin-top: var(--page-margin);")
            );
        }

        [Test]
        public void SettingsCss_DoesNotHaveSettingsWithNullDefaults()
        {
            Assert.That(_cssOfSettingsObject, Does.Not.Contain("--marginBox-border-color:"));
        }

        [Test]
        public void AppearanceCss_HasThemeSettings()
        {
            // from efl-zero-margin-ebook
            Assert.That(_generatedAppearanceCss, Does.Contain("--page-margin: 3mm;"));
            Assert.That(
                _generatedAppearanceCss,
                Does.Contain(":not(.bloom-interactive-page).numberedPage.Device16x9Landscape")
            );
            Assert.That(_generatedAppearanceCss, Does.Contain("--page-margin: 0mm;"));
        }

        [Test]
        public void AppearanceCss_HasMigrationSettings()
        {
            // from efl-zero-margin-ebook
            Assert.That(_generatedAppearanceCss, Does.Contain("--pageNumber-show: none;"));
        }

        /// <summary>
        /// Regression test, we should never create rules in bloom books that target :root because they
        /// don't work in Bloom Player.
        /// </summary>
        [Test]
        public void AppearanceCss_HasNoRootRules()
        {
            // from efl-zero-margin-ebook
            Assert.That(_generatedAppearanceCss, Does.Not.Contain(":root"));
        }

        /// <summary>
        /// The following several tests are partly to test that the order of the css is as expected:
        /// default theme, then chosen theme, then settings object.
        /// </summary>
        [Test]
        public void CssOfDefaultTheme_HasExpectedMargins()
        {
            // we expect the default settings before the theme ones
            Assert.That(_cssOfDefaultTheme, Does.Contain("--page-margin: 12mm;"));
            Assert.That(
                _cssOfDefaultTheme,
                Does.Contain("--cover-margin-top: var(--page-margin);")
            );
            Assert.That(
                _cssOfDefaultTheme,
                Does.Contain(
                    @".LegalLandscape {
    --page-margin: 15mm;"
                )
            );
            Assert.That(
                _cssOfDefaultTheme,
                Does.Contain(
                    @".A6Landscape {
    --page-margin: 10mm;"
                )
            );
            Assert.That(
                _cssOfDefaultTheme,
                Does.Contain(
                    @".bloom-page[class*=""Device""] {
    --page-margin: 10px;"
                )
            );
            Assert.That(
                _cssOfDefaultTheme,
                Does.Contain(
                    @".Cm13Landscape {
    --page-margin: 5mm;"
                )
            );
        }

        [Test]
        public void CssOfDefaultTheme_SetsDefaultTopicVisibility()
        {
            Assert.That(
                _cssOfDefaultTheme,
                Does.Contain("--cover-topic-show: doShow-css-will-ignore-this-and-use-default")
            );
        }

        [Test]
        public void CssOfChosenTheme_OverridesMargin()
        {
            Assert.That(_generatedAppearanceCss, Does.Contain("--page-margin: 12mm;"));
            Assert.That(
                _generatedAppearanceCss,
                Does.Contain(":not(.bloom-interactive-page).numberedPage.Device16x9Landscape")
            );
            Assert.That(_cssOfEbookZeroMarginTheme, Does.Contain("--page-margin: 0mm;"));
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
                Does.Contain("--boolean-test-L2-show: doShow-css-will-ignore-this-and-use-default")
            );
        }
    }
}

/// <summary>
/// A class for testing the AppearanceSettings class. It is a subclass of AppearanceSettings so that
/// we can add a couple of spurious properties to it for testing. These properties replace the cover-title-LX-show
/// properties, which we don't want to count on in testing CssDisplayVariableDef, since we are no longer counting
/// on generated variables to control visibility of those fields, and may stop generating them altogether.
/// </summary>
public class AppearanceSettingsTest : AppearanceSettings
{
    public AppearanceSettingsTest()
    {
        var testDef1 = new CssDisplayVariableDef("boolean-test-L2-show", "coverFields", true);
        var testDef2 = new CssDisplayVariableDef("boolean-test-L3-show", "coverFields", false);
        propertyDefinitions = propertyDefinitions.Concat(new[] { testDef1, testDef2, }).ToArray();
        testDef1.SetDefault(_properties);
        testDef2.SetDefault(_properties);
    }
}
