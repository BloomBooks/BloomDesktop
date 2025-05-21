using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web;
using System.Xml;
using System.Xml.Linq;
using Bloom.Api;
using Bloom.Collection;
using Bloom.Edit;
using L10NSharp;
using Microsoft.CSharp.RuntimeBinder;
using SIL.Extensions;
using SIL.Linq;
using SIL.Reporting;
using SIL.Text;
using SIL.WritingSystems;
using SIL.IO;
using Bloom.SafeXml;
using Bloom.Publish;

namespace Bloom.Book
{
    /// <summary>
    /// This class manages the "data-*" elements of a bloom document.
    /// </summary>
    /// <remarks>
    /// At the beginning of the document, we have a special div for holding book-wide data.
    /// It may hosts all manner of data about the book, including copyright, what languages are currently visible, etc.Here's a sample of a simple one:
    /*<div id="bloomDataDiv">
              <div data-book="bookTitle" lang="en">Awito Builds a toilet</div>
              <div data-book="bookTitle" lang="tpi">Awito i wokim haus</div>
              <div data-book="coverImage" lang="*">tmpABDB.png</div>
              <div data-book="topic" lang="tpi">Health</div>
              <div data-book="contentLanguage1" lang="*">en</div>
              <div data-book="contentLanguage2" lang="*">tpi</div>
              <div data-book="copyright" lang="*">Copyright © 1994, National Department of Education</div>
              <div data-book="licenseImage" lang="*">license.png?1348557455942</div>
              <div data-book="licenseUrl" lang="en">http://creativecommons.org/licenses/by-nc-sa/3.0/</div>
              <div data-book="licenseDescription" lang="en">You may not use this work for commercial purposes. You may adapt or build upon this work, but you may distribute the resulting work only under the same or similar license to this one.You must attribute the work in the manner specified by the author.</div>
              <div data-book="originalAcknowledgments" lang="tpi">Book Development by:  Curriculum Development Division</div>
            </div>
            */
    /// After the bloomDataDiv, elements with "data-*" attributes can occur throughout a book, for example on the cover page:
    /*    <div class="bloom-page">
        <div class="bloom-translationGroup coverTitle">
          <div data-book="bookTitle" lang="en">Awito Builds a house</div>
          <div data-book="bookTitle" lang="tpi">Awito i wokim haus</div>
        </div>
    */
    /// This class must keep these in sync
    /// There is also a file meta.json which contains data that is also kept online to aid in searching for books. Some of this must also be kept
    /// in sync with data in the html, for example, metadata.volumeInfo.title should match (currently the English alternative of) the content of the
    /// bloomDataDiv bookTitle div.
    /// </remarks>
    public class BookData
    {
        /// <summary>
        /// This is the attribute name used in the data div for a rather complex data-model.
        /// Specifically, an xmatter page sets a specific page name as the value of this attribute.
        /// That value is used as a key to link the attributes stored in the data div with the attributes on the page itself.
        /// This allows us to keep the values persisted (since the xmatter is constantly being regenerated).
        ///
        /// E.g.
        /// In the data div, we have a div such as
        /// &lt;div data-xmatter-page="frontCover" data-backgroundaudio="SoundTrack0.mp3" data-backgroundaudiovolume="0.5717869999999999"&gt;&lt;/div&gt;
        ///
        /// The actual xmatter page will also have these same attributes (along with all the rest it has).
        /// &lt;div class="bloom-page cover coverColor bloom-frontMatter frontCover Device16x9Landscape layout-style-Default side-right"
        /// id="76ed5d5b-c178-4db1-8be1-4a2f63eccaa4"
        /// data-xmatter-page="frontCover" data-backgroundaudio="SoundTrack0.mp3" data-backgroundaudiovolume="0.5717869999999999"
        /// lang=""&gt;
        /// </summary>
        private const string kDataXmatterPage = "data-xmatter-page";

        private readonly HtmlDom _dom;
        private readonly Action<SafeXmlElement> _updateImgNode;
        internal readonly CollectionSettings CollectionSettings;
        private readonly DataSet _dataset;
        private SafeXmlElement _dataDiv;
        private Object thisLock = new Object();

        // At one point we used XmlString for these, but language tags cannot actually
        // contain any characters that need encoding, so it seems an unnecessary complication.
        private string _cachedLangTag1;
        private string _cachedLangTag2;
        private string _cachedLangTag3;
        private string _cachedSignLangTag;
        private string _cachedMetadataLangTag1;
        private bool _gotLangCache;

        //URLs are encoded in a certain way for the src attributes
        //If they have certain symbols (namely &), they need to be encoded differently
        //when saved as xml in the data-div. Ideally, we might use an abstraction over
        //string which know what encoding things are in. Or we could work hard to always
        //keep strings unencoded in this class and DataSet in then encode them as they
        //get written out to various places. But either of those is too disruptive at this
        //point so this is a simple solution. Ref BL-3235.
        public HashSet<string> KeysOfVariablesThatAreUrlEncoded = new HashSet<string>();

        /// <param name="dom">Set this parameter to, say, a page that the user just edited, to limit reading to it, so its values don't get overriden by previous pages.
        ///   Supply the whole dom if nothing has priority (which will mean the data-div will win, because it is first)</param>
        /// <param name="collectionSettings"> </param>
        /// <param name="updateImgNodeCallback">This is a callback so as not to introduce dependencies on ImageUpdater & the current folder path</param>
        public BookData(
            HtmlDom dom,
            CollectionSettings collectionSettings,
            Action<SafeXmlElement> updateImgNodeCallback
        )
        {
            _dom = dom;
            _updateImgNode = updateImgNodeCallback;
            CollectionSettings = collectionSettings;
            GetOrCreateDataDiv();
            _dataset = new DataSet();
            GatherDataItemsFromSettings(_dataset, CollectionSettings);
            GatherDataItemsFromXElement(_dataset, _dom.RawDom);
            MigrateData(_dataset);
        }

        /// <summary>
        /// The first (or only) language to show, usually the vernacular.
        /// </summary>
        public string Language1Tag
        {
            get
            {
                if (!_gotLangCache)
                {
                    if (_cachingLangData)
                    {
                        // recursive call from GatherDataItemsFromXElement; just use collection language
                        return CollectionSettings.Language1Tag;
                    }
                    CacheLangData();
                }

                return _cachedLangTag1;
            }
        }

        /// <summary>
        /// For bilingual or trilingual books, this is the second language to show,
        /// after Language1.
        /// </summary>
        public String Language2Tag
        {
            get
            {
                if (!_gotLangCache)
                    CacheLangData();
                return _cachedLangTag2;
            }
        }

        /// <summary>
        /// For sign language books, this is the tag of the sign language.
        /// </summary>
        public string SignLanguageTag
        {
            get
            {
                if (!_gotLangCache)
                    CacheLangData();
                return _cachedSignLangTag;
            }
        }

        /// <summary>
        /// This is the language we use to show metadata, which currently includes most
        /// of the fields in XMatter, including the second title. In phase 2 of our language
        /// cleanup, we may separate out more categories.
        /// Currently this is always the the collection's L2, though we may allow configuring
        /// it eventually.
        /// It should never be null, but might be the same as Language1 (or 2 or 3).
        /// </summary>
        public String MetadataLanguage1Tag
        {
            get
            {
                if (!_gotLangCache)
                    CacheLangData();
                return _cachedMetadataLangTag1;
            }
        }

        /// <summary>
        /// We tentatively anticipate supporting a second metadata language, typically useful for
        /// something like a regional language where ML1 is a national one, though it's equally
        /// possible that ML1 might be a regional language while ML2 could be the language of
        /// an international organization working on the project.
        /// Somewhere in phase 2 or later, this might become configurable for a book
        /// independent of the collection; or we may decide that metadata languages are fixed
        /// for a collection.
        /// For now, it serves as the definition of the data-default-languages code N2
        /// (a legacy name kept for backwards compatibility, short for National Language 2).
        /// This is currently only used where the user configures a text box to show N2 by
        /// choosing the last radio button.
        /// </summary>
        public string MetadataLanguage2Tag => CollectionSettings.Language3Tag;

        private bool _cachingLangData = false;

        /// <summary>
        /// Cache values for LanguageNTag and MetadataLang1Tag
        /// It is safe to cache this because we reload everything when changing languages
        /// in the collection. When we change them in the active language menu, we update the cache.
        /// It is important because these properties are heavily used, especially when saving pages.
        /// </summary>
        private void CacheLangData()
        {
            if (_cachingLangData)
            {
                throw new ApplicationException("recursive call to CachecLangData");
            }
            _cachingLangData = true;
            try
            {
                GatherDataItemsFromXElement(_dataset, _dom.RawDom);
                _cachedLangTag1 = GetVariableOrNull("contentLanguage1", "*").Unencoded;
                if (
                    string.IsNullOrEmpty(_cachedLangTag1)
                    || GetWritingSystemOrNull(_cachedLangTag1) == null
                )
                {
                    // If contentLanguage1 isn't in the element (typically in unit tests), just use Language1 from CollectionSettings.
                    // Likewise, if the stored WS isn't a current collection language (typically because we just modified collection settings),
                    // use the first collection language.
                    _cachedLangTag1 = CollectionSettings.Language1.Tag;
                }

                _cachedSignLangTag = CollectionSettings.SignLanguageTag;

                _cachedLangTag2 = GetVariableOrNull("contentLanguage2", "*").Unencoded;
                _cachedLangTag3 = GetVariableOrNull("contentLanguage3", "*").Unencoded;
                // If either of these is a WS no longer in the collection, drop it. Also drop duplicates
                // (which should only happen as a result of language 1 changing because of collection settings changes).
                if (
                    GetWritingSystemOrNull(_cachedLangTag3) == null
                    || _cachedLangTag3 == _cachedLangTag2
                    || _cachedLangTag3 == _cachedLangTag1
                )
                    _cachedLangTag3 = null;
                if (
                    GetWritingSystemOrNull(_cachedLangTag2) == null
                    || _cachedLangTag2 == _cachedLangTag1
                )
                {
                    // If we still have an L3, promote it to L2.
                    _cachedLangTag2 = _cachedLangTag3;
                    _cachedLangTag3 = null;
                }

                _gotLangCache = true;
            }
            finally
            {
                _cachingLangData = false;
            }

            // To avoid triggering the recursive call warning above, these need to be done outside
            // the protected block.
            UpdateLang1Derivatives(_cachedLangTag1);
            UpdateLang2Derivatives(_cachedLangTag2);
            UpdateLang3Derivatives(_cachedLangTag3);
            UpdateML1Derivatives();
        }

        internal void UpdateCache()
        {
            CacheLangData();
        }

        private void UpdateLang1Derivatives(string newLang1)
        {
            _cachedLangTag1 = newLang1;
            if (newLang1 == null)
                return; // This should only happen in the earliest stages of constructing a BookData
            // I'm not sure why this is needed nor, since it is using *, why we don't use UpdateGenericLanguageString
            // so it ONLY has that alternative. I cannot find anywhere this value is used. I'm putting this in
            // because GatherDataItemsFromSettings fills it in, and if it IS used, I want to keep it consistent
            // with the new notion of what language 1 means.
            // Taking out for now to see what breaks.
            //_dataset.AddLanguageString("nameOfLanguage",
            //	XmlString.FromUnencoded(Language1.Name), "*", true);
            _dataset.UpdateGenericLanguageString(
                "iso639Code",
                XmlString.FromUnencoded(newLang1),
                true
            );
        }

        private void UpdateLang3Derivatives(string newLang3)
        {
            _cachedLangTag3 = newLang3;
            // I'm not sure why this is needed nor, since it is using *, why we don't use UpdateGenericLanguageString
            // so it ONLY has that alternative. I cannot find anywhere this value is used. I'm putting this in
            // because GatherDataItemsFromSettings fills it in, and if it IS used, I want to keep it consistent
            // with the new notion of what language 3 means.
            // For the same reason, I'm not sure whether it should be set to empty or removed if there is no L3.
            // Taking out for now to see what breaks.
            //_dataset.AddLanguageString("nameOfNationalLanguage2",
            //	XmlString.FromUnencoded(Language3?.Name), "*", true);
        }

        private void UpdateLang2Derivatives(string newLang2)
        {
            _cachedLangTag2 = newLang2;
            // There actually are no derivatives of L2. Derivatives of what used to be L2
            // are now derived from ML1 (alias N2).

            // This is not now necessary in normal operation, as ML1 does not currently
            // depend on L2. However, we have unit tests that mess with collection languages
            // without doing the full reloading that happens in normal operation.
            // It helps keep these working if changing L2 also updates the current ML1.
            UpdateML1Derivatives();
        }

