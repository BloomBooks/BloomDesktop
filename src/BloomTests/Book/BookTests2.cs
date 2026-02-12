using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.SafeXml;
using NUnit.Framework;

namespace BloomTests.Book
{
    public class BookTests2 : BookTests
    {
        [Test]
        public void GetCoverImagePathAndElt_HasTwoCoverImages_GetsRightOne()
        {
            SetDom(
                @"
<div id='bloomDataDiv'>
	<div data-book='coverImage' lang='*' src='IMG_1413.jpg' id='data'></div>
</div>
<div class='bloom-page bloom-frontMatter outsideFrontCover'>
	<div class='marginBox'>
        <div class='bloom-canvas' data-book='coverImage' id='normal' style='background-image:url(IMG_1413.jpg)'>
            <div class=""bloom-canvas-element bloom-backgroundImage"" >
                <div class=""bloom-imageContainer"">
                    <img src=""IMG_1413.jpg""/>
                </div>
            </div>
		</div>
	</div>
    <div class='marginBox bloom-customMarginBox'>
        <div class='bloom-canvas' data-book='coverImage' id='custom' style='background-image:url(IMG_1413.jpg)'>
            <div class=""bloom-canvas-element"" >
                <div class=""bloom-imageContainer"">
                    <img src=""IMG_1413.jpg""/>
                </div>
            </div>
	    </div>
    </div>
</div>"
            );
            File.WriteAllText(Path.Combine(_tempFolder.Path, "IMG_1413.jpg"), "dummy");
            var book = CreateBook();
            var coverImgPath = book.GetCoverImagePathAndElt(out SafeXmlElement coverImgElt);
            Assert.That(Path.GetFileName(coverImgPath), Is.EqualTo("IMG_1413.jpg"));
            Assert.That(coverImgElt.GetAttribute("id"), Is.EqualTo("normal"));
        }

        [Test]
        public void GetCoverImagePathAndElt_HasTwoImgCoverImages_IgnoresDataDivAndSwitchesToCustomWhenPageIsCustomCover()
        {
            SetDom(
                @"
<div id='bloomDataDiv'>
	<div data-book='coverImage' lang='*' src='IMG_1413.jpg' id='data'></div>
    <div data-book='customCover'>
        <div class='bloom-canvas'>
            <div class=""bloom-canvas-element bloom-backgroundImage"">
                <div class=""bloom-imageContainer"">
                    <img data-book=""coverImage"" src=""IMG_from_dataDiv.jpg"" id='fromDataDivCustomCover'/>
                </div>
            </div>
        </div>
    </div>
</div>
<div class='bloom-page bloom-frontMatter outsideFrontCover'>
	<div class='marginBox'>
        <div class='bloom-canvas'>
            <div class=""bloom-canvas-element bloom-backgroundImage"" >
                <div class=""bloom-imageContainer"">
                    <img src=""IMG_notCoverInNormal.jpg"" id='normal-noncover-earlier'/>
                    <img data-book=""coverImage"" src=""IMG_1413.jpg"" id='normal'/>
                </div>
            </div>
		</div>
	</div>
    <div class='marginBox bloom-customMarginBox'>
        <div class='bloom-canvas'>
            <div class=""bloom-canvas-element"" >
                <div class=""bloom-imageContainer"">
                    <img src=""IMG_notCoverInCustom.jpg"" id='custom-noncover-earlier'/>
                    <img data-book=""coverImage"" src=""IMG_1413.jpg"" id='custom'/>
                </div>
            </div>
	    </div>
    </div>
</div>"
            );
            File.WriteAllText(Path.Combine(_tempFolder.Path, "IMG_1413.jpg"), "dummy");
            File.WriteAllText(Path.Combine(_tempFolder.Path, "IMG_from_dataDiv.jpg"), "dummy");
            File.WriteAllText(Path.Combine(_tempFolder.Path, "IMG_notCoverInNormal.jpg"), "dummy");
            File.WriteAllText(Path.Combine(_tempFolder.Path, "IMG_notCoverInCustom.jpg"), "dummy");
            var book = CreateBook();
            var coverImgPath = book.GetCoverImagePathAndElt(out SafeXmlElement coverImgElt);
            Assert.That(Path.GetFileName(coverImgPath), Is.EqualTo("IMG_1413.jpg"));
            Assert.That(coverImgElt.GetAttribute("id"), Is.EqualTo("normal"));

            var outsideFrontCover =
                book.RawDom.SelectSingleNode(
                    "//div[contains(concat(' ', normalize-space(@class), ' '), ' outsideFrontCover ')]"
                ) as SafeXmlElement;
            outsideFrontCover.SetAttribute(
                "class",
                outsideFrontCover.GetAttribute("class") + " bloom-custom-cover"
            );

            coverImgPath = book.GetCoverImagePathAndElt(out coverImgElt);
            Assert.That(Path.GetFileName(coverImgPath), Is.EqualTo("IMG_1413.jpg"));
            Assert.That(coverImgElt.GetAttribute("id"), Is.EqualTo("custom"));

            coverImgElt.RemoveAttribute("data-book");
            coverImgPath = book.GetCoverImagePathAndElt(out coverImgElt);
            Assert.That(Path.GetFileName(coverImgPath), Is.EqualTo("IMG_notCoverInCustom.jpg"));
            Assert.That(coverImgElt.GetAttribute("id"), Is.EqualTo("custom-noncover-earlier"));
        }

        [Test]
        public void GetCoverImagePathAndElt_HasNoCoverImage_ReturnsNulls()
        {
            SetDom(
                @"
<div id='bloomDataDiv'>
	<div data-book='someOtherData' lang='*'>value</div>
</div>
<div class='bloom-page'>
	<div class='marginBox'>
        <div class='bloom-canvas'>
            <div class=""bloom-canvas-element"" >
                <div class=""bloom-imageContainer"">
                    <img src=""IMG_1413.jpg"" id='not-cover'/>
                </div>
            </div>
		</div>
	</div>
</div>"
            );
            var book = CreateBook();
            var coverImgPath = book.GetCoverImagePathAndElt(out SafeXmlElement coverImgElt);
            Assert.That(coverImgPath, Is.Null);
            Assert.That(coverImgElt, Is.Null);
        }
    }
}
