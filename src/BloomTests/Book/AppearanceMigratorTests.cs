using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Book;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace BloomTests.Book
{
    public class AppearanceMigratorTests
    {
        // This must not be modified even slightly, or the checksum will not match.
        public static string cssThatTriggersPurpleRoundedTheme =
            @" /* Below you find the settings for this custom Bloom design. This book only has a custom design for ""A5Portrait"".   */


/* The below statements control margins on the Outside Cover. */
.outsideFrontCover.A5Portrait .marginBox {
  height: 190mm; 
  width: 124mm;
  top: 10mm;
  left: 12mm !important;
}

/* The below statements control the size and color of the marginbox. The marginbox holds the text and picture of that page. The top and left numbers determine the position of the margin box on the page.   */
:not(.bloom-interactive-page).A5Portrait.numberedPage.side-left .marginBox {
  border: 1.5mm solid;
  padding: 1.5mm;
  border-radius: 25px;
  border-color: #282828;
  height: 191mm;
  width: 127.5mm;
  top: 7mm;
  left: 7mm !important;
}

:not(.bloom-interactive-page).A5Portrait.numberedPage.side-left .marginBox img{

  border-radius: 15px 15px 0px 0px;
}

:not(.bloom-interactive-page).A5Portrait.numberedPage.side-right .marginBox img{

  border-radius: 15px 15px 0px 0px;
}

:not(.bloom-interactive-page).A5Portrait.numberedPage.side-right .marginBox {
  border: 1.5mm solid;
  padding: 1.5mm;
  border-radius: 25px;
  border-color: #282828;
  height: 191mm;
  width: 127.5mm;
  top: 7mm;
  left: 8mm !important;
}

  
/* The following two statements control the position of the page number   */ 
.A5Portrait.numberedPage.side-left::after {
  left: calc(100% / 2 - 65.5mm);
}

.A5Portrait.numberedPage.side-right::after {
  left: calc(100% / 2 + 54mm);
}

/* The section below controls the pagenumber and the white circle around it.  */ 
.A5Portrait.numberedPage::after {
  bottom: 2.5mm !important;
  font-size: 11pt;
  color: black;
  
}
/* The section below controls the pagenumber and the white circle around it.  */ 
.A5Portrait.numberedPage.side-left::after {
  bottom: 190.5mm !important;
  font-size: 13pt;
  z-index: 1000;
  color: black;
  border: 0.5mm solid;
  border-radius: 50%;
  border-color: #ffffff;
  background: #ffffff;
  padding: 1.75mm;
  width: 7mm;
  text-align: center !important;
  margin: auto;
}  
.A5Portrait.numberedPage.side-right::after {  
  bottom: 190.5mm !important;
  font-size: 13pt;
  z-index: 1000;
  color: black;
  border: 0.5mm solid;
  border-radius: 50%;
  border-color: #ffffff;
  background: #ffffff;
  padding: 1.75mm;
  width: 7mm;
  text-align: center !important;
  margin: auto;
}


/* End of statements controlling layout of 'A5Portrait'. */
";

        [Test]
        public void GetJsonThatSubstitutesForCustomCSS_FindsRightTheme()
        {
            var result = AppearanceMigrator.Instance.GetAppearanceThatSubstitutesForCustomCSS(
                cssThatTriggersPurpleRoundedTheme
            );
            Assert.That(
                result,
                Is.EqualTo(
                    Path.Combine(
                        AppearanceMigrator.GetFolderContainingAppearanceMigrations(),
                        "purpleRounded",
                        "appearance.json"
                    )
                )
            );
        }

        [Test]
        public void GetJsonThatSubstitutesForCustomCSS_FindsNoTheme()
        {
            var result = AppearanceMigrator.Instance.GetAppearanceThatSubstitutesForCustomCSS(
                "some nonsense"
            );
            Assert.That(result, Is.Null);
        }
    }
}
