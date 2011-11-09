using System;
using Skybound.Gecko;
using System.Runtime.InteropServices;

namespace Skybound.Gecko
{
	//AGENT_SHEET = 0
	//USER_SHEET = 1
	[Guid("1f42a6a2-ab0a-45d4-8a96-396f58ea6c6d"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface nsIStyleSheetService
	{
		void loadAndRegisterSheet(nsIURI sheetURI, uint type);
		bool sheetRegistered(nsIURI sheetURI, uint type);
		void unregisterSheet(nsIURI sheetURI, uint type);
	}

	public class GeckoStyleSheetService
	{
		nsIStyleSheetService StyleSheetService;

		public GeckoStyleSheetService()
		{
			StyleSheetService = Xpcom.GetService<nsIStyleSheetService>("@mozilla.org/content/style-sheet-service;1");
			StyleSheetService = Xpcom.QueryInterface<nsIStyleSheetService>(StyleSheetService);
		}

		public bool IsStyleSheetRegistered(string sheetURI, uint type)
		{
			nsIIOService ios = Xpcom.GetService<nsIIOService>("@mozilla.org/network/io-service;1");
			ios = Xpcom.QueryInterface<nsIIOService>(ios);
			nsIURI ssURI = ios.NewURI(new nsAUTF8String(sheetURI), null, null);
			if (!StyleSheetService.sheetRegistered(ssURI, type))
			{
				return false;
			}
			return true;
		}

		public void RegisterStyleSheet(string sheetURI, uint type)
		{
			nsIIOService ios = Xpcom.GetService<nsIIOService>("@mozilla.org/network/io-service;1");
			ios = Xpcom.QueryInterface<nsIIOService>(ios);
			nsIURI ssURI = ios.NewURI(new nsAUTF8String(sheetURI), null, null);
			if (StyleSheetService.sheetRegistered(ssURI, type))
				return;

			StyleSheetService.loadAndRegisterSheet(ssURI, type);
		}

		public void UnregisterStyleSheet(string sheetURI, uint type)
		{
			nsIIOService ios = Xpcom.GetService<nsIIOService>("@mozilla.org/network/io-service;1");
			ios = Xpcom.QueryInterface<nsIIOService>(ios);
			nsIURI ssURI = ios.NewURI(new nsAUTF8String(sheetURI), null, null);
			if (StyleSheetService.sheetRegistered(ssURI, type))
				StyleSheetService.unregisterSheet(ssURI, type);
		}
	}
}