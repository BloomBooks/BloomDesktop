using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using BloomTemp;

namespace Bloom.Edit
{
    /// <summary>
    /// This class supports the Edit Tab Toolbox as a whole. Eventually, all of this should be moved to javascript-land.
    /// Since the toolbox is mainly implemented in HTML/Javascript, there is no distinct .NET control for it.
    /// Thus, unlike other View classes in Bloom, ToolboxView does not inherit from a Control class,
    /// nor are there ever any instances; all methods are currently static.
    /// Currently necessary steps to add a new tool:
    /// - Add the tool's folder to ToolboxView.GetToolboxServerDirectories().
    /// - Create a folder under BloomBrowserUI/bookEdit/toolbox. Its name should match the toolId (see below).
    /// - Create a file in that folder with extension .tsx to contain the React code of the panel
    ///		- it (or another file) should have a class which implements ITool
    ///			- minimally this must implement id() to return the tool ID
    ///			- also beginRestoreSettings should return a (possibly already-resolved) promise.
    ///			- create one instance and publish it to get the tool known to the toolbox and include its
    ///				code in the toolbox bundle: in toolboxBootstrap.ts, add a line like
    ///				ToolBox.registerTool(new MyWonderfulTool());
    ///			- should implement makeRootElement() to create one div, the react root.
    ///				- the returned root should already have been passed to ReactDOM.render().
    ///			- Make a new xlf entry with ID EditTab.Toolbox.{UCToolId}.Heading,
    ///				where UCToolId is the capitalized version of your tool Id, e.g., "Music".
    ///				We currently assume the default English value of this will be UCToolId Tool, e.g., "Music Tool"
    ///				(This supports localization of the tool's accordion tab label.)
    /// - In some toolbox less file (typically a new one for your tool, but could be toolbox.less)
    ///		you need to create a rule like
    ///			.ui-accordion h3[data-toolId="motionTool"] span.ui-accordion-header-icon {
    ///				background-image:url('/bloom/images/motion.svg') !important;
    ///			}
    ///		which specifies the icon for your tool. (And create the icon in the BloomBrowserUI/images folder).
    /// - Usually you will add a line to GetToolboxServerDirectories() in this file
    /// - Add two lines like this to src\BloomBrowserUI\bookEdit\toolbox\settings\Settings.pug
    ///		.checkbox.clear#musicCheck(data-tool='musicTool', onclick='editTabBundle.showOrHideTool_click(this);')
    ///		.checkbox-label(data-i18n='EditTab.Toolbox.Music.Heading') Music Tool
    /// </summary>
    public class ToolboxView
    {
        public static void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler("toolbox/settings", HandleSettings, false);
        }

        /// <summary>
        /// Some tool settings files may need moving to the correct locations when installing a bloompack.
        /// Currently only the reader tools needs to do this. We could try to do some trick where
        /// we call a method on all subclasses by reflection, but I think this is sufficient
        /// encapsulation.
        /// </summary>
        /// <param name="newlyAddedFolderOfThePack"></param>
        internal static void CopyToolSettingsForBloomPack(string newlyAddedFolderOfThePack)
        {
            DecodableReaderToolSettings.CopyReaderToolsSettingsToWhereTheyBelong(
                newlyAddedFolderOfThePack
            );
        }

        /// <summary>
        /// Provides a hook for anything the toolbox wants to do when a project is opened.
        /// </summary>
        public static void SetupToolboxForCollection(CollectionSettings settings)
        {
            DecodableReaderToolSettings.CopyRelevantNewReaderSettings(settings);
        }

        public static IEnumerable<string> GetToolboxServerDirectories()
        {
            yield return BloomFileLocator.GetBrowserDirectory("bookEdit", "toolbox");
            yield return BloomFileLocator.GetBrowserDirectory(
                "bookEdit/toolbox/readers/leveledReader"
            );
            yield return BloomFileLocator.GetBrowserDirectory(
                "bookEdit/toolbox/readers/decodableReader"
            );
            yield return BloomFileLocator.GetBrowserDirectory("bookEdit/toolbox/talkingBook");
            yield return BloomFileLocator.GetBrowserDirectory("bookEdit/toolbox/motion");
            yield return BloomFileLocator.GetBrowserDirectory("bookEdit/toolbox/music");
            yield return BloomFileLocator.GetBrowserDirectory(
                "bookEdit/toolbox/readers/readerSetup"
            );
            yield return BloomFileLocator.GetBrowserDirectory("bookEdit/toolbox/signLanguage");
            yield return BloomFileLocator.GetBrowserDirectory("bookEdit/toolbox/imageDescription");
            yield return BloomFileLocator.GetBrowserDirectory("bookEdit/toolbox/overlay");
        }

        public static string MakeToolboxContent(Book.Book book)
        {
            var path = BloomFileLocator.GetBrowserFile(false, "bookEdit/toolbox", "toolbox.html");
            var domForToolbox = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(path));
            XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(domForToolbox.RawDom);
            return domForToolbox.getHtmlStringDisplayOnly();
        }

        private static void HandleSettings(ApiRequest request)
        {
            if (request.HttpMethod != HttpMethods.Get)
                throw new ApplicationException(request.LocalPath() + " only implements 'get'");

            var settings = new Dictionary<string, object>
            {
                { "current", request.CurrentBook.BookInfo.CurrentTool }
            };

            foreach (var tool in request.CurrentBook.BookInfo.Tools)
            {
                if (!String.IsNullOrEmpty(tool.State))
                    settings.Add(tool.StateName, tool.State);
            }

            request.ReplyWithJson(settings);
        }

        /// <summary>
        /// Used to save various settings relating to the toolbox. Passed a string which is typically two or three elements
        /// divided by a tab.
        /// - may be passed 'active' followed by the ID of one of the check boxes that indicates whether the DR, LR, or TB tools are
        /// in use, followed by "1" if it is used, or "0" if not. These IDs are arranged to be the tool name followed by "Check".
        /// - may be passed 'current' followed by the name of one of the toolbox tools
        /// - may be passed 'state' followed by the name of one of the tools and its current state string.
        /// </summary>
        public static void SaveToolboxSettings(Book.Book book, string data)
        {
            var args = data.Split(new[] { '\t' });

            switch (args[0])
            {
                case "active":
                    UpdateActiveToolSetting(
                        book,
                        args[1].Substring(0, args[1].Length - "Check".Length),
                        args[2] == "1"
                    );
                    return;

                case "current":
                    book.BookInfo.CurrentTool = args[1];
                    return;

                case "state":
                    UpdateToolState(book, args[1], args[2]);
                    return;
                case "visibility":
                    UpdateToolboxVisibility(book, args[1]);
                    return;
            }
        }

        private static void UpdateToolboxVisibility(Book.Book book, string visibility)
        {
            book.BookInfo.ToolboxIsOpen = (visibility == "visible");
            book.BookInfo.Save();
        }

        private static void UpdateToolState(Book.Book book, string toolName, string state)
        {
            var tools = book.BookInfo.Tools;
            var item = tools.FirstOrDefault(t => t.ToolId == toolName);

            if (item != null)
            {
                item.State = state;
            }
        }

        private static void UpdateActiveToolSetting(Book.Book book, string toolName, bool enabled)
        {
            var tools = book.BookInfo.Tools;
            var item = tools.FirstOrDefault(t => t.ToolId == toolName);

            if (item == null)
            {
                item = ToolboxToolState.CreateFromToolId(toolName);
                tools.Add(item);
            }
            item.Enabled = enabled;
        }
    }
}
