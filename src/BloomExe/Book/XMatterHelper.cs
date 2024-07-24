using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Collection;
using Bloom.SafeXml;
using L10NSharp;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;
using SIL.Xml;

namespace Bloom.Book
{
    /// <summary>
    /// Handles chores related to Front and Back Matter packs.
    /// These allow users to have a set which fits there country/organizational situation, and have it applied
    /// to all books they create, whether original or from shells created in other country/organization contexts.
    /// </summary>
    public class XMatterHelper
    {
        private readonly HtmlDom _bookDom;
        private readonly string _nameOfXMatterPack;

        /// <summary>
        /// Constructs by finding the file and folder of the xmatter pack, given the its key name e.g. "Factory", "SILIndonesia".
        /// The default key name is provided as a method parameter, but that can be overridden by a value from inside the book.
        /// The name of the file should be (key)-XMatter.htm. The name and the location of the folder is not our problem...
        /// we leave it to the supplied fileLocator to find it.
        /// </summary>
        /// <param name="bookDom">The book's DOM</param>
        /// <param name="xmatterNameFromCollectionSettings">e.g. "Factory", "SILIndonesia".  This can be overridden inside the bookDom.</param>
        /// <param name="fileLocator">The locator needs to be able tell us the path to an xmatter html file, given its name</param>
        /// <param name="useDeviceVersionIfAvailable">If true, use a pack-specific device xmatter, or "Device" if none found. E.g. ABC => ABC-Device</param>
        public XMatterHelper(
            HtmlDom bookDom,
            string xmatterNameFromCollectionSettings,
            IFileLocator fileLocator,
            bool useDeviceVersionIfAvailable = false
        )
        {
            string directoryPath = null;
            _bookDom = bookDom;
            var bookSpecificXMatterPack = bookDom.GetMetaValue("xmatter", null);
            if (!String.IsNullOrWhiteSpace(bookSpecificXMatterPack))
            {
                bookSpecificXMatterPack = MigrateXMatterName(bookSpecificXMatterPack);
                _nameOfXMatterPack = bookSpecificXMatterPack;
                if (useDeviceVersionIfAvailable)
                    _nameOfXMatterPack = GetBestDeviceXMatterAvailable(
                        _nameOfXMatterPack,
                        fileLocator
                    );
                var errorTemplate = LocalizationManager.GetString(
                    "Errors.XMatterSpecifiedByBookNotFound",
                    "This book called for a Front/Back Matter pack named '{0}', but this version of Bloom does not have it, and Bloom could not find it on this computer. The book has been changed to use the Front/Back Matter pages from the Collection Settings."
                );
                directoryPath = GetXMatterDirectory(
                    _nameOfXMatterPack,
                    fileLocator,
                    String.Format(errorTemplate, bookSpecificXMatterPack),
                    false
                );
                if (directoryPath == null)
                {
                    // Remove the xmatter specification from the DOM since it couldn't be found.
                    _bookDom.RemoveMetaElement("xmatter");
                }
            }
            if (directoryPath == null)
            {
                _nameOfXMatterPack = xmatterNameFromCollectionSettings;
                if (useDeviceVersionIfAvailable)
                    _nameOfXMatterPack = GetBestDeviceXMatterAvailable(
                        _nameOfXMatterPack,
                        fileLocator
                    );
                directoryPath = GetXMatterDirectory(
                    _nameOfXMatterPack,
                    fileLocator,
                    "It should not be possible to get an error here, because the collection verifies its xmatter name in CheckAndFixDependencies()",
                    true
                );
            }
            var htmName = _nameOfXMatterPack + "-XMatter.html";
            PathToXMatterHtml = directoryPath.CombineForPath(htmName);
            if (!RobustFile.Exists(PathToXMatterHtml))
            {
                htmName = _nameOfXMatterPack + "-XMatter.htm"; // pre- Bloom 3.7
                PathToXMatterHtml = directoryPath.CombineForPath(htmName);
            }
            if (!RobustFile.Exists(PathToXMatterHtml))
            {
                ErrorReport.NotifyUserOfProblem(
                    new ShowOncePerSessionBasedOnExactMessagePolicy(),
                    "Could not locate the file {0} in {1} (also checked .html)",
                    htmName,
                    directoryPath
                );
                throw new ApplicationException();
            }
            PathToXMatterStylesheet = directoryPath.CombineForPath(GetStyleSheetFileName());
            PathToXMatterSettings = directoryPath.CombineForPath("xmatter.json");
            if (!RobustFile.Exists(PathToXMatterStylesheet))
            {
                ErrorReport.NotifyUserOfProblem(
                    new ShowOncePerSessionBasedOnExactMessagePolicy(),
                    "Could not locate the file {0} in {1}",
                    GetStyleSheetFileName(),
                    directoryPath
                );
                throw new ApplicationException();
            }
            XMatterDom = XmlHtmlConverter.GetXmlDomFromHtmlFile(PathToXMatterHtml, false);
        }

