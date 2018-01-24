using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using BloomTemp;
using Newtonsoft.Json;
using SIL.Code;
using SIL.IO;
using SIL.Xml;

namespace Bloom.Edit
{
	/// <summary>
	/// This class supports the Edit Tab Toolbox as a whole. Eventually, all of this should be moved to javascript-land.
	/// Since the toolbox is mainly implemented in HTML/Javascript, there is no distinct .NET control for it.
	/// Thus, unlike other View classes in Bloom, ToolboxView does not inherit from a Control class,
	/// nor are there ever any instances; all methods are currently static.
	/// Currently necessary steps to add a new tool:
	/// - Create a subclass of ToolboxTool.
	///		- Give it a static constant string StaticToolId
	///		- override ToolId to return StaticToolId
	/// - Add a case to ToolboxTool.CreateFromToolId() and GetToolboxToolFromJsonObject().
	/// - Add the tool's folder to ToolboxView.GetToolboxServerDirectories().
	/// - Create a folder under BloomBrowserUI/bookEdit/toolbox. It's name should match the toolId.
	/// - Create a file in that folder with extension .tsx to contain the React code of the panel
	///		- it (or another file) should have a class which implements ITool
	///			- minimally this must implement name() to return the tool ID
	///			- also beginRestoreSettings should return a (possibly already-resolved) promise.
	///			- create one instance and publish it to get the tool known to the toolbox:
	///				ToolBox.getTabModels().push(new MyWonderfulTool());
	///			- should implement makeRootElements() to create html with one h3 and one div,
	///				both having attribute data-panelId='{toolId}Tool'. The h3 should have the tool's
	///				accordion label (with suitable i18n attr).
	///			- the div will be passed to ReactDOM.render() as the root element.
	/// </summary>
	public class ToolboxView
	{
		private static string[] _idsOfToolsThisVersionKnowsAbout;

		// We could just list them (and used to), but this version makes it unnecessary to remember to add
		// new ones to the list.
		private static string[] IdsOfToolsThisVersionKnowsAbout
		{
			get
			{
				if (_idsOfToolsThisVersionKnowsAbout == null)
				{
					_idsOfToolsThisVersionKnowsAbout = typeof(ToolboxTool).Assembly.GetTypes()
						.Where(t => t.IsSubclassOf(typeof(ToolboxTool)))
						.Where(t => t != typeof(UnknownTool))
						.Select(t => t.GetField("StaticToolId").GetValue(null))
						.Cast<string>()
						.ToArray();
				}
				return _idsOfToolsThisVersionKnowsAbout;
			}
		}

		public static void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("toolbox/settings", HandleSettings, false);
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
			DecodableReaderTool.CopyReaderToolsSettingsToWhereTheyBelong(newlyAddedFolderOfThePack);
		}

		/// <summary>
		/// Provides a hook for anything the toolbox wants to do when a project is opened.
		/// </summary>
		/// <param name="settings"></param>
		public static void SetupToolboxForCollection(CollectionSettings settings)
		{
			DecodableReaderTool.CopyRelevantNewReaderSettings(settings);
		}

		public static IEnumerable<string> GetToolboxServerDirectories()
		{
			yield return BloomFileLocator.GetBrowserDirectory("bookEdit","toolbox");
			yield return BloomFileLocator.GetBrowserDirectory("bookEdit/toolbox/readers/leveledReader");
			yield return BloomFileLocator.GetBrowserDirectory("bookEdit/toolbox/readers/decodableReader");
			yield return BloomFileLocator.GetBrowserDirectory("bookEdit/toolbox/talkingBook");
			yield return BloomFileLocator.GetBrowserDirectory("bookEdit/toolbox/panAndZoom");
			yield return BloomFileLocator.GetBrowserDirectory("bookEdit/toolbox/music");
			yield return BloomFileLocator.GetBrowserDirectory("bookEdit/toolbox/bookSettings");
			yield return BloomFileLocator.GetBrowserDirectory("bookEdit/toolbox/readers/readerSetup");
		}

		public static string MakeToolboxContent(Book.Book book)
		{
			var path = BloomFileLocator.GetBrowserFile(false, "bookEdit/toolbox", "toolbox.html");
			var domForToolbox = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(path));
			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(domForToolbox.RawDom);
			return TempFileUtils.CreateHtml5StringFromXml(domForToolbox.RawDom);
		}

		private static List<ToolboxTool> GetPossibleTools(Book.Book book, string[] idsOfToolsThisVersionKnowsAbout)
		{
			var toolsThatHaveDataInBookInfo =
				book.BookInfo.Tools.Where(t => idsOfToolsThisVersionKnowsAbout.Contains(t.ToolId)).ToList();
			var toolsToDisplay = toolsThatHaveDataInBookInfo;
			toolsToDisplay.AddRange(
				idsOfToolsThisVersionKnowsAbout.Except(
					toolsThatHaveDataInBookInfo.Select(t => t.ToolId)).Select(ToolboxTool.CreateFromToolId));
			return toolsToDisplay.ToList();
		}


		private static void HandleSettings(ApiRequest request)
		{
			if(request.HttpMethod != HttpMethods.Get)
				throw new ApplicationException(request.LocalPath()+" only implements 'get'");

			var settings = new Dictionary<string, object>
			{
				{"current", request.CurrentBook.BookInfo.CurrentTool}
			};

			foreach (var tool in GetPossibleTools(request.CurrentBook, IdsOfToolsThisVersionKnowsAbout))
			{
				if (!String.IsNullOrEmpty(tool.State))
					settings.Add(tool.StateName, tool.State);
				else
				{
					var defaultState = tool.DefaultState();
					if (!string.IsNullOrEmpty(defaultState))
					{
						settings.Add(tool.StateName, defaultState);
					}
				}
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
					UpdateActiveToolSetting(book, args[1].Substring(0, args[1].Length - "Check".Length), args[2] == "1");
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
			book.BookInfo.ToolboxIsOpen = (visibility=="visible");
			book.BookInfo.Save();
		}

		private static void UpdateToolState(Book.Book book, string toolName, string state)
		{
			var tools = book.BookInfo.Tools;
			var item = tools.FirstOrDefault(t => t.ToolId == toolName);

			if (item != null)
			{
				item.State = state;
				item.SaveDefaultState();
			}
		}

		private static void UpdateActiveToolSetting(Book.Book book, string toolName, bool enabled)
		{
			var tools = book.BookInfo.Tools;
			var item = tools.FirstOrDefault(t => t.ToolId == toolName);

			if (item == null)
			{
				item = ToolboxTool.CreateFromToolId(toolName);
				tools.Add(item);
			}
			item.Enabled = enabled;
		}
	}
}
