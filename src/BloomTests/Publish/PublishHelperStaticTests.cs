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
        public void SimplifyBackgroundImages_CreatesImgAndRemovesBackgroundCE_RemovesHasCEClass()
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
    </div>
</div>"
            );

            PublishHelper.SimplifyBackgroundImages(dom);

            var assertThatDom = AssertThatXmlIn.Element(dom.DocumentElement);
            assertThatDom.HasNoMatchForXpath("//div[@class='bloom-backgroundImage']");
            // verifies that the img was created in the right place (or moved) and (at least several) attributes were kept.
            // Also that we removed bloom-has-canvas-element
            assertThatDom.HasSpecifiedNumberOfMatchesForXpath(
                "//div[@class='bloom-canvas']/img[@data-book='coverImage' and @src=\"Duck-billed_platypus.jpg\" and @data-creator=\"Charles J Sharp\"]",
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