        private void UpdateML1Derivatives()
        {
            _cachedMetadataLangTag1 = CollectionSettings.Language2Tag;
            // We thought about using this, but decided in the end that until we give the user
            // full control over ML1 it's better to stick with the old behavior where ML1 is simply
            // the second collection language.
            //_cachedMetadataLangTag1 = string.IsNullOrEmpty(_cachedLangTag2)
            //	? CollectionSettings.Language2Tag
            //	: _cachedLangTag2;

            // I'm not sure why this is needed nor, since it is using *, why we don't use UpdateGenericLanguageString
            // so it ONLY has that alternative. I cannot find anywhere this value is used. I'm putting this in
            // because GatherDataItemsFromSettings fills it in, and if it IS used, I want to keep it consistent
            // with the new notion of what national language 1 means.
            // Taking out for now to see what breaks.
            //_dataset.AddLanguageString("nameOfNationalLanguage1",
            //	XmlString.FromUnencoded(MetadataLanguage1.Name), "*", true);
        }

        /// <summary>
        /// For trilingual books, this is the third language to show
        /// </summary>
        public String Language3Tag
        {
            get
            {
                if (!_gotLangCache)
                    CacheLangData();
                return _cachedLangTag3;
            }
        }

        /// <summary>
        /// A book-level style number sequence
        /// </summary>
        public int StyleNumberSequence
        {
            get
            {
                lock (thisLock)
                {
                    GatherDataItemsFromXElement(_dataset, _dom.RawDom);
                    string curSeqStr = GetVariableOrNull("styleNumberSequence", "*").Unencoded;
                    int curSeq;
                    int nextSeq = 1;
                    if (Int32.TryParse(curSeqStr, out curSeq))
                        nextSeq = curSeq + 1;
                    Set(
                        "styleNumberSequence",
                        XmlString.FromUnencoded(nextSeq.ToString(CultureInfo.InvariantCulture)),
                        false
                    );
                    return nextSeq;
                }
            }
        }

        public void UpdateVariablesAndDataDivThroughDOM(BookInfo info = null)
        {
            // For some reason, cleaning up the anchor elements in the div#bloomDataDiv doesn't work
            // when called from HtmlDom.ProcessPageAfterEditing.  Doing it here works.
            var div = _dom.Body.SelectSingleNode("div[@id='bloomDataDiv']") as SafeXmlElement;
            if (div != null)
                HtmlDom.CleanupAnchorElements(div);
            UpdateVariablesAndDataDiv(_dom.RawDom.FirstChild, info);
        }

        /// <summary>
        /// Create or update the data div with all the data-book values in the document
        /// </summary>
        /// <param name="dom">This is either the whole document, or a page div that we just edited and want to read from.</param>
        public void SuckInDataFromEditedDom(HtmlDom dom, BookInfo info = null)
        {
            UpdateVariablesAndDataDiv(dom.RawDom, info);
        }

        public void SynchronizeDataItemsThroughoutDOM()
        {
            var itemsToDelete = new HashSet<Tuple<string, string>>();
            SynchronizeDataItemsFromContentsOfElement(_dom.Body, itemsToDelete);
        }

        /// <summary>
        /// Create or update the data div with all the data-book values in the document
        /// </summary>
        /// <param name="elementToReadFrom">This is either the whole document, or a page div that we just edited and want to read from.</param>
        private void UpdateVariablesAndDataDiv(SafeXmlNode elementToReadFrom, BookInfo info = null)
        {
            //Debug.WriteLine("before update: " + _dataDiv.OuterXml);

            var itemsToDelete = new HashSet<Tuple<string, string>>();
            DataSet incomingData = SynchronizeDataItemsFromContentsOfElement(
                elementToReadFrom,
                itemsToDelete
            );
            UpdateToolRelatedDataFromBookInfo(info, incomingData, itemsToDelete);
            incomingData.UpdateGenericLanguageString(
                "contentLanguage1",
                XmlString.FromUnencoded(Language1.Tag),
                false
            );
            incomingData.UpdateGenericLanguageString(
                "contentLanguage2",
                String.IsNullOrEmpty(Language2Tag) ? null : XmlString.FromUnencoded(Language2Tag),
                false
            );
            incomingData.UpdateGenericLanguageString(
                "contentLanguage3",
                String.IsNullOrEmpty(Language3Tag) ? null : XmlString.FromUnencoded(Language3Tag),
                false
            );
            incomingData.UpdateGenericLanguageString(
                "signLanguage",
                string.IsNullOrEmpty(SignLanguageTag)
                    ? null
                    : XmlString.FromUnencoded(SignLanguageTag),
                false
            );

            //Debug.WriteLine("xyz: " + _dataDiv.OuterXml);
            foreach (var v in incomingData.TextVariables)
            {
                if (!v.Value.IsCollectionValue)
                    UpdateSingleTextVariableInDataDiv(v.Key, v.Value);
            }
            foreach (var tuple in itemsToDelete)
                UpdateSingleTextVariableInDataDiv(tuple.Item1, tuple.Item2, XmlString.Empty);
            foreach (var attributeSet in incomingData.XmatterPageDataAttributeSets)
                PushXmatterPageAttributesIntoDataDiv(attributeSet);
            //Debug.WriteLine("after update: " + _dataDiv.OuterXml);

            UpdateTitle(info); //this may change our "bookTitle" variable if the title is based on a template that reads other variables (e.g. "Primer Term2-Week3")
            UpdateIsbn(info);
            if (info != null)
                UpdateBookInfoTags(info);
            UpdateCredits(info);
        }

        private void UpdateToolRelatedDataFromBookInfo(
            BookInfo info,
            DataSet incomingData,
            HashSet<Tuple<string, string>> itemsToDelete
        )
        {
            if (info == null)
                return; // only in tests
            var tools = info.Tools;
            var bookClass = _dom.Body.GetAttribute("class");

            if (!bookClass.Contains("leveled-reader") && !bookClass.Contains("decodable-reader"))
            {
                incomingData.UpdateGenericLanguageString(
                    "levelOrStageNumber",
                    XmlString.Empty,
                    false
                );
                itemsToDelete.Add(new Tuple<string, string>("levelOrStageNumber", "*"));
                return;
            }

            var levelTool = tools.FirstOrDefault(t => t.ToolId == "leveledReader");
            if (levelTool != null && bookClass.Contains("leveled-reader"))
            {
                var level = levelTool.State;
                incomingData.UpdateGenericLanguageString(
                    "levelOrStageNumber",
                    XmlString.FromUnencoded(level),
                    false
                );
                itemsToDelete.RemoveWhere(item => item.Item1 == "levelOrStageNumber");
            }

            var decodableTool = tools.FirstOrDefault(t => t.ToolId == "decodableReader");
            if (decodableTool != null && bookClass.Contains("decodable-reader"))
            {
                var stageString = decodableTool.State
                    ?.Split(';')
                    .FirstOrDefault()
                    ?.Split(':')
                    .Skip(1)
                    .FirstOrDefault();
                if (!string.IsNullOrEmpty(stageString))
                {
                    incomingData.UpdateGenericLanguageString(
                        "levelOrStageNumber",
                        XmlString.FromUnencoded(stageString),
                        false
                    );
                    itemsToDelete.RemoveWhere(item => item.Item1 == "levelOrStageNumber");
                }

                int stage;
                if (int.TryParse(stageString, out stage) && CollectionSettings != null)
                {
                    var settingsPath = DecodableReaderToolSettings.GetReaderToolsSettingsFilePath(
                        CollectionSettings
                    );
                    if (RobustFile.Exists(settingsPath))
                    {
                        try
                        {
                            var settingsJson = RobustFile.ReadAllText(settingsPath, Encoding.UTF8);
                            var settings = DynamicJson.Parse(settingsJson);
                            var stages = settings.stages;
                            var allLetters = "";
                            for (var i = 0; i < stage; i++)
                            {
                                var stageData = stages[i];
                                allLetters += " " + stageData.letters;
                            }
                            var letters = string.Join(", ", allLetters.Trim().Split(' '));
                            incomingData.UpdateLanguageString(
                                "decodableStageLetters",
                                XmlString.FromUnencoded(letters),
                                Language1.Tag,
                                false
                            );
                            itemsToDelete.RemoveWhere(
                                item => item.Item1 == "decodableStageLetters"
                            );
                        }
                        catch (XmlException e)
                        {
                            // The file fails to parse somehow
                            Debug.WriteLine(e.Message);
                        }
                        catch (RuntimeBinderException e)
                        {
                            // Happens when we don't find the expected stages or letters properties,
                            // or when the stages array has too few elements.
                            Debug.WriteLine(e.Message);
                        }
                        // other exceptions we want to know about.
                    }
                }
            }
        }

        /// <summary>
        /// We have a set of attributes which belong to an xmatter page; we need to update the data div with those values.
        /// In attributeSet, the key is the page name (such as "frontCover"); the value is the set of attributes as key-value pairs.
        /// </summary>
        private void PushXmatterPageAttributesIntoDataDiv(
            KeyValuePair<string, ISet<KeyValuePair<string, string>>> attributeSet
        )
        {
            var dataDivElementForThisXmatterPage = (SafeXmlElement)
                _dataDiv.SelectSingleNode($"div[@{kDataXmatterPage}='{attributeSet.Key}']");

            if (dataDivElementForThisXmatterPage != null)
                _dataDiv.RemoveChild(dataDivElementForThisXmatterPage);

            dataDivElementForThisXmatterPage = AddDataDivElement(
                kDataXmatterPage,
                attributeSet.Key
            );

            foreach (var attributeKvp in attributeSet.Value)
                dataDivElementForThisXmatterPage.SetAttribute(attributeKvp.Key, attributeKvp.Value);

            // Ok, we've updated the dom; now we need to update the dataset.
            _dataset.UpdateXmatterPageDataAttributeSet(attributeSet.Key, attributeSet.Value);
        }

        private void MigrateData(DataSet data)
        {
            MigrateTopic(data);
        }

        private void MigrateTopic(DataSet data)
        {
            DataSetElementValue topic;
            if (!data.TextVariables.TryGetValue("topic", out topic))
                return;

            CleanupTopic(topic);

            MigrateSpiritualTopic(data, topic);
        }

        //Until late in Bloom 3, we collected the topic in the National language, which is messy because then we would have to know how to
        //translate from all those languages to all other languages. Now, we just save English, and translate from English to whatever.
        //By far the largest number of books posted to bloomlibrary with this problem were Tok Pisin books, which actually just had
        //an English word as their value for "topic", so there we just switch it over to English.
        private void CleanupTopic(DataSetElementValue topic)
        {
            var topicStrings = topic.TextAlternatives;
            if (string.IsNullOrEmpty(topicStrings["en"]) && topicStrings["tpi"] != null)
            {
                topicStrings["en"] = topicStrings["tpi"];

                topicStrings.RemoveLanguageForm(topicStrings.Find("tpi"));
            }

            // BL-2746 For awhile during the v3.3 beta period, after the addition of ckeditor
            // our topic string was getting wrapped in html paragraph markers. There were a good
            // number of beta testers, so we need to clean up that mess.
            topicStrings.Forms.ForEach(
                languageForm =>
                    topicStrings[languageForm.WritingSystemId] = languageForm.Form
                        .Replace("<p>", "")
                        .Replace("</p>", "")
            );

            if (!string.IsNullOrEmpty(topicStrings["en"]))
            {
                //starting with 3.5, we only store the English key in the datadiv.
                topicStrings.Forms
                    .Where(lf => lf.WritingSystemId != "en")
                    .ForEach(lf => topicStrings.RemoveLanguageForm(lf));

                _dom.SafeSelectNodes(
                        "//div[@id='bloomDataDiv']/div[@data-book='topic' and not(@lang='en')]"
                    )
                    .Cast<SafeXmlElement>()
                    .ForEach(e => e.ParentNode.RemoveChild(e));
            }
        }

        // In 5.6, we removed the Spiritual topic and added the Bible topic.
        // We can auto-migrate from Spiritual to Bible if the copyright
        // (or original copyright) indicates it is appropriate.
        // Otherwise, we just remove the Spiritual topic.
        private void MigrateSpiritualTopic(DataSet data, DataSetElementValue topic)
        {
            var topicStr = topic.TextAlternatives["en"];
            if (topicStr != "Spiritual")
                return;

            string copyrightStr = null;
            if (data.TextVariables.TryGetValue("copyright", out var copyright))
            {
                copyrightStr = copyright.TextAlternatives["*"];
            }
            if (
                string.IsNullOrEmpty(copyrightStr)
                && data.TextVariables.TryGetValue("originalCopyright", out var originalCopyright)
            )
            {
                copyrightStr = originalCopyright.TextAlternatives["*"];
            }
            if (!string.IsNullOrEmpty(copyrightStr))
            {
                foreach (
                    var bibleCopyright in new[]
                    {
                        "Bible",
                        "SIL",
                        "Global Recordings",
                        "Kartidaya",
                        "Little Zebra",
                        "JMPBK",
                        "WPS"
                    }
                )
                {
                    if (copyrightStr.Contains(bibleCopyright))
                    {
                        topic.TextAlternatives["en"] = "Bible";
                        return;
                    }
                }
            }

            // We didn't find a Bible-related copyright; just remove the Spiritual topic from the book.
            data.TextVariables.Remove("topic");
        }