        public static string GetBestDeviceXMatterAvailable(
            string xmatterName,
            IFileLocator fileLocator
        )
        {
            if (xmatterName.EndsWith("Device"))
                return xmatterName;

            // Look to see if there is a special Device version of this xmatter
            var deviceXmatterName = $"{xmatterName}-Device";
            var directoryPath = GetXMatterDirectory(
                deviceXmatterName,
                fileLocator,
                null,
                false,
                true
            );
            if (directoryPath != null)
                return deviceXmatterName;

            // Look in the stylesheet and see if it already handles device layout
            try
            {
                var plainXmatterDirectory = GetXMatterDirectory(
                    xmatterName,
                    fileLocator,
                    null,
                    false,
                    true
                );
                if (plainXmatterDirectory != null)
                {
                    var cssPath = Path.Combine(
                        plainXmatterDirectory,
                        GetStyleSheetFileName(xmatterName)
                    );
                    if (RobustFile.ReadAllText(cssPath).Contains(".Device16x9"))
                        return xmatterName;
                }
            }
            catch (Exception)
            {
                // swallow and fall back to dedicated device xmatter
            }

            // use the default Device xmatter which is just named "Device"
            return "Device";
        }

        public static string GetXMatterDirectory(
            string nameOfXMatterPack,
            IFileLocator fileLocator,
            string errorMsg,
            bool throwIfError,
            bool silent = false
        )
        {
            var directoryName = nameOfXMatterPack + "-XMatter";

            if (Program.RunningHarvesterMode)
            {
                // Get a new file locator that also searches the Custom XMatter directory.
                // This allows the Harvseter to preserve custom branding if those books are uploaded to web. (See BL-BL-9084)
                var extraSearchPaths = new string[]
                {
                    BloomFileLocator.GetCustomXMatterDirectory()
                };
                fileLocator = fileLocator.CloneAndCustomize(extraSearchPaths);
            }

            if (silent)
            {
                // Using LocateDirectoryWithThrow is quite expensive for directories we don't find...the Exception it creates, which we don't use,
                // includes a concatenation of a long list of paths it searched in. (It's quite common now to search for an xmatter directory
                // we don't often find, such as looking for one called Traditional-Device when publishing something with Traditional xmatter
                // on a device.
                try
                {
                    var result = fileLocator.LocateDirectory(directoryName);
                    if (result == null || !Directory.Exists(result))
                        return null;
                    return result;
                }
                catch (ApplicationException)
                {
                    return null;
                }
            }
            try
            {
                return fileLocator.LocateDirectoryWithThrow(directoryName);
            }
            catch (ApplicationException error)
            {
                if (silent)
                    return null;
                var frontBackMatterProblem = LocalizationManager.GetString(
                    "Errors.XMatterProblemLabel",
                    "Front/Back Matter Problem",
                    "This shows in the 'toast' that pops up to notify the user of a non-fatal problem."
                );
                NonFatalProblem.Report(
                    ModalIf.None,
                    PassiveIf.All,
                    frontBackMatterProblem,
                    errorMsg,
                    error
                );
                if (throwIfError)
                    throw new ApplicationException(errorMsg);
            }
            return null;
        }

        public string GetStyleSheetFileName()
        {
            return GetStyleSheetFileName(_nameOfXMatterPack);
        }

        private const string kStylesheetSuffix = "-XMatter.css";

        public static string GetStyleSheetFileName(string xmatterName)
        {
            return xmatterName + kStylesheetSuffix;
        }

