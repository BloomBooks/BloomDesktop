using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Publish;
using Bloom.SafeXml;
using NUnit.Framework;

namespace BloomTests.Publish
{
    public class PublishHelperStaticTests
    {
        [Test]
        public void SimplifyBackgroundImages_MovesImgAndRemovesBackgroundCE_RemovesHasCEClass()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                @"
<div class=""bloom-page"">
    <div class=""bloom-canvas bloom-has-canvas-element"" data-bubble=""{`version`:`1.0`}"">
        <div class=""bloom-translationGroup bloom-imageDescription bloom-trailingElement"" style=""font-size: 16px;"">
            <div class=""bloom-editable ImageDescriptionEdit-style bloom-visibility-code-on bloom-content1 cke_editable cke_editable_inline cke_contents_ltr"" lang=""tuz"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""false"" role=""textbox"" aria-label=""false"" contenteditable=""true"" data-languagetipcontent=""Turka"">
                <p>A platypus swimming in greenish brown water </p>
            </div>
        </div>
        <div class=""bloom-canvas-element bloom-backgroundImage"">
            <div class=""bloom-imageContainer"">
                <img data-book=""coverImage"" src=""Duck-billed_platypus.jpg"" data-copyright=""Charles J Sharp""
                    data-creator=""Charles J Sharp"" data-license=""cc-by"" alt=""This image, Duck-billed_platypus.jpg, is missing or was loading too slowly.""/>
            </div>
        </div>
    </div>
</div>"
            );

            PublishHelper.SimplifyBackgroundImages(dom);

            var assertThatDom = AssertThatXmlIn.Element(dom.DocumentElement);
            // I think this is sufficient verification that the background canvas element structure was removed.
            assertThatDom.HasNoMatchForXpath("//div[@class='bloom-backgroundImage']");
            // verifies that the img was moved to the right place and (at least several) attributes were kept.
            assertThatDom.HasSpecifiedNumberOfMatchesForXpath(
                "//div[@class='bloom-canvas']/img[@data-book='coverImage' and @src=\"Duck-billed_platypus.jpg\" and @data-creator=\"Charles J Sharp\"]",
                1
            );
            // should not mess with the image description at all.
            assertThatDom.HasSpecifiedNumberOfMatchesForXpath(
                "//div[@class='bloom-canvas']/div[@class='bloom-translationGroup bloom-imageDescription bloom-trailingElement']",
                1
            );
        }

        [Test]
        public void SimplifyBackgroundImages_CreatesImgAndRemovesBackgroundCE_KeepsOtherCEs()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                @"
<div class=""bloom-page"">
    <div class=""bloom-canvas bloom-has-canvas-element"" data-bubble=""{`version`:`1.0`}"">
        <div class=""bloom-canvas-element bloom-backgroundImage"">
            <div class=""bloom-imageContainer"">
                <img data-book=""coverImage"" src=""Duck-billed_platypus.jpg"" data-copyright=""Charles J Sharp""
                    data-creator=""Charles J Sharp"" data-license=""cc-by"" alt=""This image, Duck-billed_platypus.jpg, is missing or was loading too slowly.""/>
            </div>
        </div>
        <div class=""bloom-canvas-element"">
             <div class=""bloom-imageContainer"">
                 <img data-book=""coverImage"" src=""Duck-billed_platypus.jpg"" data-copyright=""Charles J Sharp""
                     data-creator=""Charles J Sharp"" data-license=""cc-by"" alt=""This image, Duck-billed_platypus.jpg, is missing or was loading too slowly.""/>
             </div>
        </div>
    </div>
</div>"
            );

            PublishHelper.SimplifyBackgroundImages(dom);

            var assertThatDom = AssertThatXmlIn.Element(dom.DocumentElement);
            assertThatDom.HasNoMatchForXpath("//div[@class='bloom-backgroundImage']");
            // verifies that the img was created in the right place (or moved) and (at least several) attributes were kept.
            // Also that we did not remove bloom-has-canvas-element
            assertThatDom.HasSpecifiedNumberOfMatchesForXpath(
                "//div[@class='bloom-canvas bloom-has-canvas-element']/img[@data-book='coverImage' and @src=\"Duck-billed_platypus.jpg\" and @data-creator=\"Charles J Sharp\"]",
                1
            );
            assertThatDom.HasSpecifiedNumberOfMatchesForXpath(
                "//div[@class='bloom-canvas bloom-has-canvas-element']/div[contains(@class, 'bloom-canvas-element')]",
                1
            );
        }

        /// <summary>
        /// A font that cannot be embedded is swapped for the default everywhere it is named, and
        /// that has to include an element's own style attribute. ePUB export writes the font for
        /// language-independent text there (BL-16624), and until this was added that one
        /// declaration escaped the substitution and named a font the book never packaged.
        /// </summary>
        [Test]
        public void FixXmlDomReferencesForBadFonts_ReplacesBadFontInInlineStyle()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                @"<html><head>
    <style type='text/css' title='userModifiedStyles'>.Equation-style[lang=""*""] { font-family: NotLicensed !important; }</style>