        private void UpdateCredits(BookInfo info)
        {
            if (info == null)
                return;

            DataSetElementValue creditsData;
            string credits = "";
            var idsToTry = WritingSystemIdsToTry.ToList();
            while (string.IsNullOrWhiteSpace(credits) && idsToTry.Count > 0)
            {
                if (_dataset.TextVariables.TryGetValue("originalAcknowledgments", out creditsData))
                {
                    credits = creditsData.TextAlternatives.GetBestAlternativeString(idsToTry);
                }
                try
                {
                    // This cleans out various kinds of markup, especially <br >, <p>, <b>, etc.
                    var elt = XElement.Parse(
                        "<div>" + credits + "</div>",
                        LoadOptions.PreserveWhitespace
                    );
                    // For some reason Value yields \n rather than platform newlines, even when the original
                    // has \r\n.
                    credits = elt.Value.Replace("\n", Environment.NewLine);
                }
                catch (XmlException ex)
                {
                    Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(ex);

                    // If we can't parse it...maybe the user really did type some XML? Just keep what we have
                }
                // If the most promising alternative is empty (e.g., vernacular usually is <p>\r\n</p>)
                // try again.
                idsToTry.RemoveAt(0);
            }
            info.Credits = credits;
        }

        /// <summary>
        /// Setup the display of the book's languages.  These are supposed to be in the national language (L2)
        /// if at all possible, so display using that language in order to use its font settings.
        /// </summary>
        /// <remarks>
        /// The places in the xmatter that use the languagesOfBook value have a @data-derived attribute instead
        /// of a @data-book attribute.  Old books with @data-book attributes should be updated automatically
        /// when the xmatter is refreshed.
        /// </remarks>
        public void SetupDisplayOfLanguagesOfBook(DataSet data = null)
        {
            if (data == null)
                data = _dataset;
            DataSetElementValue langData;
            if (!data.TextVariables.TryGetValue("languagesOfBook", out langData))
                return;

            // NOTE: The DataSetElementValue class defines that its TextAlternatives should always contains the InnerXml (or otherwise encoded) form
            // of the element, not its InnerText (unencoded) form
            // For example, the text alternatives might store strings with entity encoding, such as "Hak&#246;" instead of "Hakö"
            // We want to make sure that this "&#246;" gets displayed to the user as "ö" rather than as "&#246;".  See BL-9972
            var languagesXml = langData.TextAlternatives.GetExactAlternative("*");
            if (string.IsNullOrEmpty(languagesXml))
                return;
            var elements = this._dom.SafeSelectNodes("//div[@data-derived='languagesOfBook']");

            // Normally, this doesn't happen because xmatter pages should get updated to have data-derived.
            // But some custom xmatters don't contain this field.
            if (elements == null || elements.Length == 0)
                elements = this._dom.SafeSelectNodes(
                    "//div[not(@id='bloomDataDiv')]//div[@data-book='languagesOfBook']"
                );

            if (elements == null || elements.Length == 0)
                return; // must be in a test...
            foreach (var element in elements.Cast<SafeXmlElement>())
            {
                element.SetAttribute("lang", MetadataLanguage1Tag);
                SetNodeXml("languagesOfBook", XmlString.FromXml(languagesXml), element);
            }
        }

        /// <summary>
        /// Topics are uni-directional value, react™-style. The UI tells the book to change the topic
        /// key, and then eventually the page/book is re-evaluated and the appropriate topic is displayed
        /// on the page.
        /// To differentiate from fields with @data-book, which are two-way, the topic on the page instead
        /// has a @data-derived attribute (in the data-div, it is still a data-book... perhaps that too could
        /// change to something like data-book-source, but it's not clear to me yet, so.. not yet).
        /// When the topic is changed, the javascript sends c# a message with the new English Key for the topic is set in the data-div,
        /// and then the page is re-computed. That leads to this method, which grabs the
        /// english topic (which serves as the 'key') from the datadiv. It then finds the placeholder
        /// for the topic and fills it with the best translation it can find.
        /// </summary>
        private void SetUpDisplayOfTopicInBook(DataSet data)
        {
            var topicPageElement = this._dom.SelectSingleNode("//div[@data-derived='topic']");
            if (topicPageElement == null)
            {
                //old-style. here we don't have the data-derived, so we need to avoid picking from the datadiv
                topicPageElement = this._dom.SelectSingleNode(
                    "//div[not(id='bloomDataDiv')]//div[@data-book='topic']"
                );
                if (topicPageElement == null)
                {
                    //most unit tests do not have complete books, so this not surprising. It just means we don't have anything to do
                    return;
                }
            }
            //clear it out what's there now
            topicPageElement.RemoveAttribute("lang");
            topicPageElement.InnerText = "";

            DataSetElementValue topicData;

            var parentOfTopicDisplayElement = ((SafeXmlElement)(topicPageElement.ParentNode));
            //this just lets us have css rules that vary if there is a topic (allows other text to be centered instead left-aligned)
            //we'll change it later if we find there is a topic
            parentOfTopicDisplayElement.SetAttribute("data-have-topic", "false");

            //if we have no topic element in the data-div
            //leave the field in the page with an empty text.
            if (!data.TextVariables.TryGetValue("topic", out topicData))
            {
                return;
            }

            //we use English as the "key" for topics.
            var englishTopic = topicData.TextAlternatives.GetExactAlternative("en");

            //if we have no topic, just clear it out from the page
            if (string.IsNullOrEmpty(englishTopic) || englishTopic == "NoTopic")
                return;

            parentOfTopicDisplayElement.SetAttribute("data-have-topic", "true");

            var stringId = "Topics." + englishTopic;

            var tagsInPriorityOrder = GetLanguagePrioritiesForLocalizedTextOnPage();
            var langOfTopicToShowOnCover =
                tagsInPriorityOrder.FirstOrDefault(
                    t => LocalizationManager.GetIsStringAvailableForLangId(stringId, t)
                ) ?? "en";

            var bestTranslation = LocalizationManager.GetDynamicStringOrEnglish(
                "Bloom",
                stringId,
                englishTopic,
                "this is a book topic",
                langOfTopicToShowOnCover
            );

            //NB: in a unit test environment, GetDynamicStringOrEnglish is going to give us the id back, which is annoying.
            if (bestTranslation == stringId)
                bestTranslation = englishTopic;

            topicPageElement.SetAttribute("lang", langOfTopicToShowOnCover);
            topicPageElement.InnerText = bestTranslation;
        }

        private void UpdateIsbn(BookInfo info)
        {
            if (info == null)
                return;

            DataSetElementValue isbnData;
            string isbn = null;
            if (_dataset.TextVariables.TryGetValue("ISBN", out isbnData))
            {
                isbn = isbnData.TextAlternatives.GetBestAlternativeString(WritingSystemIdsToTry); // Review: not really multilingual data, do we need this?
            }
            info.Isbn = isbn ?? "";
        }

        // For now, when there is no UI for multiple tags, we make Tags a single item, the book topic.
        // It's not clear what we will want to do when the topic changes and there is a UI for (possibly multiple) tags.
        // Very likely we still want to add the new topic (if it is not already present).
        // Should we still remove the old one?
        private void UpdateBookInfoTags(BookInfo info)
        {
            info.TopicsList = GetVariableOrNull("topic", "en").Xml; //topic key always in english
        }

