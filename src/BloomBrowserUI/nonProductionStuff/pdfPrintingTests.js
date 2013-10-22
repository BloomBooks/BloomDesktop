

function print(){
    netscape.security.PrivilegeManager.enablePrivilege("UniversalBrowserRead");
    netscape.security.PrivilegeManager.enablePrivilege('UniversalXPConnect');
    			var webBrowserPrint =
    window.content.QueryInterface(Components.interfaces.nsIInterfaceRequestor)
    	.getInterface(Components.interfaces.nsIWebBrowserPrint);

    var printSettings = webBrowserPrint.globalPrintSettings;
	webBrowserPrint.print(printSettings, null);

}

function pdf(){
    netscape.security.PrivilegeManager.enablePrivilege('UniversalXPConnect');
    			var webBrowserPrint =
    window.content.QueryInterface(Components.interfaces.nsIInterfaceRequestor)
    	.getInterface(Components.interfaces.nsIWebBrowserPrint);

    var printSettings = webBrowserPrint.globalPrintSettings;
    printSettings.printToFile = true;
    printSettings.toFileName = "c:\test.pdf";
    printSettings.printSilent = true;
    printSettings.showPrintProgress = false;
    printSettings.outputFormat =  Components.interfaces.nsIPrintSettings.kOutputFormatPDF;

    webBrowserPrint.print(printSettings, null);
}

function pdf1()
{
    netscape.security.PrivilegeManager.enablePrivilege("UniversalBrowserRead");
    netscape.security.PrivilegeManager.enablePrivilege('UniversalXPConnect');
    			var webBrowserPrint =
    window.content.QueryInterface(Components.interfaces.nsIInterfaceRequestor)
    	.getInterface(Components.interfaces.nsIWebBrowserPrint);

    var printSettings = webBrowserPrint.globalPrintSettings;

//    printSettings.orientation     = orientation;
//    printSettings.marginTop       = marginTop;
//    printSettings.marginBottom    = marginBottom;
//    printSettings.marginLeft      = marginLeft;
//    printSettings.marginRight     = marginRight;
 //   printSettings.printBGColors   = bgColors;
//    printSettings.printBGImages   = bgImages;
    printSettings.footerStrLeft   = "";
    printSettings.footerStrCenter = "";
    printSettings.footerStrRight  = "";
    printSettings.headerStrLeft   = "";
    printSettings.headerStrRight  = "";
    printSettings.headerStrCenter = "";
     printSettings.printToFile = true;
     printSettings.printSilent = true;
     printSettings.toFileName = "c://test.pdf";
    printSettings.OutputFormat =     Components.interfaces.nsIPrintSettings.kOutputFormatPDF;
     // Adobe Postscript Drivers are expected (together with a FILE: printer called
     // "Generic PostScript Printer". Drivers can be found here:
     // http://www.adobe.com/support/downloads/product.jsp?product=44&platform=Windows

         printSettings.printerName = "Generic PostScript Printer";

     printSettings.paperName = paperName;
     printSettings.showPrintProgress = false;
}