        /// <summary>
        /// Given an StyleSheet generated by GetStyleSheetFileName(),
        /// reverses the process and finds the xmatterName used to generate the input {filename}
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static string GetXMatterFromStyleSheetFileName(string filename)
        {
            var prefixEndIndex = filename.IndexOf(kStylesheetSuffix);
            if (prefixEndIndex < 0)
            {
                return null;
            }

            string xmatterName = filename.Substring(0, prefixEndIndex);
            return xmatterName;
        }

        /// <summary>
        /// Set this if you want the xmatter stuff copied into a document folder. This makes sense when setting up or
        /// modifying one of the users books, but not when just displaying a book from a shell collection.
        /// </summary>
        public string FolderPathForCopyingXMatterFiles { get; set; }

        /// <summary>
        /// The location of the stylesheet for the selected xmatter pack.
        /// </summary>
        public string PathToXMatterStylesheet { get; set; }

        public string PathToXMatterSettings { get; }

        /// <summary>
        /// This exists in an XMatter-pack folder, which we infer from this file location.
        /// </summary>
        public string PathToXMatterHtml { get; set; }

        /// <summary>
        /// this is separated out to ease unit testing
        /// </summary>
        public SafeXmlDocument XMatterDom { get; set; }

        /// <summary>
        /// Add any default user styles defined in xmatter that are not already defined in the book.
        /// (This allows us to define default formatting for xmatter elements using "user-defined"
        /// styles that the user can modify later. We must not re-insert them after the user has
        /// done so, so we limit the insertion to styles that are not already present.)
        /// </summary>
        public void InjectDefaultUserStylesFromXMatter()
        {
            var xmatterStylesNode = XMatterDom.SelectSingleNode(
                "html/head/" + HtmlDom.UserModifiedStyleXPath
            );
            if (xmatterStylesNode != null)
            {
                var existing = HtmlDom.GetUserModifiedStyleElement(_bookDom.Head);
                if (existing != null)
                {
                    var newMergedUserStyleXml = HtmlDom.MergeUserStylesOnInsertion(
                        existing,
                        xmatterStylesNode
                    );
                    existing.InnerXml = newMergedUserStyleXml;
                }
                else
                {
                    _bookDom.Head.AppendChild(xmatterStylesNode);
                }
            }
        }

