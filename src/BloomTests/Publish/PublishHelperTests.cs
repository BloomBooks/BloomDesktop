using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom;
using Bloom.Book;
using Bloom.Publish;
using Bloom.SafeXml;
using Bloom.SubscriptionAndFeatures;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Publish
{
    /// <summary>
    /// This class is used to test internals of the PublishHelper class.
    /// </summary>
    internal class TestPublishHelper : PublishHelper
    {
        internal Dictionary<string, string> MapIdToDisplay => this._mapIdToDisplay;
        internal Dictionary<string, FontInfo> MapIdToFontInfo => this._mapIdToFontInfo;

        internal void TestStoreFontUsed(SafeXmlElement elt)
        {
            StoreFontUsed(elt);
        }

        internal bool TestIsRealTextDisplayedInDesiredFont(
            SafeXmlElement element,
            FontInfo desiredFont
        )
        {
            return IsRealTextDisplayedInDesiredFont(element, desiredFont);
        }
    }

    [TestFixture]
    public class PublishHelperTests
    {
        private TestPublishHelper _helper;

        [OneTimeSetUp]
        public void Setup()
        {
            _helper = new TestPublishHelper();
            InitializeHelperMaps();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            _helper.Dispose();
            _helper = null;
        }

        [TestCase("width", "width: 1433.16px; top: -146.703px; left: -706.162px;", 1433.16)]
        [TestCase("top", "width: 1433.16px; top: -146.703px; left: -706.162px;", -146.703)]
        [TestCase("left", "width: 1433.16px; top: -146.703px; left: -706.162px;", -706.162)]
        [TestCase("left", "some silly nonsence", 0)]
        [TestCase("left", "width: 20", 0)]
        public void GetNumberFromPx(string label, string input, double expected)
        {
            Assert.That(
                Math.Abs(expected - PublishHelper.GetNumberFromPx(label, input)),
                Is.LessThan(0.001)
            );
        }

        private string _htmlCoverPage =
            @"
<div class=""bloom-page cover coverColor bloom-frontMatter frontCover outsideFrontCover side-right Device16x9Landscape"" data-page=""required singleton"" data-export=""front-matter-cover"" data-xmatter-page=""frontCover"" id=""adff8e4d-d01d-45bb-8325-9b5e28f980a5"" lang=""tpi"" data-page-number="""">
  <div class=""marginBox"" id=""PublishTempIdXXYY1"">
    <div class=""bloom-translationGroup bookTitle"" data-default-languages=""V,N1"" data-visibility-variable=""cover-title-LN-show"" id=""PublishTempIdXXYY3"">
      <div class=""bloom-editable"" lang=""z"" data-book=""bookTitle"" id=""PublishTempIdXXYY4"" />
      <div class=""bloom-editablebloom-visibility-code-on bloom-content1 bloom-contentFirst "" lang=""en"" data-book=""bookTitle"" style=""padding-bottom: 0px;"" id=""PublishTempIdXXYY5"">
        <p>My New Car</p>
      </div>
      <div class=""bloom-editable bloom-visibility-code-on bloom-contentNational1 bloom-contentSecond"" lang=""tpi"" data-book=""bookTitle"" id=""PublishTempIdXXYY6"">
        <p>Nupela kar bilong mi</p>
      </div>
      <div class=""bloom-editable bloom-contentNational2"" lang=""gaj"" data-book=""bookTitle"" id=""PublishTempIdXXYY7"" />
    </div>
  </div>
</div>
";

        [Test]
        public void FrontCoverPage_FontsUsed()
        {
            _helper.FontsUsed.Clear();
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(_htmlCoverPage);
            foreach (SafeXmlElement elt in doc.SafeSelectNodes(".//div[@id]"))
            {
                _helper.TestStoreFontUsed(elt);
            }
            Assert.That(_helper.FontsUsed.Count, Is.EqualTo(2));
            var firstFont = new PublishHelper.FontInfo
            {
                fontFamily = "ABeeZee",
                fontStyle = "normal",
                fontWeight = "700"
            };
            var secondFont = new PublishHelper.FontInfo
            {
                fontFamily = "Aileron",
                fontStyle = "normal",
                fontWeight = "700"
            };
            Assert.That(_helper.FontsUsed.Contains(firstFont), Is.True);
            Assert.That(_helper.FontsUsed.Contains(secondFont), Is.True);
        }

        [Test]
        public void FrontCoverPage_IsRealTextDisplayedInDesiredFont()
        {
            _helper.FontsUsed.Clear();
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(_htmlCoverPage);
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.True
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.True
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
        }

        private string _htmlNumberedPage =
            @"<div class=""bloom-page numberedPage"" id=""40fc8170-b82b-48ec-9ce4-b5be38d4836c"" data-page-number=""1"" lang="""">
  <div class=""marginBox"" id=""PublishTempIdXXYY29"">
    <div class=""bloom-translationGroup bloom-trailingElement bloom-vertical-align-center"" data-default-languages=""auto"" style=""font-size: 16px;"" id=""PublishTempIdXXYY30"">
      <div class=""bloom-editable normal-style"" lang=""es"" style="""" id=""PublishTempIdXXYY31"">
        <p>Recibí un auto nuevo para mi cumpleaños.</p>
      </div>
      <div class=""bloom-editable normal-style bloom-visibility-code-on bloom-content1"" lang=""en"" style=""min-height: 24px;"" tabindex=""0"" data-languagetipcontent=""English"" id=""PublishTempIdXXYY32"">
        <p>I got a new car for my birthday</p>
      </div>
      <div class=""bloom-editable normal-style bloom-visibility-code-on bloom-content2"" lang=""tpi"" style=""min-height: 24px;"" tabindex=""0"" data-languagetipcontent=""English"" id=""PublishTempIdXXYY33"">
        <p>Mi kisim nupela kar long betde bilong mi</p>
      </div>
    </div>
  </div>
</div>
";

        [Test]
        public void NumberedPage_FontsUsed()
        {
            _helper.FontsUsed.Clear();
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(_htmlNumberedPage);
            foreach (SafeXmlElement elt in doc.SafeSelectNodes(".//div[@id]"))
            {
                _helper.TestStoreFontUsed(elt);
            }
            Assert.That(_helper.FontsUsed.Count, Is.EqualTo(3));
            var firstFont = new PublishHelper.FontInfo
            {
                fontFamily = "ABeeZee",
                fontStyle = "normal",
                fontWeight = "400"
            };
            var secondFont = new PublishHelper.FontInfo
            {
                fontFamily = "Aileron",
                fontStyle = "normal",
                fontWeight = "400"
            };
            var thirdFont = new PublishHelper.FontInfo
            {
                fontFamily = "Andika",
                fontStyle = "italic",
                fontWeight = "400"
            };
            var fourthFont = new PublishHelper.FontInfo
            {
                fontFamily = "Andika",
                fontStyle = "normal",
                fontWeight = "400"
            };
            Assert.That(_helper.FontsUsed.Contains(firstFont), Is.True);
            Assert.That(_helper.FontsUsed.Contains(secondFont), Is.True);
            Assert.That(_helper.FontsUsed.Contains(thirdFont), Is.True);
            Assert.That(_helper.FontsUsed.Contains(fourthFont), Is.False);
        }

        [Test]
        public void NumberedPage_IsRealTextDisplayedInDesiredFont()
        {
            _helper.FontsUsed.Clear();
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(_htmlNumberedPage);
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.True
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.True
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.True
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
        }

        private string _htmlPageWithDisplayedEmptyText =
            @"<div class=""bloom-page numberedPage"" id=""40fc8170-b82b-48ec-9ce4-b5be38d4836c"" data-page-number=""1"" lang="""">
  <div class=""marginBox"" id=""PublishTempIdXXYY29"">
    <div class=""bloom-translationGroup bloom-trailingElement bloom-vertical-align-center"" data-default-languages=""auto"" style=""font-size: 16px;"" id=""PublishTempIdXXYY30"">
      <div class=""bloom-editable normal-style"" lang=""es"" style="""" id=""PublishTempIdXXYY31"">
        <p></p>
      </div>
      <div class=""bloom-editable normal-style bloom-visibility-code-on bloom-content1"" lang=""en"" style=""min-height: 24px;"" tabindex=""0"" data-languagetipcontent=""English"" id=""PublishTempIdXXYY32"">
        <p></p>
      </div>
      <div class=""bloom-editable normal-style bloom-visibility-code-on bloom-content2"" lang=""tpi"" style=""min-height: 24px;"" tabindex=""0"" data-languagetipcontent=""English"" id=""PublishTempIdXXYY33"">
        <p></p>
      </div>
    </div>
  </div>
</div>
";

        private string _htmlPageWithHiddenEmptyText =
            @"<div class=""bloom-page numberedPage"" id=""3e9952c0-0d64-4812-bca3-145fe70b62a7"" data-page-number=""1"" lang="""">
  <div class=""marginBox"" id=""PublishTempIdXXYY41"">
    <div class=""bloom-translationGroup bloom-trailingElement bloom-vertical-align-center"" data-default-languages=""auto"" style=""font-size: 16px;"" id=""PublishTempIdXXYY42"">
      <div class=""bloom-editable normal-style"" lang=""es"" style="""" id=""PublishTempIdXXYY43"">
        <p></p>
      </div>
      <div class=""bloom-editable normal-style bloom-visibility-code-on bloom-content1"" lang=""en"" style=""min-height: 24px;"" tabindex=""0"" data-languagetipcontent=""English"" id=""PublishTempIdXXYY44"">
        <p></p>
      </div>
      <div class=""bloom-editable normal-style bloom-visibility-code-on bloom-content2"" lang=""tpi"" style=""min-height: 24px;"" tabindex=""0"" data-languagetipcontent=""English"" id=""PublishTempIdXXYY45"">
        <p></p>
      </div>
    </div>
  </div>
</div>
";

        [Test]
        public void PageWithDisplayedEmptyText_FontsUsed()
        {
            _helper.FontsUsed.Clear();
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(_htmlPageWithDisplayedEmptyText);
            foreach (SafeXmlElement elt in doc.SafeSelectNodes(".//div[@id]"))
            {
                _helper.TestStoreFontUsed(elt);
            }
            // This may be surprising, but empty text elements that are displayed cause the font
            // information to be stored.
            Assert.That(_helper.FontsUsed.Count, Is.EqualTo(3));
        }

        [Test]
        public void PageWithHiddenEmptyText_FontsUsed()
        {
            _helper.FontsUsed.Clear();
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(_htmlPageWithHiddenEmptyText);
            foreach (SafeXmlElement elt in doc.SafeSelectNodes(".//div[@id]"))
            {
                _helper.TestStoreFontUsed(elt);
            }
            // If empty text is not displayed, the font information is not stored.
            Assert.That(_helper.FontsUsed.Count, Is.EqualTo(0));
        }

        [TestCase("displayed")]
        [TestCase("hidden")]
        public void PageWithEmptyText_IsRealTextDisplayedInDesiredFont(string type)
        {
            _helper.FontsUsed.Clear();
            var doc = SafeXmlDocument.Create();
            // Whether it is displayed or not, IsRealTextDisplayedInDesiredFont should
            // return false for empty text.
            switch (type)
            {
                case "displayed":
                    doc.LoadXml(_htmlPageWithDisplayedEmptyText);
                    break;
                case "hidden":
                    doc.LoadXml(_htmlPageWithHiddenEmptyText);
                    break;
            }
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
        }

        private void InitializeHelperMaps()
        {
            _helper.MapIdToDisplay.Clear();
            _helper.MapIdToFontInfo.Clear();
            // cover page font and display data
            _helper.MapIdToDisplay.Add("adff8e4d-d01d-45bb-8325-9b5e28f980a5", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY1", "flex");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY3", "flex");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY4", "none");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY5", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY6", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY7", "none");
            _helper.MapIdToFontInfo.Add(
                "adff8e4d-d01d-45bb-8325-9b5e28f980a5",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY1",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY3",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY4",
                new PublishHelper.FontInfo
                {
                    fontFamily = "ABeeZee",
                    fontStyle = "normal",
                    fontWeight = "700"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY5",
                new PublishHelper.FontInfo
                {
                    fontFamily = "ABeeZee",
                    fontStyle = "normal",
                    fontWeight = "700"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY6",
                new PublishHelper.FontInfo
                {
                    fontFamily = "Aileron",
                    fontStyle = "normal",
                    fontWeight = "700"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY7",
                new PublishHelper.FontInfo
                {
                    fontFamily = "Andika",
                    fontStyle = "normal",
                    fontWeight = "700"
                }
            );

            _helper.MapIdToDisplay.Add("40fc8170-b82b-48ec-9ce4-b5be38d4836c", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY29", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY30", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY31", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY32", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY33", "block");
            _helper.MapIdToFontInfo.Add(
                "40fc8170-b82b-48ec-9ce4-b5be38d4836c",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY29",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY30",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY31",
                new PublishHelper.FontInfo
                {
                    fontFamily = "Andika",
                    fontStyle = "italic",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY32",
                new PublishHelper.FontInfo
                {
                    fontFamily = "ABeeZee",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY33",
                new PublishHelper.FontInfo
                {
                    fontFamily = "Aileron",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );

            _helper.MapIdToDisplay.Add("3e9952c0-0d64-4812-bca3-145fe70b62a7", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY41", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY42", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY43", "none");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY44", "none");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY45", "none");
            _helper.MapIdToFontInfo.Add(
                "3e9952c0-0d64-4812-bca3-145fe70b62a7",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY41",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY42",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY43",
                new PublishHelper.FontInfo
                {
                    fontFamily = "Andika",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY44",
                new PublishHelper.FontInfo
                {
                    fontFamily = "ABeeZee",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY45",
                new PublishHelper.FontInfo
                {
                    fontFamily = "Aileron",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
        }

        [Test]
        public void RemovePagesByFeatureSystem_RemovesGamePages()
        {
            var doc = SafeXmlDocument.Create();
            var bookXml =
                @"<div class=""bloom-book"">
                    <div class=""bloom-page  numberedPage side-right"" id=""gamePage1"" data-page-number=""1"" data-tool-id=""game"">
                        <div class=""pageLabel"">Game Page</div>
                    </div>
                    <div class=""bloom-page numberedPage side-left"" id=""normalPage1""  data-page-number=""2""/>
                    <div class=""bloom-page numberedPage side-right"" id=""widgetPage2""  data-page-number=""3"" data-tool-id=""game"">
                        <div class=""pageLabel"">Game Page</div>
                    </div>
                    <div class=""bloom-page numberedPage side-left"" id=""normalPage2""  data-page-number=""4""/>
                </div>";
            doc.LoadXml(bookXml);
            var pageElts = doc.SafeSelectNodes("//div[contains(@class,'bloom-page')]")
                .Cast<SafeXmlElement>()
                .ToList();
            var omittedPages = new Dictionary<string, int>();
            var subscription = new Subscription("");
            PublishHelper.RemovePagesByFeatureSystem(
                pageElts,
                PublishingMediums.BloomPub,
                omittedPages,
                subscription,
                false,
                "0123456789",
                false
            );

            void GamePagesRemoved()
            {
                var assertThatXml = AssertThatXmlIn.Element(doc.DocumentElement);
                assertThatXml.HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-page')]",
                    2
                );
                // changed to side-right since it's now an odd-numbered page
                assertThatXml.HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='normalPage1' and @class='bloom-page numberedPage side-right' and @data-page-number='1']",
                    1
                );
                assertThatXml.HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='normalPage2'and @class='bloom-page numberedPage side-left' and @data-page-number='2']",
                    1
                );
                Assert.That(pageElts.Count, Is.EqualTo(2));
            }
            GamePagesRemoved();
            Assert.That(omittedPages.Count, Is.EqualTo(1));
            Assert.That(
                omittedPages["Game Page"],
                Is.EqualTo(2) // 2 widget pages were removed
            );

            doc.LoadXml(bookXml);
            pageElts = doc.SafeSelectNodes("//div[contains(@class,'bloom-page')]")
                .Cast<SafeXmlElement>()
                .ToList();
            omittedPages = new Dictionary<string, int>();
            subscription = new Subscription("Test-727011-1339"); // allows widgets
            PublishHelper.RemovePagesByFeatureSystem(
                pageElts,
                PublishingMediums.Epub, // does not allow games
                omittedPages,
                subscription,
                false,
                "0123456789",
                false
            );
            GamePagesRemoved();
            // Currently we don't track pages omitted because of publishing medium
            Assert.That(omittedPages.Count, Is.EqualTo(0));
        }

        [Test]
        public void RemovePagesByFeatureSystem_ProOrHigherTierOrDerivative_DoesNotRemoveGamePages()
        {
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(
                @"<div class=""bloom-book"">
                    <div class=""bloom-page numberedPage side-right"" id=""gamePage1"" data-page-number=""1"" data-tool-id=""game"">
                        <div class=""pageLabel"">Game Page</div>
                    </div>
                    <div class=""bloom-page numberedPage side-left"" id=""normalPage1""  data-page-number=""2""/>
                    <div class=""bloom-page numberedPage side-right"" id=""gamePage2""  data-page-number=""3"" data-tool-id=""game"">
                        <div class=""pageLabel"">Game Page</div>
                    </div>
                    <div class=""bloom-page numberedPage side-left"" id=""normalPage2""  data-page-number=""4""/>
                </div>"
            );
            // Pro tier does not remove widgets
            var pageElts = doc.SafeSelectNodes("//div[contains(@class,'bloom-page')]")
                .Cast<SafeXmlElement>()
                .ToList();
            var omittedPages = new Dictionary<string, int>();
            var subscription = new Subscription("john_hatton@sil.org-123456-3028");
            PublishHelper.RemovePagesByFeatureSystem(
                pageElts,
                PublishingMediums.BloomPub,
                omittedPages,
                subscription,
                false,
                "0123456789",
                false
            );
            void checkNotRemoved()
            {
                var assertThatXml = AssertThatXmlIn.Element(doc.DocumentElement);
                assertThatXml.HasSpecifiedNumberOfMatchesForXpath(
                    "//div[contains(@class,'bloom-page')]",
                    4
                );

                Assert.That(pageElts.Count, Is.EqualTo(4));
                Assert.That(omittedPages.Count, Is.EqualTo(0));
            }
            checkNotRemoved();

            // higher tier is OK also
            subscription = new Subscription("Test-727011-1339");
            PublishHelper.RemovePagesByFeatureSystem(
                pageElts,
                PublishingMediums.BloomPub,
                omittedPages,
                subscription,
                false,
                "0123456789",
                false
            );
            checkNotRemoved();

            // In an original they would be removed for a basic subscription...
            subscription = new Subscription("");
            PublishHelper.RemovePagesByFeatureSystem(
                pageElts,
                PublishingMediums.BloomPub,
                omittedPages,
                subscription,
                // ...but not when the book is a derivative
                true,
                "0123456789",
                false
            );
            checkNotRemoved();
        }

        [Test]
        public void RemovePagesByFeatureSystem_DoesNotRemoveFullCoverImagePages()
        {
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(
                @"<div class=""bloom-book"">
                    <div class=""bloom-page cover coverColor bloom-frontMatter cover-is-image no-margin-page frontCover outsideFrontCover side-right Device16x9Landscape"" id=""fullCoverImage"" data-page-number=""1"">
                        <div class=""pageLabel"">Front Cover</div>
                    </div>
                    <div class=""bloom-page numberedPage side-left"" id=""normalPage1""  data-page-number=""2""/>
                </div>"
            );
            var pageElts = doc.SafeSelectNodes("//div[contains(@class,'bloom-page')]")
                .Cast<SafeXmlElement>()
                .ToList();
            var omittedPages = new Dictionary<string, int>();
            var subscription = new Subscription("");
            PublishHelper.RemovePagesByFeatureSystem(
                pageElts,
                PublishingMediums.BloomPub,
                omittedPages,
                subscription,
                false,
                "0123456789",
                false
            );

            var assertThatXml = AssertThatXmlIn.Element(doc.DocumentElement);
            assertThatXml.HasSpecifiedNumberOfMatchesForXpath(
                "//div[contains(@class,'bloom-page')]",
                2
            );

            Assert.That(pageElts.Count, Is.EqualTo(2));
            Assert.That(omittedPages.Count, Is.EqualTo(0));
        }

        [Test]
        public void RemoveClassesToDisableFeatures_RemovesClassesFromFullCoverImagePages()
        {
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(
                @"<div class=""bloom-book"">
                    <div class=""bloom-page cover coverColor bloom-frontMatter cover-is-image no-margin-page frontCover outsideFrontCover side-right Device16x9Landscape"" id=""fullCoverImage"" data-page-number=""1"">
                        <div class=""pageLabel"">Front Cover</div>
                    </div>
                    <div class=""bloom-page numberedPage side-left"" id=""normalPage1""  data-page-number=""2""/>
                </div>"
            );
            var pageElts = doc.SafeSelectNodes("//div[contains(@class,'bloom-page')]")
                .Cast<SafeXmlElement>()
                .ToList();
            var subscription = new Subscription("");
            var messages = new HashSet<string>();
            PublishHelper.RemoveClassesAndAttrsToDisableFeatures(
                pageElts,
                subscription,
                false,
                messages
            );
            AssertThatXmlIn
                .Element(pageElts[0])
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//div[@id='fullCoverImage' and @class='bloom-page cover coverColor bloom-frontMatter frontCover outsideFrontCover side-right Device16x9Landscape']",
                    1
                );
            Assert.That(
                messages,
                Has.Member(
                    // Would like it to be this, but the localization manager doesn't work well enough in unit tests.
                    //@"The feature ""Fill the front cover with a single image"" was removed from this book because it requires a higher subscription tier"
                    @"The feature ""BookSettings.CoverIsImage"" was removed from this book because it requires a higher subscription tier"
                )
            );
        }

        [Test]
        public void RemoveClassesToDisableFeatures_RemovesAttrsFromMusicPages()
        {
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(
                @"<div class=""bloom-book"">
                    <div class=""bloom-page numberedPage side-left"" id=""normalPage1""  data-backgroundaudio=""bird-eleven-labs.mp3"" data-page-number=""2""/>
                </div>"
            );
            var pageElts = doc.SafeSelectNodes("//div[contains(@class,'bloom-page')]")
                .Cast<SafeXmlElement>()
                .ToList();
            var subscription = new Subscription("");
            var messages = new HashSet<string>();
            PublishHelper.RemoveClassesAndAttrsToDisableFeatures(
                pageElts,
                subscription,
                false,
                messages
            );
            AssertThatXmlIn.Element(pageElts[0]).HasNoMatchForXpath("//div[@data-backgroundaudio]");
            Assert.That(
                messages,
                Has.Member(
                    // Would like it to be this, but the localization manager doesn't work well enough in unit tests.
                    //@"The feature ""Background Audio"" was removed from this book because it requires a higher subscription tier"
                    @"The feature ""PublishTab.Feature.Music"" was removed from this book because it requires a higher subscription tier"
                )
            );
        }
    }

    [TestFixture]
    public class ReallyCropImagesTests
    {
        private HtmlDom _dom;
        private TemporaryFolder _folder;
        private SafeXmlElement _p1bgi1;
        private SafeXmlElement _p1i1c1;
        private SafeXmlElement _p2i1c1;
        private SafeXmlElement _p2i1c2;
        private SafeXmlElement _p2i1c3;
        private SafeXmlElement _p2i1a;
        private SafeXmlElement _p2i1b;
        private SafeXmlElement _p2i2a;
        private SafeXmlElement _p1i2c4;
        private SafeXmlElement _p1i3c5;
        private SafeXmlElement _p2i3c5;
        private byte[] _manPngBytes;
        private byte[] _manJpgBytes;
        private byte[] _lady24bPngBytes;
        private byte[] _p1i1c1Bytes;
        private byte[] _p2i1c2Bytes;
        private byte[] _p2i1c3Bytes;
        private byte[] _p1i2c4Bytes;
        private byte[] _p1i3c54Bytes;

        public static string MakeImageCanvasElement(
            string imgId,
            string src,
            string ceStyle,
            string imgStyle = null,
            bool background = false
        )
        {
            var backgroundString = background ? " bloom-backgroundImage" : "";
            var imgStyleString = imgStyle == null ? "" : $"style=\"{imgStyle}\"";
            return $"<div class=\"bloom-canvas-element{backgroundString}\" style=\"{ceStyle}\" data-bubble=\"{{`version`:`1.0`,`style`:`none`,`tails`:[],`level`:4,`backgroundColors`:[`transparent`],`shadowOffset`:0}}\">"
                + $"  <div tabindex=\"0\" class=\"bloom-imageContainer bloom-leadingElement\">"
                + $"      <img id=\"{imgId}\" src=\"{src}\" {imgStyleString} />"
                + $"  </div>"
                + $"</div>";
        }

        [OneTimeSetUp]
        public void Setup()
        {
            _folder = new TemporaryFolder("ReallyCropImagesTests");
            // much simplified dom, but enough for this.
            // We want: three uncropped images on two pages with the same name, all uncropped. Should remain the same names and file.
            // Two more images using the same filename, both cropped the same, on two pages, should get cropped once to a new name.
            // More versions of this image on two pages all cropped differently. Should get distinct names.
            // Two images using the same file name, where the first one is cropped, should result in the
            // uncropped one keeping the name, and the cropped one getting a new name.
            // Two images using another file name, all cropped the same, should keep name but change file content.
            _dom = new HtmlDom(
                @"
<html><head></head><body>
    <div class=""bloom-page"" id=""page1"">
        <div class=""marginBox"" id=""image1"">
            <div class=""bloom-canvas"">"
                    + MakeImageCanvasElement(
                        "p1bgi1",
                        "man.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        null,
                        true
                    )
                    + MakeImageCanvasElement(
                        "p1i1c1",
                        "man.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -80px; top: -20px"
                    )
                    + MakeImageCanvasElement(
                        "p1i2c4",
                        "lady24b.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -80px; top: -20px"
                    )
                    + MakeImageCanvasElement(
                        "p1i3c5",
                        "man%203.jpg",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -80px; top: -20px"
                    )
                    + @"
            </div>
        </div>
    </div>
    <div class=""bloom-page"" id=""page2"">
        <div class=""marginBox"" id=""image1"">
            <div class=""bloom-canvas"">"
                    + MakeImageCanvasElement(
                        "p2i1a",
                        "man.png",
                        "height: 350px; left: 300px; top: 6px; width: 140px;"
                    )
                    + MakeImageCanvasElement(
                        "p2i1b",
                        "man.png",
                        "height: 350px; left: 20px; top: 6px; width: 140px;"
                    )
                    // same image and crop as p1i1c1, should use same cropped file
                    + MakeImageCanvasElement(
                        "p2i1c1",
                        "man.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -80px; top: -20px",
                        true
                    )
                    // same image p1i1c1, but a different img style, so should get a different cropped file
                    + MakeImageCanvasElement(
                        "p2i1c2",
                        "man.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -70px; top: -20px",
                        true
                    )
                    // This one needs a different crop because the canvas element width is different
                    + MakeImageCanvasElement(
                        "p2i1c3",
                        "man.png",
                        "height: 378.826px; left: 325px; top: 6px; width: 130px;",
                        "width: 280px; left: -70px; top: -20px",
                        true
                    )
                    + MakeImageCanvasElement(
                        "p2i2a",
                        "lady24b.png",
                        "height: 350px; left: 300px; top: 6px; width: 140px;"
                    )
                    + MakeImageCanvasElement(
                        "p2i3c5",
                        "man%203.jpg",
                        "height: 378.826px; left: 325px; top: 6px; width: 140px;",
                        "width: 280px; left: -80px; top: -20px"
                    )
                    + @"
            </div>
        </div>
    </div>
</body></html>"
            );
            var _pathToTestImages = "src\\BloomTests\\ImageProcessing\\images";
            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "man.png"
            );
            RobustFile.Copy(path, Path.Combine(_folder.Path, "man.png"));
            _manPngBytes = RobustFile.ReadAllBytes(path);

            path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "man.jpg"
            );
            // We are deliberately changing the name slightly to test for a particular problem
            // that was occurring when cropping renamed the output to an 'original' name
            // containing a space. The space needs to be in the name of the image that does
            // NOT occur uncropped.
            RobustFile.Copy(path, Path.Combine(_folder.Path, "man 3.jpg"));
            _manJpgBytes = RobustFile.ReadAllBytes(path);

            path = FileLocationUtilities.GetFileDistributedWithApplication(
                _pathToTestImages,
                "lady24b.png"
            );
            RobustFile.Copy(path, Path.Combine(_folder.Path, "lady24b.png"));
            _lady24bPngBytes = RobustFile.ReadAllBytes(path);

            // SUT
            PublishHelper.ReallyCropImages(_dom.RawDom, _folder.Path, _folder.Path);

            _p1bgi1 = _dom.SelectSingleNode("//img[@id='p1bgi1']");
            _p1i1c1 = _dom.SelectSingleNode("//img[@id='p1i1c1']");
            _p2i1c1 = _dom.SelectSingleNode("//img[@id='p2i1c1']");
            _p2i1c2 = _dom.SelectSingleNode("//img[@id='p2i1c2']");
            _p2i1c3 = _dom.SelectSingleNode("//img[@id='p2i1c3']");
            _p2i1a = _dom.SelectSingleNode("//img[@id='p2i1a']");
            _p2i1b = _dom.SelectSingleNode("//img[@id='p2i1b']");
            _p2i2a = _dom.SelectSingleNode("//img[@id='p2i2a']");
            _p1i2c4 = _dom.SelectSingleNode("//img[@id='p1i2c4']");
            _p1i3c5 = _dom.SelectSingleNode("//img[@id='p1i3c5']");
            _p2i3c5 = _dom.SelectSingleNode("//img[@id='p2i3c5']");

            _p1i1c1Bytes = RobustFile.ReadAllBytes(
                Path.Combine(_folder.Path, _p1i1c1.GetAttribute("src"))
            );
            _p2i1c2Bytes = RobustFile.ReadAllBytes(
                Path.Combine(_folder.Path, _p2i1c2.GetAttribute("src"))
            );
            _p2i1c3Bytes = RobustFile.ReadAllBytes(
                Path.Combine(_folder.Path, _p2i1c3.GetAttribute("src"))
            );
            _p1i2c4Bytes = RobustFile.ReadAllBytes(
                Path.Combine(_folder.Path, _p1i2c4.GetAttribute("src"))
            );
            _p1i3c54Bytes = RobustFile.ReadAllBytes(
                // Tempting to read from _p1i3c5.GetAttribute("src")).
                // But we're have assertions that the src is unchanged for this element.
                // What we want to ensure is that the original file was overwritten, rather
                // than a new one with the encoded name man%203.jpg being created.
                Path.Combine(_folder.Path, "man 3.jpg")
            );
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            _folder.Dispose();
        }

        [Test]
        public void UncroppedImages_SrcUnchanged()
        {
            Assert.That(_p1bgi1.GetAttribute("src"), Is.EqualTo("man.png"));
            Assert.That(_p2i1a.GetAttribute("src"), Is.EqualTo("man.png"));
            Assert.That(_p2i1b.GetAttribute("src"), Is.EqualTo("man.png"));
            Assert.That(_p2i2a.GetAttribute("src"), Is.EqualTo("lady24b.png"));
        }

        [Test]
        public void CroppedImage_HaveUncroppedWithSameFile_NewName()
        {
            var newSrc = _p1i1c1.GetAttribute("src");
            Assert.That(newSrc, Is.Not.EqualTo("man.png"));
            Assert.That(Path.GetExtension(newSrc), Is.EqualTo(".png"));
            newSrc = _p1i2c4.GetAttribute("src");
            Assert.That(newSrc, Is.Not.EqualTo("lady24b.png"));
            Assert.That(Path.GetExtension(newSrc), Is.EqualTo(".png"));
        }

        [Test]
        public void CroppedImage_HasDifferentContent()
        {
            Assert.That(
                _p1i1c1Bytes,
                Is.Not.EqualTo(_manPngBytes),
                "Cropped image should have different content from original"
            );
            Assert.That(
                _p2i1c2Bytes,
                Is.Not.EqualTo(_manPngBytes),
                "Cropped image should have different content from original"
            );
            Assert.That(
                _p2i1c3Bytes,
                Is.Not.EqualTo(_manPngBytes),
                "Cropped image should have different content from original"
            );
            Assert.That(
                _p1i2c4Bytes,
                Is.Not.EqualTo(_lady24bPngBytes),
                "Cropped image should have different content from original"
            );
        }

        [Test]
        public void CroppedImages_WithDifferentCrops_ProduceDifferentFiles()
        {
            Assert.That(
                _p1i1c1Bytes,
                Is.Not.EqualTo(_p2i1c2Bytes),
                "Cropped image file contents should be different"
            );
            Assert.That(
                _p1i1c1Bytes,
                Is.Not.EqualTo(_p2i1c3Bytes),
                "Cropped image file contents should be different"
            );
            Assert.That(
                _p2i1c3Bytes,
                Is.Not.EqualTo(_p2i1c2Bytes),
                "Cropped image file contents should be different"
            );
        }

        [Test]
        public void ImageWithSameSrcAndCrop_ShouldUseSameCroppedImgFile()
        {
            Assert.That(_p1i1c1.GetAttribute("src"), Is.EqualTo(_p2i1c1.GetAttribute("src")));
            Assert.That(_p2i3c5.GetAttribute("src"), Is.EqualTo(_p1i3c5.GetAttribute("src")));
        }

        [Test]
        public void ImageWithSameSrcButDifferentCrop_ShouldUseDifferentCroppedImgFile()
        {
            var i1c1Src = _p1i1c1.GetAttribute("src");
            var i2c2Src = _p2i1c2.GetAttribute("src");
            Assert.That(i1c1Src, Is.Not.EqualTo(i2c2Src));
            Assert.That(i2c2Src, Is.Not.EqualTo("man.png"));
        }

        [Test]
        public void CroppedImage_NoUncroppedWithSameName_UsesOriginalName()
        {
            Assert.That(_p1i3c5.GetAttribute("src"), Is.EqualTo("man%203.jpg"));
        }

        [Test]
        public void CroppedImage_NoUncroppedWithSameName_HasModifiedContent()
        {
            Assert.That(
                _p1i3c54Bytes,
                Is.Not.EqualTo(_manJpgBytes),
                "Cropped image should have different content from original"
            );
        }
    }
}