        /// <summary>
        ///
        /// </summary>
        /// <remarks>I (jh) found this labelled UpdateSingleTextVariableThrougoutDom but it actually only updated the datadiv, so I changed the name.</remarks>
        /// <param name="key"></param>
        /// <param name="multiText"></param>
        private void UpdateSingleTextVariableInDataDiv(string key, DataSetElementValue v)
        {
            //Debug.WriteLine("before: " + dataDiv.OuterXml);
            var multiText = v.TextAlternatives;

            if (multiText.Count == 0)
            {
                RemoveDataDivElementIfEmptyValue(key, null);
            }
            foreach (LanguageForm languageForm in multiText.Forms)
            {
                string writingSystemId = languageForm.WritingSystemId;
                var attrs = v.GetAttributeList(writingSystemId);
                UpdateSingleTextVariableInDataDiv(
                    key,
                    writingSystemId,
                    XmlString.FromXml(languageForm.Form),
                    attrs
                );
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <remarks>I (jh) found this labelled UpdateSingleTextVariableThrougoutDom but it actually only updated the datadiv, so I changed the name.</remarks>
        /// <param name="key"></param>
        /// <param name="writingSystemId"></param>
        /// <param name="form"></param>
        private void UpdateSingleTextVariableInDataDiv(
            string key,
            string writingSystemId,
            XmlString form,
            List<Tuple<string, XmlString>> attrs = null
        )
        {
            var node = _dataDiv.SelectSingleNode(
                String.Format("div[@data-book='{0}' and @lang='{1}']", key, writingSystemId)
            );

            var userDeleted =
                attrs?.FirstOrDefault(
                    (x) => x.Item1 == "data-user-deleted" && x.Item2.Xml == "true"
                ) != null;
            _dataset.UpdateLanguageString(
                key,
                form,
                DealiasWritingSystemId(writingSystemId),
                isCollectionValue: false,
                storeEmptyValue: userDeleted
            );

            if (null == node)
            {
                if (userDeleted || !XmlString.IsNullOrEmpty(form))
                {
                    //Debug.WriteLine("creating in datadiv: {0}[{1}]={2}", key, writingSystemId, form);
                    //Debug.WriteLine("nop: " + _dataDiv.OuterXml);
                    var newElement = AddDataDivElementContainingBookVariable(
                        key,
                        writingSystemId,
                        form.Xml
                    );
                    MergeAttrsIntoElement(attrs, newElement);
                }
            }
            else
            {
                if (!userDeleted && XmlString.IsNullOrEmpty(form)) //a null value removes the entry entirely
                {
                    node.ParentNode.RemoveChild(node);
                }
                else
                {
                    SetNodeXml(key, form, node);
                    MergeAttrsIntoElement(attrs, node as SafeXmlElement);
                }
                //Debug.WriteLine("updating in datadiv: {0}[{1}]={2}", key, languageForm.WritingSystemId,
                //				languageForm.Form);
                //Debug.WriteLine("now: " + _dataDiv.OuterXml);
            }
        }

        private void SetNodeXml(string key, XmlString form, SafeXmlNode node)
        {
            if (KeysOfVariablesThatAreUrlEncoded.Contains(key))
            {
                // Reference: BL-3235
                //remove the url path encoding
                var decodedUrlStr = UrlPathString.CreateFromUrlEncodedString(form.Xml).NotEncoded; // want query as well as filepath
                //switch to html/xml encoding
                form = XmlString.FromUnencoded(decodedUrlStr);
            }
            node.InnerXml = form.Xml;
            if (node.GetAttribute("data-textonly") == "true")
            {
                // In most contexts, it's fine for Bloom to wrap the content of a div in P elements and so forth.
                // If we want to synchronize to something like a text element in an SVG, the result must be only
                // text.
                // This operation looks like a no-op, but actually it removes any markup and just keeps the text
                // that was synchronized.
                node.InnerText = node.InnerText;
            }
        }

        public SafeXmlElement AddDataDivElementContainingBookVariable(
            string key,
            string lang,
            string form
        )
        {
            return AddDataDivElement("data-book", key, lang, XmlString.FromXml(form));
        }

        public SafeXmlElement AddDataDivElement(
            string type,
            string key,
            string lang = null,
            XmlString form = null
        )
        {
            if (form == null)
                form = XmlString.Empty;

            var newDiv = _dom.RawDom.CreateElement("div");
            newDiv.SetAttribute(type, key);
            if (lang != null)
                newDiv.SetAttribute("lang", lang);
            SetNodeXml(key, form, newDiv);
            GetOrCreateDataDiv().AppendChild(newDiv);
            return newDiv;
        }

        public void Set(string key, XmlString value, bool isCollectionValue)
        {
            _dataset.UpdateGenericLanguageString(key, value, isCollectionValue);
            UpdateSingleTextVariableInDataDiv(key, _dataset.TextVariables[key]);
            // These three props, which are updated when the user checks and unchecks languages
            // in the edit-mode languages menu, are the source for the values set by
            // CacheLangData (along with the collection languages...but when one of those
            // changes we reload everything completely). So when one changes, we have to
            // update the caches themselves and various values derived from them.
            // The methods called here capture various common work that needs to be done
            // both here and in CacheLangData.
            if (key == "contentLanguage1")
                UpdateLang1Derivatives(value.Unencoded);
            else if (key == "contentLanguage2")
            {
                UpdateLang2Derivatives(value.Unencoded);
            }
            else if (key == "contentLanguage3")
                UpdateLang3Derivatives(value.Unencoded);
            // Enhance: if it becomes possible to set ML1 without a collection-level
            // change that reloads everything, we might need to call UpdateMetadataLang1Derivatives() here.
        }

        /// <summary>
        /// If the input WS is one of three magic ones, translate to the actual language;
        /// otherwise return the input unchanged.
        /// </summary>
        /// <remarks>Other aliases are used in the system, at least L1, L2, and L3. Possibly
        /// this method should be enhanced to understand them, also. It is currently replacing
        /// a dictionary we were maintaining in DataSet which only had these three values,
        /// so until we use it more widely, handling them is enough.</remarks>
        public string DealiasWritingSystemId(string writingSystemId)
        {
            switch (writingSystemId)
            {
                case "V":
                    return Language1Tag;
                case "N1":
                    return MetadataLanguage1Tag;
                case "N2":
                    return MetadataLanguage2Tag;
                default:
                    return writingSystemId;
            }
        }

        /// <summary>
        /// Return a dictionary with the conversions that DealiasWritingSystemId makes.
        /// (If this was very frequently used, we could cache it, but maintaining it
        /// is a pain.)
        /// </summary>
        public Dictionary<string, string> WritingSystemAliases
        {
            get
            {
                var result = new Dictionary<string, string>();
                result.Add("V", Language1Tag);
                result.Add("N1", MetadataLanguage1Tag);
                result.Add("N2", MetadataLanguage2Tag);
                return result;
            }
        }

        public void Set(string key, XmlString value, string lang)
        {
            _dataset.UpdateLanguageString(key, value, DealiasWritingSystemId(lang), false);
            if (_dataset.TextVariables.ContainsKey(key))
            {
                UpdateSingleTextVariableInDataDiv(key, _dataset.TextVariables[key]);
            }
            else //we go this path if we just removed the last value from the multitext
            {
                RemoveDataDivElementIfEmptyValue(key, value);
            }
        }

        public void RemoveSingleForm(string key, string lang)
        {
            Set(key, null, lang);
        }

        public void RemoveAllForms(string key)
        {
            var dataDiv = GetOrCreateDataDiv();
            foreach (var e in dataDiv.SafeSelectNodes(String.Format("div[@data-book='{0}']", key)))
            {
                dataDiv.RemoveChild(e);
            }
            if (_dataset.TextVariables.ContainsKey(key))
            {
                _dataset.TextVariables.Remove(key);
            }
        }

        private SafeXmlElement GetOrCreateDataDiv()
        {
            if (_dataDiv != null)
                return _dataDiv;
            _dataDiv = _dom.RawDom.SelectSingleNode("//div[@id='bloomDataDiv']") as SafeXmlElement;
            if (_dataDiv == null)
            {
                _dataDiv = _dom.RawDom.CreateElement("div");
                _dataDiv.SetAttribute("id", "bloomDataDiv");
                _dom.RawDom.SelectSingleNode("//body").InsertAfter(_dataDiv, null);
            }
            return _dataDiv;
        }

        /// <summary>
        /// Go through the document, reading in values from fields, and then pushing variable values back into fields.
        /// Here we're calling "fields" the html supplying or receiving the data, and "variables" being key-value pairs themselves, which
        /// are, for library variables, saved in a separate file.
        /// </summary>
        /// <param name="elementToReadFrom"> </param>
        private DataSet SynchronizeDataItemsFromContentsOfElement(
            SafeXmlNode elementToReadFrom,
            HashSet<Tuple<string, string>> itemsToDelete
        )
        {
            // We make a new dataset here, distinct from _dataset in our member variable. This is because, in GatherDataItemsFromXElement below,
            // we want to populate the data set (and then later UpdateDomFromDataSet) using the data in elementToReadFrom, often a just-edited page.
            // Since GatherDataItemsFromXElement prefers the first value it finds for any key combination, and does this by ignoring any
            // data for a key combination already present, we would not use the new data in elementToReadFrom if we started with
            // _dataset, which is already populated from the current document.
            DataSet data = new DataSet();
            GatherDataItemsFromSettings(data, CollectionSettings);

            // The first encountered value for data-book/data-collection wins... so the rest better be read-only to the user, or they're in for some frustration!
            // If we don't like that, we'd need to create an event to notice when field are changed. Usually this is fine, because
            // we're gathering from a single page we just edited, and don't usually have duplicate data on the same page.
            // If we're reading from the whole book, the data div comes first and wins as intended.

            GatherDataItemsFromXElement(data, elementToReadFrom, itemsToDelete);

            MigrateData(data);

            //            SendDataToDebugConsole(data);
            UpdateDomFromDataSet(data, "*", _dom.RawDom, itemsToDelete);

            //REVIEW: the calls above are, for the reason stated, acting on a local DataSet, "data".
            //But then these ones are acting on the member variable. Seems like something should update _dataset
            // to account for the changes we just pulled in, and before we UpdateTitle, etc. It's possible that
            // another method of this class typically gets called after this one and fixes things (in which case the
            // calls below are possibly redundant).
            UpdateTitle();
            SetupDisplayOfLanguagesOfBook(_dataset);
            SetUpDisplayOfTopicInBook(_dataset);
            return data;
        }

        private void GatherDataItemsFromSettings(
            DataSet data,
            CollectionSettings collectionSettings
        )
        {
            //            if (makeGeneric)
            //            {
            //                data.WritingSystemCodes.Add("V", collectionSettings.Language2Tag);
            //                    //This is not an error; we don't want to use the verncular when we're just previewing a book in a non-verncaulr collection
            //                data.AddGenericLanguageString("iso639Code", collectionSettings.Language1Tag, true);
            //                    //review: maybe this should be, like 'xyz"
            //                data.AddGenericLanguageString("nameOfLanguage", "(Your Language Name)", true);
            //                data.AddGenericLanguageString("nameOfNationalLanguage1", "(Region Lang)", true);
            //                data.AddGenericLanguageString("nameOfNationalLanguage2", "(National Lang)", true);
            //                data.AddGenericLanguageString("country", "Your Country", true);
            //                data.AddGenericLanguageString("province", "Your Province", true);
            //                data.AddGenericLanguageString("district", "Your District", true);
            //                data.AddGenericLanguageString("languageLocation", "(Language Location)", true);
            //            }
            //            else
            {
                // Not sure what these three should be. As far as I can determine, none of the values is used.
                // If they are used, they almost certainly ought to be based on the book's languages, which can
                // now be different from the collection languages. But this function is used to initialize a brand
                // new dataset, often for a brand new BookData, which may not have any LanguageN set yet, so
                // utilizing those values is problematic. Until we determine that these are used for something,
                // I'm setting them here, and updating them when anything changes the BookData language values.
                // For now, we're taking this out to see if anything breaks. May not be needed anyway since set in caching code.
                //data.AddLanguageString("nameOfLanguage", XmlString.FromUnencoded(collectionSettings.Language1.Name), "*", true);
                //data.AddLanguageString("nameOfNationalLanguage1",
                //					   XmlString.FromUnencoded(collectionSettings.Language2.Name), "*", true);

                //data.AddLanguageString("nameOfNationalLanguage2",
                //					   XmlString.FromUnencoded(collectionSettings.Language3?.Name??""), "*", true);
                data.UpdateGenericLanguageString(
                    "iso639Code",
                    XmlString.FromUnencoded(Language1Tag),
                    true
                );
                data.UpdateGenericLanguageString(
                    "country",
                    XmlString.FromUnencoded(collectionSettings.Country),
                    true
                );
                data.UpdateGenericLanguageString(
                    "province",
                    XmlString.FromUnencoded(collectionSettings.Province),
                    true
                );
                data.UpdateGenericLanguageString(
                    "district",
                    XmlString.FromUnencoded(collectionSettings.District),
                    true
                );
                string location = "";
                var separator = LocalizationManager.GetString(
                    "EditTab.FrontMatter.ListSeparator",
                    ", ",
                    "This is used to separate items in a list, such as 'Province, District, Country' on the Title Page. For English, that means comma followed by a space. Don't forget the space if your script uses them.",
                    GetLanguagePrioritiesForLocalizedTextOnPage(false),
                    out _
                );
                if (!String.IsNullOrEmpty(collectionSettings.District))
                    location += collectionSettings.District + separator;
                if (!String.IsNullOrEmpty(collectionSettings.Province))
                    location += collectionSettings.Province + separator;

                if (!String.IsNullOrEmpty(collectionSettings.Country))
                {
                    location += collectionSettings.Country;
                }

                location = TrimEnd(location, separator);

                data.UpdateGenericLanguageString(
                    "languageLocation",
                    XmlString.FromUnencoded(location),
                    true
                );
            }
        }

        private static string TrimEnd(string source, string value)
        {
            while (source.EndsWith(value))
                source = source.Remove(source.LastIndexOf(value));
            return source;
        }

        /// <summary>
        /// Give the string the user expects to see as the name of a specified language.
        /// This routine uses the user-specified name for the main project language.
        /// For the other two project languages, it explicitly uses the appropriate collection settings
        /// name for that language, which the user also set.
        /// If the user hasn't set a name for the given language, this will find a fairly readable name
        /// for the languages Palaso knows about (probably the autonym) and fall back to the code itself
        /// if it can't find a name.
        /// BL-8174 But in case the code includes Script/Region/Variant codes, we should show them somewhere too.
        /// </summary>
        public string GetDisplayNameForLanguage(string code)
        {
            // Why aren't we using book languages here? We're just trying to get a name, and the book languages are always
            // among the collection languages, so it means three things to check instead of four. And if by any chance
            // we're wanting a name of a language that is not currently in use in the book, but is known to the collection,
            // it's desirable to find it.

            return CollectionSettings.GetDisplayNameForLanguage(code, MetadataLanguage1Tag);
        }

        /// <summary>
        /// walk through the sourceDom, collecting up values from elements that have data-book or data-collection or data-xmatter-page attributes.
        /// </summary>
        public void GatherDataItemsFromXElement(
            DataSet data,
            SafeXmlNode sourceElement, // can be the whole sourceDom or just a page
            HashSet<Tuple<string, string>> itemsToDelete = null
        ) // records key, lang pairs for which we found an empty element in the source.
        {
            string elementName = "*";
            try
            {
                string query =
                    $".//{elementName}[(@data-book or @data-library or @data-collection or @{kDataXmatterPage}) and not(contains(@class,'bloom-writeOnly'))]";

                var nodesOfInterest = sourceElement.SafeSelectNodes(query);

                foreach (SafeXmlElement node in nodesOfInterest)
                {
                    bool isCollectionValue = false;

                    string key = node.GetAttribute("data-book").Trim();
                    if (key == String.Empty)
                    {
                        key = node.GetAttribute(kDataXmatterPage).Trim();
                        if (key != String.Empty)
                        {
                            if (!data.XmatterPageDataAttributeSets.ContainsKey(key))
                                GatherXmatterPageDataAttributeSetIntoDataSet(data, node);
                            // This element has a data-xmatter-page attribute. So it is a bloom-page div.
                            // And currently a bloom-page cannot also be an element waiting to be filled with data-collection, so we're done here.
                            continue;
                        }
                        key = node.GetAttribute("data-collection").Trim();
                        if (key == String.Empty)
                        {
                            key = node.GetAttribute("data-library").Trim(); //the old (pre-version 1) name of collections was 'library'
                        }
                        isCollectionValue = true;
                    }

                    string value;
                    // BL-9111 The same key can be processed multiple times, but we only want to modify
                    // KeysOfVariablesThatAreUrlEncoded, if we actually insert a value into data.TextVariables.
                    // This flag lets us check later.
                    var isVariableUrlEncoded = false;
                    if (HtmlDom.IsImgOrSomethingWithBackgroundImage(node))
                    {
                        value = HtmlDom.GetImageElementUrl(node).UrlEncoded;
                        isVariableUrlEncoded = true;
                    }
                    else
                    {
                        var node1 = node.CloneNode(true); // so we can remove labels without modifying node
                        // Datadiv content should be node content without labels. The labels are not really part
                        // of the content we want to replicate, they are just information for the user, and
                        // specific to one context. Also, including them causes them to get repeated in each location;
                        // SetInnerXmlPreservingLabel() assumes data set content does not include label elements.
                        var labels = node1.SafeSelectNodes(".//label");
                        foreach (var label in labels)
                            label.ParentNode.RemoveChild(label);
                        value = node1.InnerXml.Trim(); //may contain formatting
                        if (KeysOfVariablesThatAreUrlEncoded.Contains(key))
                        {
                            value = UrlPathString.CreateFromHtmlXmlEncodedString(value).UrlEncoded;
                        }
                    }

                    string lang = node.GetOptionalStringAttribute("lang", "*");
                    if (lang == "") //the above doesn't stop a "" from getting through
                        lang = "*";
                    if (lang == "{V}")
                        lang = Language1.Tag;
                    if (lang == "{N1}")
                        lang = MetadataLanguage1Tag;
                    if (lang == "{N2}")
                        lang = MetadataLanguage2Tag;

                    bool userDeleted = node.GetAttribute("data-user-deleted") == "true";
                    if (StringAlternativeHasNoText(value) && !userDeleted)
                    {
                        // This is a value we may want to delete
                        if (itemsToDelete != null)
                            itemsToDelete.Add(Tuple.Create(key, lang));
                    }
                    else if (!value.StartsWith("{"))
                    //ignore placeholder stuff like "{Book Title}"; that's not a value we want to collect
                    {
                        if (
                            (
                                elementName.ToLowerInvariant() == "textarea"
                                || elementName.ToLowerInvariant() == "input"
                                || node.GetOptionalStringAttribute("contenteditable", "false")
                                    == "true"
                            ) && (lang == "V" || lang == "N1" || lang == "N2")
                        )
                        {
                            throw new ApplicationException(
                                "Editable element (e.g. TextArea) should not have placeholder @lang attributes (V,N1,N2)\r\n\r\n"
                                    + node.OuterXml
                            );
                        }

                        // if we don't have a value for this variable and this language, add it.
                        // (If we already do, keep the first value we found.)
                        // There are two ways we could NOT have a value already: either the key is not
                        // in data.TextVariables at all, or there is a DataSetElementValue for that key,
                        // but it contains no data for lang.
                        DataSetElementValue dsv;
                        bool added = false; // did we add a new value?
                        // If we already decided this key/lang should be deleted (we found a definite empty value for it),
                        // ignore anything we find later in the document for that combination.
                        // This was relevant in an anomalous situation where a title was deleted on the cover,
                        // the first sync failed to delete it on the title page, and a subsequent sync saw the
                        // empty title on the cover and made an entry in itemsToDelete, but later found
                        // the undeleted title on the title page and resurrected it. I'm not sure we need this test now that is fixed,
                        // but our principle is that the first element we find for a given key/lang combination wins,
                        // so I thought this should be fixed too.
                        if (
                            itemsToDelete == null
                            || !itemsToDelete.Contains(Tuple.Create(key, lang))
                        )
                        {
                            if (!data.TextVariables.TryGetValue(key, out dsv))
                            {
                                var t = new MultiTextBase();
                                t.SetAlternative(lang, value);
                                dsv = new DataSetElementValue(t, isCollectionValue);
                                data.TextVariables.Add(key, dsv);
                                added = true;
                            }
                            else if (!dsv.TextAlternatives.ContainsAlternative(lang))
                            {
                                MultiTextBase t = dsv.TextAlternatives;
                                if (userDeleted && string.IsNullOrEmpty(value))
                                    t.SetAnnotationOfAlternativeIsStarred(lang, true); // This allows empty strings to be saved and restored as empty strings.
                                else
                                    t.SetAlternative(lang, value);
                                added = true;
                            }

                            if (added)
                            {
                                if (isVariableUrlEncoded)
                                {
                                    if (value.Contains("%20%20"))
                                    {
                                        // This can be catastrophic. See BL-9145. Tidy will reduce the double space to a single space, and then it won't be the string
                                        // we tried to save. UrlEncoded things are often filenames or other resource locators, so removing a space means we won't find it.
                                        // Currently, the only data we think gets this urlencoded behavior is image sources, and we're preventing them from having
                                        // names with multiple spaces. Need to do the same for any other data where exactly preserving the content is essential.
                                        ErrorReport.ReportNonFatalMessageWithStackTrace(
                                            $"Trying to save URL-encoded value '{value}' for {key} with multiple spaces. This will probably cause problems. Please report it and, if possible, avoid the multiple spaces."
                                        );
                                    }

                                    KeysOfVariablesThatAreUrlEncoded.Add(key);
                                }

                                dsv.SetAttributeList(lang, GetAttributesToSave(node));
                            }
                        }
                    }

                    if (KeysOfVariablesThatAreUrlEncoded.Contains(key))
                    {
                        Debug.Assert(
                            !value.Contains("&amp;"),
                            "In memory, all image urls should be encoded such that & is just &."
                        );
                    }
                }
            }
            catch (Exception error)
            {
                throw new ApplicationException(
                    "Error in GatherDataItemsFromDom(,"
                        + elementName
                        + "). RawDom was:\r\n"
                        + sourceElement.OuterXml,
                    error
                );
            }
        }

        // Attributes not to copy when saving element attribute data in a DataSetElementValue.
        static HashSet<string> _attributesNotToCopy = new HashSet<string>(
            new[]
            {
                // Junk that gets left behind by UI
                "tabindex",
                "aria-describedby",
                "aria-label",
                "role",
                "spellcheck",
                "data-hasqtip",
                "data-languagetipcontent",
                // These are the keys that are used to match the copy-from and copy-to elements.
                // They are already on both so no point in copying.
                "data-book",
                "data-collection",
                // This is important because without it magic languages like "N1" could get overwritten by specific ones.
                "lang",
                // If there's explicit formatting on an element, we probably don't want the same on every copy of
                // the corresponding data-book.
                // Remove "style" is normally what want to do, but there is one case where we do not. See BL-9460.
                // "style"
            }
        );

        // These bloom-managed attributes must actually be removed from the destination if the element we're copying from
        // doesn't have them.
        static HashSet<string> _attributesToRemoveIfAbsent = new HashSet<string>(
            new[] { "data-audiorecordingendtimes" }
        );

        // Classes not to copy when saving a class attribute as part of a DataSetElementValue.
        // In addition we don't copy any class ending in -style, as these are key to the possibly different
        // appearance we want for the same data in different places.
        static HashSet<string> _classesNotToCopy = new HashSet<string>(
            new[]
            {
                // Really important NOT to copy; bloom code manages these.
                "bloom-content1",
                "bloom-visibility-code-on",
                "bloom-content2",
                "bloom-content3",
                "bloom-contentNational1",
                "bloom-contentNational2"
            }
        );

        // These bloom-managed classes must actually be removed from the destination if the element we're copying from
        // doesn't have them. (ui-suppressHighlight should never get into the DOM at all, but if it somehow sneaks by,
        // at least the next Save should be able to remove it.)
        static HashSet<string> _classesToRemoveIfAbsent = new HashSet<string>(
            new[] { "bloom-postAudioSplit", "ui-suppressHighlight" }
        );

        private List<Tuple<string, XmlString>> GetAttributesToSave(SafeXmlElement node)
        {
            var result = new List<Tuple<string, XmlString>>();
            foreach (var attr in node.AttributePairs)
            {
                if (_attributesNotToCopy.Contains(attr.Name))
                    continue;
                if (attr.Name == "class")
                {
                    var classes = attr.Value.Split().ToList();
                    classes.RemoveAll(x => _classesNotToCopy.Contains(x) || x.EndsWith("-style"));
                    result.Add(
                        Tuple.Create("class", XmlString.FromUnencoded(string.Join(" ", classes)))
                    );
                    continue;
                }
                result.Add(Tuple.Create(attr.Name, XmlString.FromUnencoded(attr.Value)));
            }
            // if the node is an img, save some extra data
            if (node.Name == "img")
            {
                var extras = HtmlDom.GetDataForReconstructingBackgroundImgWrapper(node);
                foreach (var extra in extras)
                {
                    result.Add(Tuple.Create(extra.Item1, XmlString.FromUnencoded(extra.Item2)));
                }
            }
            return result;
        }

        /// <summary>
        /// Xmatter pages sometimes have data-* attributes that can be changed by the user.
        /// For example, setting background music. This method reads those attributes into the given dataset under the xmatterPageKey.
        /// </summary>
        /// <param name="element">Could be the xmatter page element or the related div inside the data div</param>
        private void GatherXmatterPageDataAttributeSetIntoDataSet(
            DataSet dataSet,
            SafeXmlElement element
        )
        {
            var xmatterPageKey = element.GetAttribute(kDataXmatterPage).Trim();

            ISet<KeyValuePair<string, string>> attributes =
                new HashSet<KeyValuePair<string, string>>();
            foreach (var attribute in element.AttributePairs)
            {
                if (attribute.Name != kDataXmatterPage && attribute.Name.StartsWith("data-"))
                {
                    // xmatter pages are not numbered.  See https://issues.bloomlibrary.org/youtrack/issue/BL-7303.
                    // This will clean up books that have wrongly set backmatter page numbers.
                    // NB: if we ever decide to allow specifying some xmatter pages to be numbered, we'll need to figure
                    // out how to handle that information here and in HtmlDom.UpdatePageNumberAndSideClassOfPages().
                    if (attribute.Name == "data-page-number")
                        attributes.Add(new KeyValuePair<string, string>(attribute.Name, ""));
                    else
                        attributes.Add(
                            new KeyValuePair<string, string>(attribute.Name, attribute.Value)
                        );
                }
            }

            if (dataSet.XmatterPageDataAttributeSets.ContainsKey(xmatterPageKey))
                dataSet.XmatterPageDataAttributeSets.Remove(xmatterPageKey);
            dataSet.XmatterPageDataAttributeSets.Add(xmatterPageKey, attributes);
        }

        /// <summary>
        /// given the values in our dataset, push them out to the fields in the pages
        /// </summary>
        public void UpdateDomFromDataset()
        {
            var noItemsToDelete = new HashSet<Tuple<string, string>>();
            UpdateDomFromDataSet(_dataset, "*", _dom.RawDom, noItemsToDelete);
        }

        /// <summary>
        /// Where, for example, somewhere on a page something has data-book='foo' lang='fr',
        /// we set the value of that element to French subvalue of the data item 'foo', if we have one.
        /// </summary>
        private void UpdateDomFromDataSet(
            DataSet data,
            string elementName,
            SafeXmlDocument targetDom,
            HashSet<Tuple<string, string>> itemsToDelete
        )
        {
            try
            {
                var query =
                    $"//{elementName}[(@data-book or @data-collection or @data-library or @{kDataXmatterPage})]";
                var nodesOfInterest = targetDom.SafeSelectNodes(query);

                foreach (SafeXmlElement node in nodesOfInterest)
                {
                    var key = node.GetAttribute("data-book").Trim();

                    if (key == string.Empty)
                    {
                        key = node.GetAttribute(kDataXmatterPage).Trim();
                        if (key != string.Empty)
                        {
                            UpdateXmatterPageDataAttributeSets(data, node);
                            continue;
                        }
                        key = node.GetAttribute("data-collection").Trim();
                        if (key == string.Empty)
                        {
                            key = node.GetAttribute("data-library").Trim(); //"library" is the old name for what is now "collection"
                        }
                    }

                    if (string.IsNullOrEmpty(key))
                        continue;

                    if (data.TextVariables.ContainsKey(key))
                    {
                        if (UpdateImageFromDataSet(data, node, key))
                            continue;

                        var lang = DealiasWritingSystemId(
                            node.GetOptionalStringAttribute("lang", "*")
                        );

                        //							//see comment later about the inability to clear a value. TODO: when we re-write Bloom, make sure this is possible
                        //							if(data.TextVariables[key].TextAlternatives.Forms.Length==0)
                        //							{
                        //								//no text forms == desire to remove it. THe multitextbase prohibits empty strings, so this is the best we can do: completly remove the item.
                        //								targetDom.RemoveChild(node);
                        //							}
                        //							else
                        if (!string.IsNullOrEmpty(lang))
                        //if we don't even have this language specified (e.g. no national language), the  give up
                        {
                            //Ideally, we have this string, in this desired language.
                            DataSetElementValue dsv = data.TextVariables[key];
                            var form = dsv.TextAlternatives.GetBestAlternative(new[] { lang, "*" });
                            var s = form == null ? "" : form.Form;

                            if (KeysOfVariablesThatAreUrlEncoded.Contains(key))
                            {
                                Debug.Assert(
                                    !s.Contains("&amp;"),
                                    "In memory, all image urls should be encoded such that & is just &."
                                );
                            }
                            //But if not, maybe we should copy one in from another national language
                            if (StringAlternativeHasNoText(s))
                                s = PossiblyCopyFromAnotherLanguage(node, lang, data, key);

                            //NB: this was the focus of a multi-hour bug search, and it's not clear that I got it right.
                            //The problem is that the title page has N1 and n2 alternatives for title, the cover may not.
                            //the gather page was gathering no values for those alternatives (why not), and so GetBestAlternativeSTring
                            //was giving "", which we then used to remove our nice values.
                            //REVIEW: what affect will this have in other pages, other circumstances. Will it make it impossible to clear a value?
                            //Hoping not, as we are differentiating between "" and just not being in the multitext at all.
                            //don't overwrite a datadiv alternative with empty just becuase this page has no value for it.
                            // JohnT update: if we simply do nothing when dsv.TextAlternatives doesn't contain lang,
                            // that DOES prevent deleting stuff. We often got away with it, because the edited page would
                            // have the empty content from being edited, and itemsToDelete would list this key/lang combination
                            // as a result, and a couple of calls up the stack, UpdateVariablesAndDataDiv() would typically
                            // delete it from the data-div while processing itemsToDelete. But if we're looking at the bookTitle,
                            // which is typically on more than one page, there's still a page where it's not deleted, and the
                            // next update will pick that as preferred value. See BL-10739
                            if (
                                s == ""
                                && !dsv.TextAlternatives.ContainsAlternative(lang)
                                && !itemsToDelete.Contains(Tuple.Create(key, lang))
                            )
                                continue;

                            //hack: until I think of a more elegant way to avoid repeating the language name in N2 when it's the exact same as N1...
                            var n1Form = GetBestUnwrappedAlternative(
                                data.TextVariables[key].TextAlternatives,
                                new[] { MetadataLanguage1Tag, "*" }
                            );
                            if (lang == MetadataLanguage2Tag && n1Form != null && s == n1Form.Form)
                            {
                                s = ""; //don't show it in N2, since it's the same as N1
                            }
                            SetInnerXmlPreservingLabel(key, node, XmlString.FromXml(s));
                            var attrs = dsv.GetAttributeList(lang);
                            if (attrs != null)
                            {
                                MergeAttrsIntoElement(attrs, node);
                            }
                        }
                    }
                    else if (!HtmlDom.IsImgOrSomethingWithBackgroundImage(node))
                    {
                        // See whether we need to delete something
                        var lang = DealiasWritingSystemId(
                            node.GetOptionalStringAttribute("lang", "*")
                        );
                        if (itemsToDelete.Contains(Tuple.Create(key, lang)))
                        {
                            SetInnerXmlPreservingLabel(key, node, XmlString.Empty); // a later process may remove node altogether.
                        }
                    }
                }
            }
            catch (Exception error)
            {
                throw new ApplicationException(
                    "Error in UpdateDomFromDataSet(,"
                        + elementName
                        + "). RawDom was:\r\n"
                        + targetDom.OuterXml,
                    error
                );
            }
        }

        internal void MergeAttrsIntoElement(
            List<Tuple<string, XmlString>> attrs,
            SafeXmlElement node
        )
        {
            if (attrs == null)
                return;
            var attrsToRemove = new HashSet<string>(_attributesToRemoveIfAbsent);

            foreach (var tuple in attrs)
            {
                attrsToRemove.Remove(tuple.Item1); // won't remove this one!
                // class requires special treatment. We want a union of the classes the element already has
                // and the ones from the data set.
                if (tuple.Item1 == "class")
                {
                    var classesToRemove = new HashSet<string>(_classesToRemoveIfAbsent);
                    // There's probably a HashSet union function we could use here a little more concisely.
                    // I prefer not to disturb the order of the classes more than we have to.
                    var newClasses = tuple.Item2.Unencoded.Split();
                    var classes = node.GetAttribute("class").Split().ToList();
                    var currentSet = new HashSet<string>(classes);
                    foreach (var newClass in newClasses)
                    {
                        // If "ui-audioCurrent" has managed to make its way into the class attribute, it needs
                        // to be removed (and not reinserted!).  See BL-12094.
                        if (newClass == "ui-audioCurrent")
                        {
                            classesToRemove.Add(newClass);
                            continue;
                        }
                        classesToRemove.Remove(newClass); // in the source, should not remove
                        if (!currentSet.Contains(newClass))
                            classes.Add(newClass);
                    }

                    node.SetAttribute(
                        "class",
                        string.Join(" ", classes.Where(x => !classesToRemove.Contains(x)))
                    );
                    continue;
                }

                node.SetAttribute(tuple.Item1, tuple.Item2.Unencoded);
            }

            foreach (var attr in attrsToRemove)
            {
                if (node.HasAttribute(attr)) // not sure if we need this, RemoveAttribute may handle not found OK.
                    node.RemoveAttribute(attr);
            }
        }

        // internal for testing
        internal static bool StringAlternativeHasNoText(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return true;
            }
            var strippedString = s.Replace("<p>", string.Empty)
                .Replace("</p>", string.Empty)
                .Replace("<p />", string.Empty)
                .Replace("<p/>", string.Empty)
                .Replace("<br>", string.Empty)
                .Replace("</br>", string.Empty)
                .Replace("<br />", string.Empty)
                .Replace("<br/>", string.Empty);
            return string.IsNullOrWhiteSpace(strippedString);
        }

        /// <summary>
        /// Xmatter pages sometimes have data-* attributes that can be changed by the user.
        /// For example, setting background music. This method sets those attributes from the given dataset into the dom.
        /// </summary>
        /// <param name="element">Could be the xmatter page element or the related div inside the data div</param>
        private void UpdateXmatterPageDataAttributeSets(DataSet dataset, SafeXmlElement element)
        {
            var xmatterPageKey = element.GetAttribute(kDataXmatterPage).Trim();
            ISet<KeyValuePair<string, string>> attributes;
            if (!dataset.XmatterPageDataAttributeSets.TryGetValue(xmatterPageKey, out attributes))
                return;
            foreach (var attribute in attributes)
                element.SetAttribute(attribute.Key, attribute.Value);
        }

        /// <summary>
        /// some templates have a <label></label> element that javascript turns into a bubble describing the field
        /// these labels are temporary, so the go away and are not saved to data-book. But when we then take
        /// an xmatter template page and replace the contents with what was in data-book, we don't want to clobber
        /// the <label></label> elements that are already in there. BL-3078
        /// </summary>
        /// <param name="key"></param>
        /// <param name="node"></param>
        /// <param name="form"></param>
        private void SetInnerXmlPreservingLabel(string key, SafeXmlElement node, XmlString form)
        {
            var labelElement = node.SelectSingleNode("label");
            SetNodeXml(key, form, node);
            if (labelElement != null)
                node.AppendChild(labelElement);
        }

        /// <summary>
        /// Given a node in the content section of the book that has a data-book attribute, see
        /// if this node holds an image and if so, look up the url of the image from the supplied
        /// dataset and stick it in there. Handle both img elements and divs that have a
        /// background-image in an inline style attribute.
        ///
        /// At the time of this writing, the only image that is handled here is the cover page.
        /// The URLs of images in the content of the book are not known to the data-div.
        /// But each time the book is loaded up, we collect up data from the xmatter and stick
        /// it in the data-div(in the DOM) / dataSet (in an object here in code), stick in a
        /// blank xmatter, then push the values back into the xmatter.
        /// </summary>
        /// <returns>true if this node is an image holder of some sort.</returns>
        internal bool UpdateImageFromDataSet(DataSet data, SafeXmlElement node, string key)
        {
            if (!HtmlDom.IsImgOrSomethingWithBackgroundImage(node))
                return false;
            var variable = data.TextVariables[key];
            var getFirstAlt = variable.TextAlternatives.GetFirstAlternative();
            var otherAttributes = variable.GetAttributeList("*");
            // Make sure we don't re-encode the new image url.
            var newImageUrl = KeysOfVariablesThatAreUrlEncoded.Contains(key)
                ? UrlPathString.CreateFromUrlEncodedString(getFirstAlt)
                : UrlPathString.CreateFromHtmlXmlEncodedString(getFirstAlt);
            var oldImageUrl = HtmlDom.GetImageElementUrl(node);
            var imgOrDivWithBackgroundImage = node;
            HtmlDom.SetImageElementUrl(imgOrDivWithBackgroundImage, newImageUrl);
            // Attaching the onerror handler in javascript is too late for the cover page
            // image.  The image resource load fails before the bootstrap code is called
            // in document.ready().  This minimal code sets a class that is used by CSS and
            // javascript to make things look right.  The class is not copied into the
            // new xmatter page, so it gets set afresh each time the book is opened for
            // editing.  See BL-14241.
            // Any change to the value of the onerror attribute here should be reflected
            // in the javascript code in bloomImages.ts/HandleImageError().
            if (imgOrDivWithBackgroundImage.Name == "img")
                imgOrDivWithBackgroundImage.SetAttribute(
                    "onerror",
                    "this.classList.add('bloom-imageLoadError')"
                );

            if (_updateImgNode != null && !newImageUrl.Equals(oldImageUrl))
            {
                try
                {
                    _updateImgNode(node);
                }
                catch (TagLib.CorruptFileException e)
                {
                    NonFatalProblem.Report(
                        ModalIf.Beta,
                        PassiveIf.All,
                        "Problem reading image metadata",
                        newImageUrl.NotEncoded,
                        e
                    );
                    return false;
                }
            }

            // Historically, we've gone back and forth about putting width/height on images. Normally if we find this,
            // we want to remove it because we now use object-fit:contain instead. However in some styling cases (at least border),
            // that wasn't sufficient so we need to keep using width/height. We indicate that we're in this situation with this
            // class on the parent imageContainer. See BL-9460

            // Also, if we're restoring a cropped (or potentially cropped) image, we need to keep the style;
            // this is indicated by finding special attributes used to contain data about a background image
            // represented as a canvas element.
            var backgroundImgTupleNames = HtmlDom.BackgroundImgTupleNames;
            var backgroundImgValues = new string[backgroundImgTupleNames.Length];
            for (var i = 0; i < backgroundImgTupleNames.Length; i++)
            {
                backgroundImgValues[i] = otherAttributes
                    ?.Find(x => x.Item1 == backgroundImgTupleNames[i])
                    ?.Item2.Unencoded;
            }

            var hasBackgroundImgData = backgroundImgValues.All(x => x != null);

            // Note that these attributes were already run through the _attributesNotToCopy filter, which wipes out the ones
            // we don't ever want restored. The style attribute is special, for a series of historical reasons,
            // and the BackgroundImgTupleNames ones are kept so we can use them below, but not copied back to the img.
            otherAttributes
                ?.Where(a => a.Item1 != "src" && !backgroundImgTupleNames.Contains(a.Item1))
                .ForEach(a =>
                {
                    if (
                        a.Item1 == "style"
                        && (
                            node.ParentNode
                                .GetOptionalStringAttribute("class", "")
                                .Contains("bloom-scale-with-code") || hasBackgroundImgData
                        )
                    )
                    {
                        imgOrDivWithBackgroundImage.SetAttribute(a.Item1, a.Item2.Unencoded);
                    }
                });

            // If we find data related to a background image, restore that whole structure.
            if (hasBackgroundImgData)
            {
                HtmlDom.ReconstructBackgroundImgWrapper(node, backgroundImgValues);
            }
            return true;
        }

        /// <summary>
        /// In some cases, we're better off copying from another national language than leaving the field empty.
        /// </summary>
        /// <remarks>
        ///	This is a tough decision. Without this, if we have, say, an English Contributors list but English isn't the N1 (L2), then the
        /// book won't show it at all. An ideal solution would just order them and then "display the first non-empty one", but that would require some java script... not
        /// something could be readily done in CSS, far as I can think.
        /// For now, I *think* this won't do any harm, and if it does, it's adding data, not losing it. Users had complained about "losing" the contributor data before.
        ///</remarks>
        private string PossiblyCopyFromAnotherLanguage(
            SafeXmlElement element,
            string languageCodeOfTargetField,
            DataSet data,
            string key
        )
        {
            string classes = element.GetAttribute("class");

            if (!classes.Contains("bloom-copyFromOtherLanguageIfNecessary"))
            {
                return "";
            }

            // If the user explicitly deletes the field after it's been copied, we should respect that.
            // See BL-13779.
            string userDeleted = element.GetAttribute("data-user-deleted");
            if (userDeleted == "true")
            {
                return "";
            }

            LanguageForm formToCopyFromSinceOursIsMissing = null;
            string s = "";

            if (
                languageCodeOfTargetField == MetadataLanguage1Tag
                || //is it a national language?
                // languageCodeOfTargetField == Language3Tag) || we had this before but don't think we need it.
                //this one is a kludge as we clearly have this case of a vernacular field that people have used
                //to hold stuff that should be copied to every shell. So we can either remove the restriction of the
                //first two clauses in this if statement, or add another bloom-___ class in order to make execptions.
                //Today, I'm not seing the issue clearly enough, so I'm just going to patch this one exising hole.
                classes.Contains("smallCoverCredits")
            )
            {
                formToCopyFromSinceOursIsMissing = GetBestUnwrappedAlternative(
                    data.TextVariables[key].TextAlternatives,
                    new[] { languageCodeOfTargetField, "*", "en", "fr", "es", "pt" }
                );
                if (formToCopyFromSinceOursIsMissing != null)
                    s = formToCopyFromSinceOursIsMissing.Form;

                if (StringAlternativeHasNoText(s))
                {
                    //OK, well even on a non-global language is better than none
                    //s = data.TextVariables[key].TextAlternatives.GetFirstAlternative();
                    formToCopyFromSinceOursIsMissing = GetFirstAlternativeForm(
                        data.TextVariables[key].TextAlternatives
                    );
                    if (formToCopyFromSinceOursIsMissing != null)
                        s = formToCopyFromSinceOursIsMissing.Form;
                }
            }

            /* this was a fine idea, execpt that if the user then edits it, well, it's not borrowed anymore but we'll still have this sitting there misleading us
                                //record our dubious deed for posterity
                                if (formToCopyFromSinceOursIsMissing != null)
                                {
                                    node.SetAttribute("bloom-languageBloomHadToCopyFrom",
                                                      formToCopyFromSinceOursIsMissing.WritingSystemId);
                                }
                                 */
            // Prevent a duplicate (audio) id from being created.
            return HtmlDom.ReplaceAllIdValues(s);
        }

        private LanguageForm GetBestUnwrappedAlternative(
            MultiTextBase textAlternatives,
            IEnumerable<string> languagesToTry
        )
        {
            var allForms = textAlternatives.GetOrderedAndFilteredForms(languagesToTry);
            return allForms.FirstOrDefault(
                harderWay => !StringAlternativeHasNoText(harderWay.Form)
            );
        }

        private LanguageForm GetFirstAlternativeForm(MultiTextBase alternatives)
        {
            foreach (LanguageForm form in alternatives.Forms)
            {
                var trimmedForm = form.Form.Trim();
                if (trimmedForm.Length > 0 && !StringAlternativeHasNoText(trimmedForm))
                {
                    return form;
                }
            }
            return null;
        }

        private void RemoveDataDivElementIfEmptyValue(string key, XmlString value)
        {
            if (XmlString.IsNullOrEmpty(value))
            {
                foreach (
                    SafeXmlElement node in _dom.SafeSelectNodes(
                        "//div[@id='bloomDataDiv']//div[@data-book='" + key + "']"
                    )
                )
                {
                    node.ParentNode.RemoveChild(node);
                }
            }
        }

        /// <summary>
        /// Returns the value of the specified {key} for the specified {writingSystem}
        /// </summary>
        /// <returns>An non-null XmlString representing the value. (The underlying data may be null, but the wrapper will be non-null)</returns>
        public XmlString GetVariableOrNull(string key, string writingSystem)
        {
            var f = _dataset.TextVariables.ContainsKey(key)
                ? _dataset.TextVariables[key].TextAlternatives[writingSystem]
                : null;

            if (string.IsNullOrEmpty(f)) //the TextAlternatives thing gives "", whereas we want null
                f = null; // FYI, wrapping null like this rather than directly returning null will make our callers' lives simpler.

            return XmlString.FromXml(f);
        }

        /// <summary>
        /// Looks up the value of a data attribute associated with a particular xmatter page.
        /// For example, you may want to find the value of the "data-backgroundaudio" attribute of the "frontCover":
        ///		GetXmatterPageDataAttributeValue("frontCover", "data-backgroundaudio")
        /// </summary>
        public string GetXmatterPageDataAttributeValue(string xmatterPageKey, string attributeName)
        {
            ISet<KeyValuePair<string, string>> attributeSet;
            if (
                !_dataset.XmatterPageDataAttributeSets.TryGetValue(xmatterPageKey, out attributeSet)
            )
                return null;
            var attributeKvp = attributeSet.SingleOrDefault(kvp => kvp.Key == attributeName);
            return attributeKvp.Equals(default(KeyValuePair<string, string>))
                ? null
                : attributeKvp.Value;
        }

        public MultiTextBase GetMultiTextVariableOrEmpty(string key)
        {
            return _dataset.TextVariables.ContainsKey(key)
                ? _dataset.TextVariables[key].TextAlternatives
                : new MultiTextBase();
        }

        private static void SendDataToDebugConsole(DataSet data)
        {
#if DEBUG
            foreach (var item in data.TextVariables)
            {
                foreach (LanguageForm form in item.Value.TextAlternatives.Forms)
                {
                    Debug.WriteLine(
                        "Gathered: {0}[{1}]={2}",
                        item.Key,
                        form.WritingSystemId,
                        form.Form
                    );
                }
            }
#endif
        }

        private void UpdateTitle(BookInfo info = null)
        {
            DataSetElementValue title;
            if (_dataset.TextVariables.TryGetValue("bookTitleTemplate", out title))
            {
                //NB: In seleting from an ordered shopping list of priority entries, this is only
                //handling a scenario where a single (title,writingsystem) pair is of interest.
                //That's all we've needed thusfar. But we could imagine needing to work through each one.

                var form = title.TextAlternatives.GetBestAlternative(WritingSystemIdsToTry);

                //allow the title to be a template that pulls in data variables, e.g. "P1 Primer Term{book.term} Week {book.week}"
                foreach (var dataItem in _dataset.TextVariables)
                {
                    form.Form = form.Form.Replace(
                        "{" + dataItem.Key + "}",
                        dataItem.Value.TextAlternatives.GetBestAlternativeString(
                            WritingSystemIdsToTry
                        )
                    );
                }

                _dom.Title = form.Form;
                if (info != null)
                    info.Title = form.Form.Replace("<br />", ""); // Clean out breaks inserted at newlines.

                this.Set("bookTitle", XmlString.FromXml(form.Form), form.WritingSystemId);
            }
            else if (_dataset.TextVariables.TryGetValue("bookTitle", out title))
            {
                var t = title.TextAlternatives.GetBestAlternativeString(WritingSystemIdsToTry);
                _dom.Title = t;
                if (info != null)
                {
                    info.Title = TextOfInnerHtml(t.Replace("<br />", "")); // Clean out breaks inserted at newlines.
                    // Now build the AllTitles field
                    var sb = new StringBuilder();
                    sb.Append("{");
                    foreach (var langForm in title.TextAlternatives.Forms)
                    {
                        if (sb.Length > 1)
                            sb.Append(",");
                        sb.Append("\"");
                        sb.Append(langForm.WritingSystemId);
                        sb.Append("\":\"");
                        sb.Append(
                            TextOfInnerHtml(langForm.Form)
                                .Replace("\\", "\\\\")
                                .Replace("\"", "\\\"")
                        ); // Escape backslash and double-quote
                        sb.Append("\"");
                    }
                    sb.Append("}");
                    info.AllTitles = sb.ToString();
                }
            }
            else
            {
                _dom.Title = "";
            }

            if (BookIsDerivative())
            {
                // We just need to make the info, if any, consistent wit the bookdata
                if (info != null)
                {
                    string encodedTitle = GetVariableOrNull("originalTitle", "*").Xml;
                    info.OriginalTitle = HttpUtility.HtmlDecode(encodedTitle);
                }
            }
            else
            {
                string innerHtml = title?.TextAlternatives.GetExactAlternative(Language1.Tag);
                string innerText = BookData.TextOfInnerHtml(innerHtml); // Notably, also removes the markup

                // Note: Even though originalTitle.Xml and innerHtml are both encoded,
                // they are NOT equivalent. InnerHtml could contain nested markup (like paragraph tags),
                // but for originalTitle we want that markup removed.  So, don't try to do XmlString.FromXml(innerHtml)
                XmlString originalTitle = XmlString.FromUnencoded(innerText);

                // Although _dataset.TextVariables stores ENCODED strings in its inner workings,
                // info.OriginalTitle is expected to contain a DECODED string
                _dataset.UpdateGenericLanguageString("originalTitle", originalTitle, false);
                UpdateSingleTextVariableInDataDiv(
                    "originalTitle",
                    _dataset.TextVariables["originalTitle"]
                );
                if (info != null)
                {
                    info.OriginalTitle = originalTitle.Unencoded;
                }
            }
        }

        /// <summary>
        /// The data we extract into title fields of _dataSet is the InnerXml of some XML node.
        /// This might have markup, e.g., making a word italic. It will also have the amp, lt, and gt escaped.
        /// We want to reduce it to plain text to store in bookInfo.
        /// </summary>
        /// <param name="input">Must be XHTML-encoded.</param>
        /// <returns></returns>
        internal static string TextOfInnerHtml(string input)
        {
            var text = Book.RemoveHtmlMarkup(
                input,
                Book.LineBreakSpanConversionMode.ToSimpleNewline
            );

            // Leading and trailing whitespace are undesirable for the title even if the user has
            // put them in for some strange reason.  (BL-7558)
            return text?.Trim();
        }

        private string[] WritingSystemIdsToTry
        {
            get
            {
                var langs = new List<string>();
                langs.AddRange(GetAllBookLanguageCodes());
                foreach (var code in new[] { "en", "fr", "th", "pt", "*" })
                {
                    if (!langs.Any(s => s == code))
                        langs.Add(code);
                }
                return langs.ToArray();
            }
        }

        public void SetMultilingualContentLanguages(params string[] contentLanguages)
        {
            foreach (var lang in contentLanguages)
            {
                GetWritingSystem(lang); // throws if not valid collection language
            }
            var language2Code = contentLanguages.Length > 1 ? contentLanguages[1] : null;
            var language3Code = contentLanguages.Length > 2 ? contentLanguages[2] : null;

            Set("contentLanguage1", XmlString.FromUnencoded(contentLanguages[0]), false);
            Set("contentLanguage2", XmlString.FromUnencoded(language2Code), false);
            Set("contentLanguage3", XmlString.FromUnencoded(language3Code), false);
        }

        //        public IEnumerable<KeyValuePair<string,DataSetElementValue>>  GetCollectionVariables()
        //        {
        //            return from v in this._dataset.TextVariables where v.Value.IsCollectionValue select v;
        //        }

        /// <summary>
        /// Find the settings file for the specified branding (in unit tests, this may be a
        /// path to a temporary folder). If it exists and parses as valid JSON and contains
        /// a Presets section, use that to fill in any data-div elements which it
        /// specifies and which are currently empty.
        /// </summary>
        /// <param name="brandingNameOrPath"></param>
        public void MergeBrandingSettings(string brandingNameOrPath)
        {
            // clear out any previous values, because if they've changed the branding
            // (or moved this book to another collection), the current branding might
            // not have something to overwrite what was there, and we don't want the
            // old stuff hanging around.
            foreach (var key in _dataset.TextVariables.Keys.ToArray())
            {
                if (key.ToLowerInvariant().Contains("branding"))
                {
                    RemoveAllForms(key);
                }
            }

            var settings = BrandingSettings.GetSettingsOrNull(brandingNameOrPath);

            if (settings != null && settings.Presets != null)
            {
                // Determine this BEFORE we possibly start setting some of them!
                var allCopyrightEmpty = true;
                foreach (var setting in BookCopyrightAndLicense.SettingsToCheckForDefaultCopyright)
                {
                    if (!string.IsNullOrWhiteSpace(GetVariableOrNull(setting, "*").Xml))
                    {
                        allCopyrightEmpty = false;
                        break;
                    }
                }

                foreach (var item in settings.Presets)
                {
                    if (
                        string.IsNullOrWhiteSpace(item.DataBook)
                        || string.IsNullOrWhiteSpace(item.Lang)
                        || string.IsNullOrWhiteSpace(item.Condition)
                    )
                        continue;

                    // Allow licenseUrl to be set to nothing. Otherwise, there is no way for branding to set a custom license.
                    if (string.IsNullOrWhiteSpace(item.Content) && item.DataBook != "licenseUrl")
                        continue;

                    // In some places we might need to worry about content that looks empty but contains
                    // things like <br /> or empty <p> elements. But our data-div maintenance code
                    // seems to already eliminate anything that looks empty like that.
                    switch (item.Condition)
                    {
                        case "ifEmpty":
                            if (
                                !string.IsNullOrWhiteSpace(
                                    GetVariableOrNull(item.DataBook, item.Lang).Xml
                                )
                            )
                                continue;
                            break;
                        case "ifAllCopyrightEmpty":
                            if (!allCopyrightEmpty)
                                continue;
                            break;
                        case "always":
                            break;
                        default: // any condition we don't recognize, don't do it.
                            continue;
                    }

                    if (item.DataBook == "copyright")
                    {
                        var presetContainsMoreThanPublisher = item.Content
                            .SplitTrimmed(' ')
                            .Any(
                                word =>
                                    word == "©"
                                    || word.ToLowerInvariant() == "copyright"
                                    || word.StartsWith("20")
                            );
                        if (!presetContainsMoreThanPublisher)
                        {
                            // if there is nothing in the copyright field
                            if (string.IsNullOrWhiteSpace(GetVariableOrNull("copyright", "*").Xml))
                            {
                                item.Content =
                                    "Copyright © "
                                    + DateTime.Now.Year.ToString()
                                    + " "
                                    + item.Content;
                            }
                            else
                                continue;
                        }
                    }

                    // I'm not clear if this ever is needed anymore, since we now
                    // do the flavor replacement when reading the json. I can't
                    // rule out that there aren't other values that also have {flavor}
                    var content = item.Content.Replace(
                        "{flavor}",
                        CollectionSettings.GetBrandingFlavor()
                    );
                    if (content.Contains("{bookshelfUrlKey}"))
                    {
                        // If the bookshelf isn't set, then don't incorporate the branding image display for it.  See BL-10451.
                        if (
                            String.IsNullOrEmpty(CollectionSettings.DefaultBookshelf)
                            && content.Contains("src='{bookshelfUrlKey}.svg'")
                        )
                            content = "";
                        else
                            content = content.Replace(
                                "{bookshelfUrlKey}",
                                CollectionSettings.DefaultBookshelf
                            );
                    }

                    content = MergeInPersonalization(content);

                    Set(item.DataBook, XmlString.FromXml(content), item.Lang);
                }
            }
        }

        /// <summary>
        /// If we have "{personalization}" in the subscription's HTML template, fill in the
        /// personalization from the subscription code.  If the personalization is empty,
        /// throw an exception because it should always exist if it's in the template.
        /// </summary>
        private string MergeInPersonalization(string content)
        {
            // if the CollectionSettings has a Subscription Personalization, replace any instances of
            // {personalization} with that value.
            if (content.Contains("{personalization}"))
            {
                var personalization = this.CollectionSettings.Subscription.Personalization;
                if (string.IsNullOrEmpty(personalization))
                {
                    throw new ApplicationException(
                        "Branding personalization is not set, but the branding template contains {personalization}."
                    );
                }
                return content.Replace("{personalization}", personalization);
            }

            return content;
        }

        /// <summary>
        /// At least one of these should exist if the book is a derivative, at least after the call to
        /// BookStarter.SetOriginalCopyrightAndLicense() which initializes all of them, since we don't allow a
        /// book to have no license, nor to be uploaded without copyright...unless of course it was derived
        /// before 3.9, when we started doing this. In that case the best we can do is for SetOriginalCopyrightAndLicense
        /// to record the best information we have from the source book. But for other purposes, we can regard
        /// a book as a derivative if it has one of these fields.
        /// </summary>
        public bool BookIsDerivative()
        {
            return GetMultiTextVariableOrEmpty("originalLicenseUrl").Count > 0
                || GetMultiTextVariableOrEmpty("originalLicenseNotes").Count > 0
                || GetMultiTextVariableOrEmpty("originalCopyright").Count > 0;
        }

        /// <summary>
        /// Get the indicated language.
        /// </summary>
        /// <remarks>
        /// This is the crucial method for book-specific language setting.
        /// Phase 0 defers to the collection settings.
        /// NB: for tests or other places where HtmlDom is empty or null, this method should still
        /// defer to the collection settings values.
        /// </remarks>
        public WritingSystem GetLanguage(LanguageSlot slot)
        {
            switch (slot)
            {
                case LanguageSlot.Language1:
                    return GetWritingSystem(Language1Tag);
                case LanguageSlot.Language2:
                    return Language2Tag == null ? null : GetWritingSystem(Language2Tag);
                case LanguageSlot.Language3:
                    return Language3Tag == null ? null : GetWritingSystem(Language3Tag);
                case LanguageSlot.MetadataLanguage1:
                    return GetWritingSystem(MetadataLanguage1Tag);
                case LanguageSlot.MetadataLanguage2:
                    return GetWritingSystem(MetadataLanguage2Tag);
                case LanguageSlot.SignLanguage:
                    return GetWritingSystem(SignLanguageTag);
                default:
                    throw new ArgumentException(
                        "BookData.GetLanguage() cannot handle that slot yet"
                    );
            }
        }

        // simple synonyms for the most common languages used
        public WritingSystem Language1 => GetLanguage(LanguageSlot.Language1);
        public WritingSystem Language2 => GetLanguage(LanguageSlot.Language2);
        public WritingSystem Language3 => GetLanguage(LanguageSlot.Language3);

        public WritingSystem MetadataLanguage1 => GetLanguage(LanguageSlot.MetadataLanguage1);
        public WritingSystem MetadataLanguage2 => GetLanguage(LanguageSlot.MetadataLanguage2);
        public WritingSystem SignLanguage => GetLanguage(LanguageSlot.SignLanguage);

        /// <summary>
        /// Get one of the collection language objects that matches the language tag.
        /// Crash if not found.
        /// </summary>
        public WritingSystem GetWritingSystem(string langTag)
        {
            var result = GetWritingSystemOrNull(langTag);
            if (result == null)
                throw new ApplicationException(
                    $"Trying to get language not in collection: {langTag}"
                );
            return result;
        }

        /// <summary>
        /// Get one of the collection language objects that matches the language tag, or null if it doesn't match.
        /// </summary>
        private WritingSystem GetWritingSystemOrNull(string langTag)
        {
            if (CollectionSettings.Language1.Tag == langTag)
                return CollectionSettings.Language1;
            if (CollectionSettings.Language2?.Tag == langTag)
                return CollectionSettings.Language2;
            if (CollectionSettings.Language3?.Tag == langTag)
                return CollectionSettings.Language3;
            if (CollectionSettings.SignLanguage?.Tag == langTag)
                return CollectionSettings.SignLanguage;
            return null;
        }

        /// <summary>
        /// Returns an ordered list of distinct languages actively used in this book (from L1,
        /// L2, L3, and M1).  Note that L1 and M1 are always defined, and L2 and L3 may or may not be
        /// defined.  The returned list will have 1, 2, or 3 items in it.  L1 will always be
        /// the first language, unless includeLanguage1 is set false.  Then M1 will be the first
        /// language (even if it is the same as L1).
        /// </summary>
        /// <remarks>
        /// Places where GetBasicBookLanguages is used should be examined to see if GetAllBookLanguages
        /// should be used instead.  It's conceivable in the long run that this method may or may not
        /// be needed.
        /// </remarks>
        public List<WritingSystem> GetBasicBookLanguages(
            bool includeLanguage1 = true,
            bool includeSignLanguage = false
        )
        {
            return GetBasicBookLanguageCodes(includeLanguage1, includeSignLanguage)
                .Select(c => GetWritingSystem(c))
                .ToList();
        }

        /// <summary>
        /// Returns an ordered list of codes of distinct languages actively used in this book (from L1,
        /// L2, L3, and M1).  Note that L1 and M1 are always defined, and L2 and L3 may or may not be
        /// defined.  The returned list will have 1, 2, or 3 items in it.  L1 will always be
        /// the first language, unless includeLanguage1 is set false.  Then M1 will be the first
        /// language (even if it is the same as L1).
        /// </summary>
        /// <remarks>
        /// Places where GetBasicBookLanguageCodes is used should be examined to see if
        /// GetAllBookLanguageCodes should be used instead.  It's conceivable in the long run
        /// that this method may or may not be needed.
        /// </remarks>
        public List<string> GetBasicBookLanguageCodes(
            bool includeLanguage1 = true,
            bool includeSignLanguage = false,
            bool includeMetaData2 = false
        )
        {
            var langCodes = new List<string>();
            if (includeLanguage1)
                langCodes.Add(Language1Tag);
            if (includeSignLanguage)
                AddLang(langCodes, SignLanguageTag);
            AddLang(langCodes, MetadataLanguage1Tag);
            AddLang(langCodes, Language2Tag);
            AddLang(langCodes, Language3Tag);
            if (
                includeMetaData2
                && !string.IsNullOrEmpty(MetadataLanguage2Tag)
                && !langCodes.Contains(MetadataLanguage2Tag)
            )
                langCodes.Add(MetadataLanguage2Tag);
            return langCodes;
        }

        private void AddLang(List<string> langs, string ws)
        {
            if (string.IsNullOrEmpty(ws))
                return;
            if (langs.Any(l => l == ws))
                return;
            langs.Add(ws);
        }

        /// <summary>
        /// Return an ordered list of distinct languages used in this book.
        /// L1 will always be first, following by L2 if different, and then L3 if it exists and
        /// is different.  The order of any remainng languages is (at the moment) undefined.
        /// L1 will always be the first language, unless includeLanguage1 is set false.  Then
        /// L2 will be the first language (even if it is the same as L1).
        /// </summary>
        /// <remarks>
        /// This method obviously will need to be worked on...
        /// </remarks>
        public List<WritingSystem> GetAllBookLanguages(
            bool includeLanguage1 = true,
            bool includeSignLanguage = false
        )
        {
            return GetBasicBookLanguages(includeLanguage1, includeSignLanguage); // until we get a better list to work with...
        }

        /// <summary>
        /// Return an ordered list of codes of distinct languages used in this book.
        /// Eventually we may want to add a param for including sign language; for now, none of the callers want it,
        /// so it is just excluded.
        /// L1 will always be first, following by L2 if different, and then L3 if it exists and
        /// is different.  The order of any remainng languages is (at the moment) undefined.
        /// L1 will always be the first language, unless includeLanguage1 is set false.  Then
        /// L2 will be the first language (even if it is the same as L1).
        /// </summary>
        /// <remarks>
        /// This method obviously will need to be worked on...
        /// </remarks>
        public List<string> GetAllBookLanguageCodes(
            bool includeLanguage1 = true,
            bool includeMetaData2 = false
        )
        {
            return GetBasicBookLanguageCodes(includeLanguage1, false, includeMetaData2); // until we get a better list to work with...
        }

        /// <summary>
        /// Given a choice, what language should we use to display text on the page
        /// (not in the UI, which is controlled by the UI Language).
        /// Setting includeLang1 to false is currently used when localizing FullOriginalCopyrightLicenseSentence,
        /// and the "comma,space" string used to separate things in a list. It's not clear to me why we'd want to
        /// exclude L1 in these cases, whether we mean the book L1 or the collection L1 now that they can be
        /// different. Possibly the idea is that it's unlikely a localization is available in a vernacular
        /// language, but that would be true of all strings, and it's only unlikely; the vernacular language
        /// of a collection can be a major language. Possibly the idea is that the string we are trying to
        /// localize is normally in ML1, so that should be what we try first? For now I'm guessing that it's
        /// best to omit both the book L1 and the collection L1 (unless they are included for another reason).
        /// </summary>
        /// <returns>A prioritized enumerable of language codes</returns>
        public IEnumerable<string> GetLanguagePrioritiesForLocalizedTextOnPage(
            bool includeLang1 = true
        )
        {
            var langCodes = new List<string>();
            // The .Where is needed for various unit tests as a minimum.
            langCodes.AddRange(
                GetBasicBookLanguageCodes(includeLang1).Where(lc => !string.IsNullOrWhiteSpace(lc))
            );
            // Try any collection settings we don't already have...but not the first if we're excluding vernacular
            if (includeLang1)
                AddLang(langCodes, CollectionSettings.Language1Tag);
            AddLang(langCodes, CollectionSettings.Language2Tag);
            AddLang(langCodes, CollectionSettings.Language3Tag);
            if (!langCodes.Contains("en"))
                langCodes.Add("en");

            // reverse-order loop so that given e.g. zh-Hans followed by zh-Hant we insert zh-CN after the second one.
            // That is, we'll prefer either of the explicit variants to the fall-back.
            // The part before the hyphen (if there is one) is the main language.
            var count = langCodes.Count;
            for (int i = count - 1; i >= 0; i--)
            {
                var fullLangTag = langCodes[i];
                if (fullLangTag == null)
                    continue;
                var language = fullLangTag.Split('-')[0]; // Generally insert corresponding language for longer culture
                if (language == "zh")
                {
                    language = "zh-CN"; // Insert this instead for Chinese
                }

                if (langCodes.IndexOf(language) >= 0)
                    continue;
                langCodes.Insert(i + 1, language);
            }

            var pashtoLanguages = new[] { "ps", "pus", "pbu", "pst", "pbt" };
            // As of Jun 2020, we have Crowdin translation for pbu,
            // but that seems like a mistake. Some day, we should change it
            // to the macrolanguage which is ps (2-letter) or pus (3-letter).
            // So, when we have any of the Pashto codes above, look for a
            // translation in one of the codes below.
            var tryTheseLanguageCodes = new[] { "pbu", "ps", "pus" };
            for (int i = 0; i < langCodes.Count; i++)
            {
                if (pashtoLanguages.Contains(langCodes[i]))
                {
                    langCodes.InsertRange(
                        i + 1,
                        tryTheseLanguageCodes.Where(lc => !langCodes.Contains(lc))
                    );
                    break;
                }
            }
            return langCodes;
        }

        internal LanguageDescriptor[] MakeLanguageUploadData(string[] langTags)
        {
            var result = new LanguageDescriptor[langTags.Length];
            var bookLangs = GetBasicBookLanguages(true, true);
            for (int i = 0; i < langTags.Length; i++)
            {
                var code = langTags[i];
                string name = null;
                var lang = bookLangs.FirstOrDefault(ws => ws.Tag == code);
                if (lang != null)
                    name = lang.Name;
                else
                    IetfLanguageTag.GetBestLanguageName(code, out name);
                string ethCode;
                LanguageSubtag data;
                if (!StandardSubtags.RegisteredLanguages.TryGet(code.ToLowerInvariant(), out data))
                    ethCode = code;
                else
                {
                    ethCode = data.Iso3Code;
                    if (string.IsNullOrEmpty(ethCode))
                        ethCode = code;
                }
                result[i] = new LanguageDescriptor()
                {
                    LangTag = code,
                    Name = name,
                    EthnologueCode = ethCode
                };
            }
            return result;
        }
    }
}