        public void InjectXMatter(
            Dictionary<string, string> writingSystemCodes,
            Layout layout,
            bool orderXmatterForDeviceUse,
            string metadataLangTag,
            List<string> oldIds = null
        )
        {
            //don't want to pollute shells with this content
            if (!string.IsNullOrEmpty(FolderPathForCopyingXMatterFiles))
            {
                //copy over any image files used by this front matter
                string path = Path.GetDirectoryName(PathToXMatterHtml);
                foreach (
                    var file in Directory
                        .GetFiles(path, "*.png")
                        .Concat(
                            Directory
                                .GetFiles(path, "*.jpg")
                                .Concat(
                                    Directory
                                        .GetFiles(path, "*.gif")
                                        .Concat(Directory.GetFiles(path, "*.bmp"))
                                )
                        )
                )
                {
                    string destFileName = FolderPathForCopyingXMatterFiles.CombineForPath(
                        Path.GetFileName(file)
                    );
                    Utils.LongPathAware.ThrowIfExceedsMaxPath(destFileName);
                    RobustFile.Copy(file, destFileName, true);
                }
            }

            _bookDom.EnsureStylesheetLinks(Path.GetFileName(PathToXMatterStylesheet));

            var divBeforeNextFrontMatterPage = _bookDom.RawDom.SelectSingleNode(
                "//body/div[@id='bloomDataDiv']"
            );

            foreach (
                SafeXmlElement xmatterPage in XMatterDom.SafeSelectNodes(
                    "/html/body/div[contains(@data-page,'required')]"
                )
            )
            {
                var newPageDiv = _bookDom.RawDom.ImportNode(xmatterPage, true) as SafeXmlElement;
                // If we're updating an existing book, we want to keep the IDs (as much as possible; sometimes
                // the number of xmatter pages changes and we have to add IDs). In this case, oldIds is obtained
                // while running RemoveExistingXMatter.
                // If we're creating a new book, we want to generate new IDs, so things like caches of cover thumbnails
                // by ID don't find the same ID for every book derived from a given template. In that case, we discard the IDs
                // from any removed xmatter pages.
                // If the new xmatter has fewer pages than the old xmatter, some will not be reused. If the current page
                // is one of those, the attempt to refresh the current page will fail (since the expected ID won't be found).
                // Bloom will recover by switching to the first page of the new xmatter, but it will be a bit disconcerting.
                // It may also be a bit disconcerting if, for example, both xmatters have a 'credits' page and that is the
                // current one, but there are more or fewer pages before 'credits' in the new xmatter. We will end up on the
                // page that gets the same ID as the old 'credits' page, but it won't be the same page.
                // We could try harder to, for example, stay on a page that has the same label if there is one, but changing
                // xmatter is a rare operation (except perhaps for testers and developers), so it's probably not worth the effort.
                // We could make things more predictable by always switching to the first page when changing xmatter, but
                // in fact this code is FAR more often used to reinstate the SAME xmatter as part of EnsureUpToDate(), and
                // in such cases we will always have a matching page to stay on.
                var newId = Guid.NewGuid().ToString();
                if (oldIds != null && oldIds.Count > 0)
                {
                    newId = oldIds[0];
                    oldIds.RemoveAt(0);
                }
                newPageDiv.SetAttribute("id", newId);

                // Various fields don't have a useful lang attribute value (doesn't exist or is "*") but we need a lang attribute to set the font properly.
                // By setting it on the page, all those fields can properly inherit the language 2 code and thus use its font. See BL-8545.
                // Since we are only doing this for xmatter, we don't have to worry about the lang attribute staying around when it shouldn't.
                // Old Blooms will effectively remove it when injecting xmatter. New Blooms will always set it to the current language 2 code.
                newPageDiv.SetAttribute("lang", metadataLangTag);

                // We have some xmatters that don't know about devices, and these we replace with the
                // standard Device Xmatter as needed.
                // There is a second type where we have explicitly made a special xmatter just for devices:
                // ABC, Dari, and Pasti are examples of these.
                // Finally, we have other xmatters that deal with device formatting via a stylesheet, without having
                // a second folder and html for devices (e.g. Kyrgyzstan 2020). For this last one, we want to
                // just automatically reorder the pages, when we are preparing the document for publishing
                // to device contexts.
                var moveNonCoverToBack =
                    orderXmatterForDeviceUse && !PathToXMatterStylesheet.Contains("Device-XMatter");
                if (
                    IsBackMatterPage(xmatterPage)
                    || (moveNonCoverToBack && ShouldBeInBackForDeviceUse(xmatterPage))
                )
                {
                    //note: this is redundant unless this is the 1st backmatterpage in the list
                    divBeforeNextFrontMatterPage = _bookDom.RawDom.SelectSingleNode(
                        "//body/div[last()]"
                    );
                }

                //we want the xmatter pages to match what we found in the source book
                SizeAndOrientation.UpdatePageSizeAndOrientationClasses(newPageDiv, layout);

                //any @lang attributes that have a metalanguage code (N1, N2, V) get filled with the actual code.
                //note that this older method is crude, as you're in trouble if the user changes one of those to
                //a different language. Instead, use data-metalanguage.
                foreach (SafeXmlElement node in newPageDiv.SafeSelectNodes("//*[@lang]"))
                {
                    var lang = node.GetAttribute("lang");
                    if (writingSystemCodes.ContainsKey(lang))
                    {
                        node.SetAttribute("lang", writingSystemCodes[lang]);
                    }
                }

                _bookDom.RawDom
                    .SelectSingleNode("//body")
                    .InsertAfter(newPageDiv, divBeforeNextFrontMatterPage);
                divBeforeNextFrontMatterPage = newPageDiv;

                //enhance... this is really ugly. I'm just trying to clear out any remaining "{blah}" left over from the template
                foreach (
                    SafeXmlElement e in newPageDiv.SafeSelectNodes("//*[starts-with(text(),'{')]")
                )
                {
                    foreach (var node in e.ChildNodes)
                    {
                        var t = node as SafeXmlText;
                        if (t != null && t.Value.StartsWith("{"))
                            t.Value = ""; //otherwise html tidy will through away span's (at least) that are empty, so we never get a chance to fill in the values.
                    }
                }
            }
            InjectFlyleafIfNeeded(layout);
        }

        public bool TemporaryDom { get; set; }

