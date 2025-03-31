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
                    data-creator=""Charles J Sharp"" data-license=""cc-by"" alt=""This picture, Duck-billed_platypus.jpg, is missing or was loading too slowly.""/>
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
                    data-creator=""Charles J Sharp"" data-license=""cc-by"" alt=""This picture, Duck-billed_platypus.jpg, is missing or was loading too slowly.""/>
            </div>
        </div>
        <div class=""bloom-canvas-element"">
             <div class=""bloom-imageContainer"">
                 <img data-book=""coverImage"" src=""Duck-billed_platypus.jpg"" data-copyright=""Charles J Sharp""
                     data-creator=""Charles J Sharp"" data-license=""cc-by"" alt=""This picture, Duck-billed_platypus.jpg, is missing or was loading too slowly.""/>
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
    }
}
