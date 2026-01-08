using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using CommandLine;
using Microsoft.Web.WebView2.Core;
using SIL.IO;

namespace WebView2PdfMaker
{
    static class Program
    {
        static int _returnCode = 0;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        private static int Main(string[] args)
        {
            Console.WriteLine("Starting WebView2PdfMaker");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += new System.Threading.ThreadExceptionEventHandler(
                Application_ThreadException
            );
            AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;

            // See https://github.com/commandlineparser/commandline for documentation about CommandLine.Parser
            var parser = new Parser(
                (settings) =>
                {
                    settings.CaseInsensitiveEnumValues = true;
                    settings.CaseSensitive = false;
                    settings.HelpWriter = Console.Error;
                }
            );
            try
            {
                parser
                    .ParseArguments<Options>(args)
                    .WithParsed<Options>(options =>
                    {
                        options.ValidateOptions();
                        Application.Run(new ConversionHost(options));
                    })
                    .WithNotParsed(errors =>
                    {
                        Console.Error.WriteLine("Error parsing command line arguments.");
                        Environment.Exit(1);
                    });
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Unhandled exception: {0}", e.Message);
                return 1;
            }
            return _returnCode;
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Console.Error.WriteLine("WebView2PdfMaker Thread Exception: " + e.ToString());
            _returnCode = 1;
            Application.Exit();
        }