        /// <summary>
        /// Some book layouts rely on the first page facing the second page. A Wall Calendar is one example.
        /// Here we check if the first content page has this requirement and, if so, we insert a blank "flyleaf"
        /// page.
        /// </summary>
        private void InjectFlyleafIfNeeded(Layout layout)
        {
            // the "inside" here means "not counting the cover"
            var numberOfFrontMatterPagesInside =
                XMatterDom.SafeSelectNodes("//div[contains(@class,'bloom-frontMatter')]").Length
                - 1;
            var firstPageWouldNotBePartOfASpread = numberOfFrontMatterPagesInside % 2 != 0;

            if (firstPageWouldNotBePartOfASpread)
            {
                var lastFrontMatterPage = _bookDom.SelectSingleNode(
                    "//div[contains(@class,'bloom-frontMatter')][last()]"
                );

                var firstContentPageAndAlsoStartsSpread = _bookDom.SelectSingleNode(
                    "//div[contains(@class,'bloom-frontMatter')][last()]" // last frontmatter page
                        + "/following-sibling::div[contains(@data-page, 'spread-start')]"
                );
                // page after that says it needs to be facing the next page
                if (firstContentPageAndAlsoStartsSpread != null)
                {
                    var flyDom = SafeXmlDocument.Create();
                    flyDom.LoadXml(
                        @"
						<div class='bloom-flyleaf bloom-frontMatter bloom-page' data-page='required singleton'>
							<div class='pageLabel' lang='en'>Flyleaf</div>
                            <div class='marginBox'>
							    <div style='height: 100px; width:100%'
								    data-hint='This page was automatically inserted because the following page is marked as part of a two page spread.'>
							    </div>
                            </div>
						</div>"
                    );
                    var flyleaf =
                        _bookDom.RawDom.ImportNode(flyDom.FirstChild, true) as SafeXmlElement;
                    flyleaf.SetAttribute("id", Guid.NewGuid().ToString());
                    lastFrontMatterPage.ParentNode.InsertAfter(flyleaf, lastFrontMatterPage);
                    SizeAndOrientation.UpdatePageSizeAndOrientationClasses(flyleaf, layout);
                }
            }
        }

        public static bool IsFrontMatterPage(SafeXmlElement pageDiv)
        {
            return pageDiv.SelectSingleNode("self::div[contains(@class, 'bloom-frontMatter')]")
                != null;
        }

        public static bool IsBackMatterPage(SafeXmlElement pageDiv)
        {
            return pageDiv.SelectSingleNode("self::div[contains(@class, 'bloom-backMatter')]")
                != null;
        }

        public static bool IsXMatterPage(SafeXmlElement pageDiv)
        {
            return pageDiv.SelectSingleNode(
                    "self::div[contains(@class, 'bloom-frontMatter') or contains(@class, 'bloom-backMatter')]"
                ) != null;
        }

        public static bool IsCoverPage(SafeXmlElement pageDiv)
        {
            return pageDiv.SelectSingleNode("self::div[contains(@class, 'cover')]") != null;
        }

        public static bool ShouldBeInBackForDeviceUse(SafeXmlElement pageDiv)
        {
            return pageDiv.SelectSingleNode("self::div[contains(@class, 'frontCover')]") == null;
        }

        /// <summary>
        ///remove any x-matter in the book
        /// </summary>
        public static void RemoveExistingXMatter(HtmlDom dom, List<string> oldPageIds)
        {
            foreach (
                SafeXmlElement div in dom.SafeSelectNodes(
                    "//div[contains(@class,'bloom-frontMatter') or contains(@class,'bloom-backMatter')]"
                )
            )
            {
                var id = div.GetAttribute("id");
                if (!string.IsNullOrEmpty(id))
                {
                    oldPageIds.Add(id);
                }
                div.ParentNode.RemoveChild(div);
            }
        }

        /// <summary>
        /// This will return a different name if we recognize the submitted name and we know that we have changed it or retired it.
        /// </summary>
        public static string MigrateXMatterName(string nameOfXMatterPack)
        {
            // Bloom 3.7 retired the BigBook xmatter pack.
            // If we ever create another xmatter pack called BigBook (or rename the Factory pack) we'll need to redo this.
            string[] retiredPacks = { "BigBook" };
            if (retiredPacks.Contains(nameOfXMatterPack))
                return CollectionSettings.kDefaultXmatterName;
            return nameOfXMatterPack;
            ;
        }
    }
}
