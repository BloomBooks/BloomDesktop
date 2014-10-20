using System.Xml;
using Bloom.Book;
using Palaso.Xml;

namespace Bloom.Publish
{

// ReSharper disable once InconsistentNaming
	public class SHRP_TeachersGuideExtension
	{
		public static bool ExtensionIsApplicable(Book.Book book)
		{
		   //for now we're not doing real extension dlls, just kind of faking it. So we will limit this load
			//to books we know go with this currently "built-in" "extension" for SIL LEAD's SHRP Project.
			return book.Title.Contains("Guide");
		}

		public static void UpdateBook(HtmlDom dom, string language1Iso639Code)
		{
			int page = 0;
			foreach (XmlElement pageDiv in dom.SafeSelectNodes("/html/body/div[contains(@class,'bloom-page')]"))
			{
				var term = pageDiv.SelectSingleNode("//div[contains(@data-book,'term')]").InnerText.Trim();
				XmlNode weekDataNode = pageDiv.SelectSingleNode("//div[contains(@data-book,'week')]");
				if(weekDataNode==null)
					continue; // term intro books don't have weeks

				var week = weekDataNode.InnerText.Trim();

				var thumbnailHolders = pageDiv.SafeSelectNodes(".//img");
				if (thumbnailHolders.Count == 2)
				{
					++page;
					((XmlElement)thumbnailHolders[0]).SetAttribute("src", language1Iso639Code+"-t"+term + "-w" + week + "-p" + page + ".png");
					++page;
					((XmlElement)thumbnailHolders[1]).SetAttribute("src", language1Iso639Code + "-t" + term + "-w" + week + "-p" + page + ".png");
				}
				//day1Thumbnail day2Thumbnail day4Thumbnail
				//unfortunately Day3 went out with  an img container just copied from day1, with erroneous "day1Thumbnail" class

			}
		}
	}
}