        private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var except = e.ExceptionObject as Exception;
            if (except != null)
            {
                Console.Error.WriteLine("Unhandled Exception: " + except.ToString());
            }
            else
            {
                Console.Error.WriteLine("WebView2PdfMaker got unknown exception");
            }
            _returnCode = 1;
            Application.Exit();
        }
    }

    [Verb(
        "create",
        isDefault: true,
        HelpText = "Create a PDF from the provided HTML",
        Hidden = true
    )]
    public class Options
    {
        [Option(
            'T',
            "margin-top",
            Default = "10",
            HelpText = "Set the page top margin (in millimeters)"
        )]
        public string TopMargin { get; set; }

        [Option(
            'B',
            "margin-bottom",
            Default = "10",
            HelpText = "Set the page bottom margin (in millimeters)"
        )]
        public string BottomMargin { get; set; }

        [Option(
            'L',
            "margin-left",
            Default = "10",
            HelpText = "Set the page left margin (in millimeters)"
        )]
        public string LeftMargin { get; set; }

        [Option(
            'R',
            "margin-right",
            Default = "10",
            HelpText = "Set the page right margin (in millimeters)"
        )]
        public string RightMargin { get; set; }

        [Option(
            'O',
            "orientation",
            Default = "portrait",
            HelpText = "Set orientation to Landscape or Portrait"
        )]
        public string Orientation { get; set; }

        [Option(
            's',
            "page-size",
            Default = "A4",
            HelpText = "Set paper size to: A4, Letter, etc. "
        )]
        public string PageSizeName { get; set; }

        [Option(
            'h',
            "page-height",
            HelpText = "Page Height (in millimeters). Use this with along with page-width instead of page-size, if needed."
        )]
        public string PageHeight { get; set; }

        [Option('w', "page-width", HelpText = "Page Width (in millimeters)")]
        public string PageWidth { get; set; }

        [Option('q', "quiet", Default = false, HelpText = "Don't show the progress dialog")]
        public bool NoUIMode { get; set; }

        [Option("debug", Default = false, HelpText = "Send debugging information to the console.")]
        public bool Debug { get; set; }

        [Option(
            "webview2-path",
            Required = false,
            HelpText = "The path to the webview2 component to use."
        )]
        public string WebView2Path { get; set; }

        [Value(
            0,
            MetaName = "input",
            Required = true,
            HelpText = "The URI to the input html, including \"file:///\" if a local file. Use quotation marks if it includes spaces."
        )]
        public string InputHtmlUri { get; set; }

        [Value(
            1,
            MetaName = "output",
            Required = true,
            HelpText = "The file name or path to the output pdf. Use quotation marks if it includes spaces."
        )]
        public string OutputPdfPath { get; set; }

        public void ValidateOptions()
        {
            // throw exception if any values are obviously invalid.
            if (IsFileMissing(InputHtmlUri))
                throw new ApplicationException(
                    $"Missing or invalid HTML input file path: \"{InputHtmlUri}\""
                );
            if (String.IsNullOrEmpty(OutputPdfPath))
                throw new ApplicationException("Missing output PDF file path");
            if (
                Orientation.ToLowerInvariant() != "portrait"
                && Orientation.ToLowerInvariant() != "landscape"
            )
                throw new ApplicationException(
                    $"orientation must be either portrait or landscape: {Orientation}"
                );
            var size = GetPaperSize(PageSizeName); // throws exception if invalid
            if (!String.IsNullOrEmpty(PageWidth) && !double.TryParse(PageWidth, out double width))
                throw new ApplicationException($"invalid page-width value: {PageWidth}");
            if (
                !String.IsNullOrEmpty(PageHeight) && !double.TryParse(PageHeight, out double height)
            )
                throw new ApplicationException($"invalid page-height value: {PageHeight}");
            if (!double.TryParse(TopMargin, out double top))
                throw new ApplicationException($"invalid margin-top value: {TopMargin}");
            if (!double.TryParse(BottomMargin, out double bottom))
                throw new ApplicationException($"invalid margin-bottom value: {BottomMargin}");
            if (!double.TryParse(LeftMargin, out double left))
                throw new ApplicationException($"invalid margin-left value: {LeftMargin}");
            if (!double.TryParse(RightMargin, out double right))
                throw new ApplicationException($"invalid margin-right value: {RightMargin}");
        }

        private bool IsFileMissing(string inputHtmlUri)
        {
            if (String.IsNullOrEmpty(inputHtmlUri))
                return true;
            if (inputHtmlUri.StartsWith("file:///"))
                return !RobustFile.Exists(Uri.UnescapeDataString(inputHtmlUri.Substring(8)));
            return false;
        }

        internal CoreWebView2PrintOrientation GetWebView2Orientation()
        {
            if (Orientation.ToLowerInvariant() == "portrait")
                return CoreWebView2PrintOrientation.Portrait;
            else
                return CoreWebView2PrintOrientation.Landscape;
        }

        private class PaperSize
        {
            public readonly string Name;
            public readonly double WidthInMillimeters;
            public readonly double HeightInMillimeters;

            public PaperSize(string name, double widthInMillimeters, double heightInMillimeters)
            {
                Name = name;
                WidthInMillimeters = widthInMillimeters;
                HeightInMillimeters = heightInMillimeters;
            }
        }

        private PaperSize GetPaperSize(string name)
        {
            name = name.ToLowerInvariant();
            var sizes = new List<PaperSize>
            {
                new PaperSize("a3", 297, 420),
                new PaperSize("a4", 210, 297),
                new PaperSize("a5", 148, 210),
                new PaperSize("a6", 105, 148),
                new PaperSize("b3", 353, 500),
                new PaperSize("b4", 250, 353),
                new PaperSize("b5", 176, 250),
                new PaperSize("b6", 125, 176),
                new PaperSize("letter", 215.9, 279.4),
                new PaperSize("halfletter", 139.7, 215.9),
                new PaperSize("quarterletter", 107.95, 139.7),
                new PaperSize("legal", 215.9, 355.6),
                new PaperSize("halflegal", 177.8, 215.9),
                new PaperSize("device16x9", 100, 1600d / 9d),
            };

            var match = sizes.Find(s => s.Name == name);
            if (match != null)
                return match;

            throw new ApplicationException(
                "Sorry, currently WebView2PdfMaker has a very limited set of paper sizes it knows about. Consider using the page-height and page-width arguments instead"
            );
        }

        const double kMillimetersPerInch = 25.4; // (or more precisely, 25.3999999999726)

        internal double GetWebView2PageWidth()
        {
            if (!String.IsNullOrEmpty(PageWidth))
            {
                double.TryParse(PageWidth, out double mmPageWidth);
                if (Debug)
                    Console.Out.WriteLine("DEBUG: raw page width = {0}mm", mmPageWidth);
                return mmPageWidth / kMillimetersPerInch;
            }
            var size = GetPaperSize(PageSizeName);
            if (Debug)
                Console.Out.WriteLine("DEBUG: raw page width = {0}mm", size.WidthInMillimeters);
            return size.WidthInMillimeters / kMillimetersPerInch;
        }

        internal double GetWebView2PageHeight()
        {
            if (!String.IsNullOrEmpty(PageHeight))
            {
                double.TryParse(PageHeight, out double mmPageHeight);
                if (Debug)
                    Console.Out.WriteLine("DEBUG: raw page height = {0}mm", mmPageHeight);
                return mmPageHeight / kMillimetersPerInch;
            }
            var size = GetPaperSize(PageSizeName);
            if (Debug)
                Console.Out.WriteLine("DEBUG: raw page height = {0}mm", size.HeightInMillimeters);
            return size.HeightInMillimeters / kMillimetersPerInch;
        }

        internal double GetWebView2TopMargin()
        {
            double.TryParse(TopMargin, out double top);
            return top / kMillimetersPerInch;
        }

        internal double GetWebView2BottomMargin()
        {
            double.TryParse(BottomMargin, out double bottom);
            return bottom / kMillimetersPerInch;
        }

        internal double GetWebView2LeftMargin()
        {
            double.TryParse(LeftMargin, out double left);
            return left / kMillimetersPerInch;
        }

        internal double GetWebView2RightMargin()
        {
            double.TryParse(RightMargin, out double right);
            return right / kMillimetersPerInch;
        }
    }
}
