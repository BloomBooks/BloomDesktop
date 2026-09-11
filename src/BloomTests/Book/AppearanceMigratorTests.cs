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
        public static string cssThatTriggersEbookEdgeToEdgeTheme =
            @"/*  Some books may need control over aspects of layout that cannot yet be adjusted
    from the Bloom interface. In those cases, Bloom provides this ""under the hood"" method
    of creating style rules using the underlying ""Cascading Stylesheets"" system.
    These rules are then applied to all books in this collection.  EDIT THIS FILE ONLY
    IN THE COLLECTION FOLDER:  changes made to a copy found in the book folder will be
    lost the next time the book is edited with Bloom!

 Note: you can also add a file named ""customBookStyles.css"" in the book folder,
    to limit the effects of the rules to just that one book.

    You can learn about CSS from hundreds of books, or online. However chances are, if
    you need this customization, you will need an expert to create a version of this file
    for you, or give you rules that you can paste in below this line. */

.position-bottom > .split-pane-component-inner,
.position-bottom
  > :not(.split-pane-component-inner)
  > .split-pane-component-inner,
.position-bottom
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > .split-pane-component-inner,
.position-bottom
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > .split-pane-component-inner,
.position-bottom
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > .split-pane-component-inner,
.position-bottom
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > .split-pane-component-inner {
  padding-top: 1mm;
}

.position-right > .split-pane-component-inner,
.position-right
  > :not(.split-pane-component-inner)
  > .split-pane-component-inner,
.position-right
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > .split-pane-component-inner,
.position-right
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > .split-pane-component-inner,
.position-right
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > .split-pane-component-inner,
.position-right
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > :not(.split-pane-component-inner)
  > .split-pane-component-inner {
  padding-left: 1mm;
}

/* This first line of code hides the page numbers. */
 .numberedPage::after {display:none;} 

/* The line below hides the credits row on the front cover. */ 
 .creditsRow {display:none}  

/* Below you find code that controls layout for pages in Device16x9Landscape layout */

 .Device16x9Landscape .marginBox img {
  max-width: 177.777778mm;
 }


.Device16x9Landscape.fullScreen .bloom-imageContainer.bloom-backgroundImage {
  /* Get past ios ""background-size:cover"" bug. See BL-7458 */
  width: 177.777778mm;
}

:not(.bloom-interactive-page).numberedPage.Device16x9Landscape .marginBox {
  height: 100mm;
  width: 177.777778mm;
  top: 0mm;
  left:0mm !important;
}

/* Below you find code that controls layout for pages in Device16x9Portrait layout */

 .Device16x9Portrait .marginBox img {
  max-width: 100mm;
 }

.Device16x9Portrait.fullScreen .bloom-imageContainer.bloom-backgroundImage {
  /* Get past ios ""background-size:cover"" bug. See BL-7458 */
  width: 100mm;
}

:not(.bloom-interactive-page).numberedPage.Device16x9Portrait .marginBox {
  height: 177.777778mm;
  width: 100mm;
  top: 0mm;
  left:0mm !important;
}
";

        [Test]
        public void GetJsonThatSubstitutesForCustomCSS_FindsRightTheme()
        {
            var result = AppearanceMigrator.Instance.GetAppearanceThatSubstitutesForCustomCSS(
                cssThatTriggersEbookEdgeToEdgeTheme
            );
            Assert.That(
                result,
                Is.EqualTo(
                    Path.Combine(
                        AppearanceMigrator.GetFolderContainingAppearanceMigrations(),
                        "efl-ebook-1",
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
