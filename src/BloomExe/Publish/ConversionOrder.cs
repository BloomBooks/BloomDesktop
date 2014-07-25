using System;
using System.ComponentModel;

namespace GeckofxHtmlToPdf
{
	/// <summary>
	/// a goal here is to follow the wkhtmltopdf parameters where it makes sense, to ease people  switching
	/// to/from wkhtmltopdf:
	/// http://madalgo.au.dk/~jakobt/wkhtmltoxdoc/wkhtmltopdf-0.9.9-doc.html
	/// 
	/// Properties without the [Option] declarations aren't accessible directly, but are more convenient
	/// when the order is being constructed from code instead of commandline arguments.
	/// </summary>
	public class ConversionOrder
	{
		private string _pageSizeName;

		//[Args.ArgsMemberSwitch(0)]
		[Description("The file name or path to the input html. Use quotation marks if it includes spaces.")]
		public string InputHtmlPath { get; set; }

		//[Args.ArgsMemberSwitch(1)]
		[Description("The file name or path to the output pdf. Use quotation marks if it includes spaces.")]
		public string OutputPdfPath { get; set; }

		[Description("Enable SIL Graphite smart font rendering")]
		[DefaultValue(false)]
		//[Args.ArgsMemberSwitch("-graphite")]
		public bool EnableGraphite { get; set; }

		[Description("Set the page top margin")]
		[DefaultValue("10")]
		//[Args.ArgsMemberSwitch("T","-margin-top")]
		public string TopMargin { get; set; }

		[Description("Set the page bottom margin")]
		[DefaultValue("10")]
		//[Args.ArgsMemberSwitch("B", "-margin-bottom")]
		public string BottomMargin { get; set; }

		[Description("Set the page left margin")]
		[DefaultValue("10")]
		//[Args.ArgsMemberSwitch("L", "-margin-left")]
		public string LeftMargin { get; set; }

		[Description("Set the page right margin")]
		[DefaultValue("10")]
		//[Args.ArgsMemberSwitch("R", "-margin-right")]
		public string RightMargin { get; set; }

		private double GetMillimeters(string distance)
		{
			//TODO: convert to mm. For now, just strips "mm"
			return double.Parse(distance.Replace("mm", ""));
		}
		public double TopMarginInMillimeters
		{
			get { return GetMillimeters(TopMargin); }
			set { TopMargin = value.ToString(); }
		}
		public double BottomMarginInMillimeters
		{
			get { return GetMillimeters(BottomMargin); }
			set { BottomMargin = value.ToString(); }
		}


		public double LeftMarginInMillimeters
		{
			get { return GetMillimeters(LeftMargin); }
			set { LeftMargin = value.ToString(); }
		}
		public double RightMarginInMillimeters
		{
			get { return GetMillimeters(RightMargin); }
			set { RightMargin = value.ToString(); }
		}

		[Description("Set orientation to Landscape or Portrait")]
		[DefaultValue("portrait")]
		//[Args.ArgsMemberSwitch("O", "-orientation")]
		public string Orientation { get; set; }

		public bool Landscape
		{
			get { return Orientation != null && Orientation.ToLower() == "landscape"; }
			set { Orientation = value ? "landscape" : "portrait"; }
		}

	

		[DefaultValue("A4")]
		//[Args.ArgsMemberSwitch("s","-page-size")]
		[Description("Set paper size to: A4, Letter, etc. ")]
		public string PageSizeName
		{
			get { return _pageSizeName; }
			set { _pageSizeName = value; }
		}

		[Description("Page Height (in millimeters). Use this with along with page-width instead of page-size, if needed.")]
		//[Args.ArgsMemberSwitch("h", "-page-height")]
		public string PageHeight { get; set; }

		[Description("Page Width (in millimeters)")]
		//[Args.ArgsMemberSwitch("w", "-page-width")]
		public string PageWidth { get; set; }

		public double PageHeightInMillimeters
		{
			get
			{
				if (string.IsNullOrWhiteSpace(PageHeight))
				{
					return 0;
				}
				else
				{
					return GetMillimeters(PageHeight);
				}	
			}//todo: units?
			set { PageHeight = value.ToString();}//todo: units?
		}
		public double PageWidthInMillimeters
		{
			get
			{
				if (string.IsNullOrWhiteSpace(PageWidth))
				{
					return 0;
				}
				else
				{
					return GetMillimeters(PageWidth);
				}
			} //todo: units?
			set { PageWidth = value.ToString(); }//todo: units?
		}

		[Description("Don't show the progress dialog")]
		//[Args.ArgsMemberSwitch("q", "-quiet")]
		[DefaultValue(false)]
		public bool NoUIMode { get; set; }

		public bool IsHTTP
		{
			get { return InputHtmlPath.ToLower().StartsWith("http"); }
		}

		[DefaultValue(false)]
		[Description("Send debugging information to the console.")]
		//[Args.ArgsMemberSwitch("-debug","-debug-javascript")]
		public bool Debug { get; set; }

/* doesn't work yet
		[DefaultValue(1.0)]
		//[Args.ArgsMemberSwitch("-zoom")]
		[Description("Zoom/scaling factor")]
 
		public double Zoom { get; set; }
		*/
	}
}