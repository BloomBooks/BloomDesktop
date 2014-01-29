using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.ComponentModel.Composition;
using System.Security.Cryptography;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using System.Linq;
using Palaso.Xml;

namespace Bloom.Publish
{
    
// ReSharper disable once InconsistentNaming
    public class SHRP_TeachersGuideExtension
    {
        public static bool ExtensionIsApplicable(string bookLineage)
        {
           //for now we're not doing real extension dlls, just kind of faking it. So we will limit this load
            //to books we know go with this currently "built-in" "extension" for SIL LEAD's SHRP Project.
            const string kSHRPTeachersBook = "DDF29517-F934-4D15-8BF0-A25ABBBF45DD";

            var ancestors = bookLineage.Split(new[] {','});
            return ancestors.Contains(kSHRPTeachersBook);
        }


        //TODO: make this be a real extension
        public static void UpdateBook(HtmlDom dom, string language1Iso639Code)
        {
            int day = 0;
            foreach (XmlElement pageDiv in dom.SafeSelectNodes("/html/body/div[contains(@class,'bloom-page')]"))
            {
                var term = pageDiv.SelectSingleNode("//div[contains(@data-book,'term')]").InnerText.Trim();
                var week = pageDiv.SelectSingleNode("//div[contains(@data-book,'week')]").InnerText.Trim();

                var thumbnailHolders = pageDiv.SafeSelectNodes(".//img");
                if (thumbnailHolders.Count == 2)
                {
                    ++day;
                    ((XmlElement)thumbnailHolders[0]).SetAttribute("src", language1Iso639Code+"-t"+term + "-w" + week + "-d" + day + ".png");
                    ++day;
                    ((XmlElement)thumbnailHolders[1]).SetAttribute("src", language1Iso639Code + "-t" + term + "-w" + week + "-d" + day + ".png");
                }
                //day1Thumbnail day2Thumbnail day4Thumbnail
                //unfortunately Day3 went out with  an img container just copied from day1, with erroneous "day1Thumbnail" class

            }
        }
    }
}
