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
