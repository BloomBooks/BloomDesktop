using Bloom.Collection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Bloom.Publish.Android
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

		public BulkBloomPubPublishSettings()
		{
		}

		/// <summary>
        /// Constructs an object based on an XElement parsed from XML
        /// </summary>
        /// <param name="xml">The XElement corresponding to the Collection. One of its elements should be named "BulkPublishBloomPubSettings" or else an exception will be thrown </param>
        /// <returns></returns>
		public BulkBloomPubPublishSettings(XElement xml)
		{
			var settingsElement = xml.Elements().Where(e => e.Name == kXmlAttrName).FirstOrDefault();
			if (settingsElement == null)
			{
				throw new KeyNotFoundException($"Key \"{kXmlAttrName}\" was not found");
			}

			this.makeBookshelfFile = CollectionSettings.ReadBoolean(settingsElement, "MakeBookshelfFile", true);
			this.makeBloomBundle = CollectionSettings.ReadBoolean(settingsElement, "MakeBloomBundle", true);
			this.bookshelfColor = CollectionSettings.ReadString(settingsElement, "BookshelfColor", "lightblue");
			this.distributionTag = CollectionSettings.ReadString(settingsElement, "DistributionTag", "");
			this.bookshelfLabel = CollectionSettings.ReadString(settingsElement, "BookshelfLabel", "");
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
