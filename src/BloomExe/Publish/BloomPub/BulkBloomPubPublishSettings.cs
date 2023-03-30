using Bloom.Collection;
using System.Linq;
using System.Xml.Linq;

namespace Bloom.Publish.BloomPub
{
	// NB: this must match IBulkSaveBloomPubsParams on the typescript side
	public class BulkBloomPubPublishSettings
	{
		// Public fields
		public bool makeBookshelfFile;
		public bool makeBloomBundle;
		public string bookshelfColor;
		// distributionTag goes into bloomPUBs created in bulk, and from there to analytics events
		public string distributionTag;
		// The server doesn't actually know what the label is, just the urlKey. So when the client has to look it up from contentful,
		// and when it tells us to go ahead and do the publishing, it includes this so that we can use it to make the bookshelf file.
		public string bookshelfLabel;


		private const string kXmlAttrName = "BulkPublishBloomPubSettings";

		/// <summary>
        /// Instantiates an instance based on an XElement parsed from XML
        /// </summary>
        /// <param name="xml">The XElement corresponding to the Collection. One of its elements should be named "BulkPublishBloomPubSettings" in order for it to be loaded</param>
        /// <returns>Parses the XML elements and returns an instantiated BulkBloomPubPublishSettings object.
		/// Returns null if element not found.
		/// </returns>
		public static BulkBloomPubPublishSettings LoadFromXElement(XElement xml)
		{
			var settingsElement = xml.Elements().Where(e => e.Name == kXmlAttrName).FirstOrDefault();
			if (settingsElement == null)
			{
				return null;
			}

			var publishSettings = new BulkBloomPubPublishSettings();
			publishSettings.makeBookshelfFile = CollectionSettings.ReadBoolean(settingsElement, "MakeBookshelfFile", true);
			publishSettings.makeBloomBundle = CollectionSettings.ReadBoolean(settingsElement, "MakeBloomBundle", true);
			publishSettings.bookshelfColor = CollectionSettings.ReadString(settingsElement, "BookshelfColor", Palette.kBloomLightBlueHex);
			// patch a problem we introduced with the first version of the code. (BL-10573)
			if (publishSettings.bookshelfColor == "lightblue")
				publishSettings.bookshelfColor = Palette.kBloomLightBlueHex;
			publishSettings.distributionTag = CollectionSettings.ReadString(settingsElement, "DistributionTag", "");
			publishSettings.bookshelfLabel = CollectionSettings.ReadString(settingsElement, "BookshelfLabel", "");
			return publishSettings;
		}

		/// <summary>
        /// Takes the current object and converts it to an XElement under the name "BulkPublishBloomPubSettings"
        /// </summary>
		public XElement ToXElement()
		{
			var settings = new XElement(kXmlAttrName);
			settings.Add(new XElement("MakeBookshelfFile", makeBookshelfFile.ToString()));
			settings.Add(new XElement("MakeBloomBundle", makeBloomBundle.ToString()));
			settings.Add(new XElement("BookshelfColor", bookshelfColor));
			settings.Add(new XElement("DistributionTag", distributionTag));
			settings.Add(new XElement("BookshelfLabel", bookshelfLabel));

			return settings;
		}
	}
}
