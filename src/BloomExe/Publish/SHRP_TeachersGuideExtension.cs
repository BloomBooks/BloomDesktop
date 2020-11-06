using System.Xml;
using Bloom.Book;
using SIL.Xml;

namespace Bloom.Publish
{

// ReSharper disable once InconsistentNaming
	public class SHRP_TeachersGuideExtension
	{
		public static bool ExtensionIsApplicable(Book.Book book)
		{
		   //for now we're not doing real extension dlls, just kind of faking it. So we will limit this load
			//to books we know go with this currently "built-in" "extension" for SIL LEAD's SHRP Project.
			return book.CollectionSettings.CollectionName.Contains("Guide") || book.CollectionSettings.CollectionName.Contains("TG");
		}

		public static void UpdateBook(HtmlDom dom, string language1Iso639Code)
		{
			int page = 0;
			foreach (XmlElement pageDiv in dom.SafeSelectNodes("/html/body//div[contains(@class,'bloom-page')]"))
			{
				// Since this will be run on any collection with "Guide" in the name, we must assume that
				// some occurences won't actually be SHRP books.
				var termNode = pageDiv.SelectSingleNode("//div[contains(@data-book,'term')]");
				if (termNode == null)
					continue;
				var term = termNode.InnerText.Trim();
				XmlNode weekDataNode = pageDiv.SelectSingleNode("//div[contains(@data-book,'week')]");
				if(weekDataNode==null)
					continue; // term intro books don't have weeks

				var week = weekDataNode.InnerText.Trim();
				// TODO: need a better way to identify thumbnails, like a class that is always there, lest  we replace some other img that we don't want to replace
				foreach (XmlElement thumbnailContainer in pageDiv.SafeSelectNodes(".//img"))
				{
					++page;
					thumbnailContainer.SetAttribute("src", language1Iso639Code + "-t" + term + "-w" + week + "-p" + page + ".png");
				}
			}
		}
	}
}