</head><body>
    <div id='equation' style=""text-align: center; font-family: 'NotLicensed'"">1 + 1</div>
    <div id='quoteless' style=""font-family: NotLicensed"">2 + 2</div>
    <div id='innocent' style=""font-family: 'PerfectlyFine'"">3 + 3</div>
    <div id='nofont' style=""text-align: center"">4 + 4</div>
</body></html>"
            );
            // Sanity check the starting state, so this cannot pass without the substitution
            // actually happening.
            Assert.That(
                dom.SelectSingleNode("//div[@id='equation']").GetAttribute("style"),
                Does.Contain("NotLicensed"),
                "precondition: the equation should start out naming the bad font"
            );

            var fixedSomething = PublishHelper.FixXmlDomReferencesForBadFonts(
                dom,
                "Andika",
                new HashSet<string> { "NotLicensed" }
            );

            Assert.That(fixedSomething, Is.True);
            // The bad font is gone from the inline styles, quoted or not, and the rest of each
            // style attribute survives.
            Assert.That(
                dom.SelectSingleNode("//div[@id='equation']").GetAttribute("style"),
                Is.EqualTo("text-align: center; font-family: 'Andika'")
            );
            Assert.That(
                dom.SelectSingleNode("//div[@id='quoteless']").GetAttribute("style"),
                Is.EqualTo("font-family: 'Andika'")
            );
            // Elements naming a different font, or no font, are left exactly as they were.
            Assert.That(
                dom.SelectSingleNode("//div[@id='innocent']").GetAttribute("style"),
                Is.EqualTo("font-family: 'PerfectlyFine'")
            );
            Assert.That(
                dom.SelectSingleNode("//div[@id='nofont']").GetAttribute("style"),
                Is.EqualTo("text-align: center")
            );
            // The userModifiedStyles element is still handled too.
            Assert.That(
                dom.SelectSingleNode("//style").InnerXml,
                Does.Contain("font-family: Andika !important;")
            );
        }

        /// <summary>
        /// A bare font name has to match only a whole name. Bloom ships families whose names are
        /// prefixes of others -- "Andika" and "Andika New Basic" -- so matching without an end
        /// boundary would rewrite part of the longer name and leave nonsense like
        /// `font-family: 'Andika' New Basic`. Found by Devin on PR #8122.
        /// </summary>
        [Test]
        public void FixXmlDomReferencesForBadFonts_BadNameIsPrefixOfAnother_LeavesTheLongerNameAlone()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                @"<html><head></head><body>
    <div id='longer' style=""font-family: Andika New Basic"">1 + 1</div>
    <div id='exact' style=""font-family: Andika"">2 + 2</div>
    <div id='heads-a-list' style=""font-family: Andika, serif"">3 + 3</div>
</body></html>"
            );

            var fixedSomething = PublishHelper.FixXmlDomReferencesForBadFonts(
                dom,
                "Substitute",
                new HashSet<string> { "Andika" }
            );

            Assert.That(fixedSomething, Is.True);
            // The whole point: a different family that merely starts with the bad name is untouched.
            Assert.That(
                dom.SelectSingleNode("//div[@id='longer']").GetAttribute("style"),
                Is.EqualTo("font-family: Andika New Basic"),
                "a longer family name that starts with the bad one must not be rewritten"
            );
            Assert.That(
                dom.SelectSingleNode("//div[@id='exact']").GetAttribute("style"),
                Is.EqualTo("font-family: 'Substitute'")
            );
            // A bad font at the head of a fallback list is still replaced, and the list survives.
            Assert.That(
                dom.SelectSingleNode("//div[@id='heads-a-list']").GetAttribute("style"),
                Is.EqualTo("font-family: 'Substitute', serif")
            );
        }

        /// <summary>
        /// With no bad font present there is nothing to do, and in particular we must not report
        /// having changed something -- callers save the file only when we say we did.
        /// </summary>
        [Test]
        public void FixXmlDomReferencesForBadFonts_NoBadFontPresent_ReportsNoChange()
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                @"<html><head></head><body>
    <div id='equation' style=""font-family: 'PerfectlyFine'"">1 + 1</div>
</body></html>"
            );

            var fixedSomething = PublishHelper.FixXmlDomReferencesForBadFonts(
                dom,
                "Andika",
                new HashSet<string> { "NotLicensed" }
            );

            Assert.That(fixedSomething, Is.False);
            Assert.That(
                dom.SelectSingleNode("//div[@id='equation']").GetAttribute("style"),
                Is.EqualTo("font-family: 'PerfectlyFine'")
            );
        }
    }
}
