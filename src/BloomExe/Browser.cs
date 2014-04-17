using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Gecko;
using Gecko.DOM;
using Gecko.Events;
using Palaso.IO;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.HtmlBrowser;
using Palaso.Xml;
using TempFile = BloomTemp.TempFile;

namespace Bloom
{
	public partial class Browser : UserControl
    {
        protected GeckoWebBrowser _browser;
        bool _browserIsReadyToNavigate;
        private string _url;
    	private XmlDocument _pageDom;
    	private TempFile _tempHtmlFile;
        private PasteCommand _pasteCommand;
        private CopyCommand _copyCommand;
		private  UndoCommand _undoCommand;
        private  CutCommand _cutCommand;
	    private bool _disposed;
	    public event EventHandler OnBrowserClick;

		private static int XulRunnerVersion
		{
			get
			{
				var geckofx = Assembly.GetAssembly(typeof(GeckoWebBrowser));
				if (geckofx == null)
					return 0;

				var versionAttribute = geckofx.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), true)
					.FirstOrDefault() as AssemblyFileVersionAttribute;
				return versionAttribute == null ? 0 : new Version(versionAttribute.Version).Major;
			}
		}

		// TODO: refactor to use same initialization code as Palaso
		public static void SetUpXulRunner()
		{
			if (Xpcom.IsInitialized)
				return;

			string xulRunnerPath = Environment.GetEnvironmentVariable("XULRUNNER");
			if (!Directory.Exists(xulRunnerPath))
			{
				xulRunnerPath = Path.Combine(FileLocator.DirectoryOfApplicationOrSolution, "xulrunner");
				if (!Directory.Exists(xulRunnerPath))
				{
					//if this is a programmer, go look in the lib directory
					xulRunnerPath = Path.Combine(FileLocator.DirectoryOfApplicationOrSolution,
						Path.Combine("lib", "xulrunner"));

					//on my build machine, I really like to have the dir labelled with the version.
					//but it's a hassle to update all the other parts (installer, build machine) with this number,
					//so we only use it if we don't find the unnumbered alternative.
					if (!Directory.Exists(xulRunnerPath))
					{
						xulRunnerPath = Path.Combine(FileLocator.DirectoryOfApplicationOrSolution,
							Path.Combine("lib", "xulrunner" + XulRunnerVersion));
					}

					if (!Directory.Exists(xulRunnerPath))
					{
						throw new ConfigurationException(
							"Can't find the directory where xulrunner (version {0}) is installed",
							XulRunnerVersion);
					}
				}
			}

			Xpcom.Initialize(xulRunnerPath);
			Application.ApplicationExit += OnApplicationExit;
		}

		private static void OnApplicationExit(object sender, EventArgs e)
		{
			// We come here iff we initialized Xpcom. In that case we want to call shutdown,
			// otherwise the app might not exit properly.
			if (Xpcom.IsInitialized)
				Xpcom.Shutdown();
			Application.ApplicationExit -= OnApplicationExit;
		}

        public Browser()
        {
            InitializeComponent();
        }

        public void SetEditingCommands( CutCommand cutCommand, CopyCommand copyCommand, PasteCommand pasteCommand, UndoCommand undoCommand)
        {
            _cutCommand = cutCommand;
            _copyCommand = copyCommand;
            _pasteCommand = pasteCommand;
            _undoCommand = undoCommand;

            _cutCommand.Implementer = () => _browser.CutSelection();
            _copyCommand.Implementer = () => _browser.CopySelection();
            _pasteCommand.Implementer = PasteFilteredText;
            _undoCommand.Implementer = () => _browser.Undo();

            //none of these worked
/*            _browser.DomKeyPress+=new GeckoDomKeyEventHandler((sender, args) => UpdateEditButtons());
            _browser.DomClick += new GeckoDomEventHandler((sender, args) => UpdateEditButtons());
            _browser.DomFocus += new GeckoDomEventHandler((sender, args) => UpdateEditButtons());
  */      }

        public void SaveHTML(string path)
        {
            _browser.SaveDocument(path, "text/html");
        }

        private void UpdateEditButtons()
        {
            if (_copyCommand == null)
                return;
			try
			{
				_cutCommand.Enabled = _browser != null && _browser.CanCutSelection;
				_copyCommand.Enabled = _browser != null && _browser.CanCopySelection;
				_pasteCommand.Enabled = _browser != null && _browser.CanPaste;
				if (_pasteCommand.Enabled)
				{
					//prevent pasting images (BL-93)
					_pasteCommand.Enabled = Clipboard.ContainsText();
				}
				_undoCommand.Enabled = _browser != null && _browser.CanUndo;

			}
			catch (Exception)
			{	
				_pasteCommand.Enabled = false;
				Logger.WriteMinorEvent("UpdateEditButtons(): Swallowed exception.");
				//REf jira.palaso.org/issues/browse/BL-197
				//I saw this happen when Bloom was in the background, with just normal stuff on the clipboard.
				//so it's probably just not ok to check if you're not front-most.
			}
        }

        void OnValidating(object sender, CancelEventArgs e)
		{
			LoadPageDomFromBrowser();
			//_afterValidatingTimer.Enabled = true;//LoadPageDomFromBrowser();
		}
 
        /// <summary> 
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (_tempHtmlFile != null)
			{
				_tempHtmlFile.Dispose();
				_tempHtmlFile = null;
			}
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
            _disposed = true;
		}
        public GeckoWebBrowser WebBrowser { get { return _browser; } }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if(DesignMode)
            {
                this.BackColor=Color.DarkGray;
                return;
            }

            _browser = new GeckoWebBrowser();
		
            _browser.Parent = this;
            _browser.Dock = DockStyle.Fill;
            Controls.Add(_browser);
        	_browser.NoDefaultContextMenu = true;
			_browser.ShowContextMenu += OnShowContextMenu;

			_browser.Navigating += _browser_Navigating;
           // NB: registering for domclicks seems to stop normal hyperlinking (which we don't
            //necessarily need).  When I comment this out, I get an error if the href had, for example,
            //"bloom" for the protocol.  We could probably install that as a protocol, rather than
            //using the click to just get a target and go from there, if we wanted.
			_browser.DomClick += OnBrowser_DomClick;

			_browser.DomKeyPress += OnDomKeyPress;
            _browserIsReadyToNavigate = true;
            
            UpdateDisplay();
			_browser.Validating += new CancelEventHandler(OnValidating);
        	_browser.Navigated += CleanupAfterNavigation;//there's also a "document completed"
            _browser.DocumentCompleted += new EventHandler(_browser_DocumentCompleted);

            _updateCommandsTimer.Enabled = true;//hack
        	var errorsToHide = new List<string>();
			errorsToHide.Add("['Shockwave Flash'] is undefined"); // can happen when mootools (used by calendar) is loaded
			//after swalling that one, you just get another... do this for now
			errorsToHide.Add("mootools"); // can happen when mootools (used by calendar) is loaded

			errorsToHide.Add("PlacesCategoriesStarter.js"); //happens if you let bloom sit there long enough

			errorsToHide.Add("PlacesDBUtils"); //happens if you let bloom sit there long enough

			errorsToHide.Add("privatebrowsing"); //no idea why it shows this error sometimes

			//again, more generally
			errorsToHide.Add("xulrunner"); // can happen when mootools (used by calendar) is loaded

#if !DEBUG
			errorsToHide.Add("Cleanup"); // TODO: can happen when switching pages quickly, as it tries to run it on about:blank. This suggests that sometimes pages aren't cleaned up.
#endif
			//This one started appearing, only on the ImageOnTop pages, when I introduced jquery.resize.js 
			//and then added the ResetRememberedSize() function to it. So it's my fault somehow, but I haven't tracked it down yet.
			//it will continue to show in firebug, so i won't forget about it

			errorsToHide.Add("jquery.js at line 622");
 			WebBrowser.JavascriptError += (sender, error) =>
			{
				var msg = string.Format("There was a JScript error in {0} at line {1}: {2}",
										error.Filename, error.Line, error.Message);
				if (!errorsToHide.Any(matchString => msg.Contains(matchString)))
					Palaso.Reporting.ErrorReport.NotifyUserOfProblem(msg);
			};

			GeckoPreferences.User["mousewheel.withcontrolkey.action"] = 3;
			GeckoPreferences.User["browser.zoom.full"] = true;

            //in firefox 14, at least, there was a bug such that if you have more than one lang on the page, all are check with English
            //until we get past that, it's just annoying
            
            GeckoPreferences.User["layout.spellcheckDefault"] = 0;

			RaiseGeckoReady();
       }

		private void _browser_DocumentCompleted(object sender, EventArgs e)
		{
			//no: crashes (at least in Sept 2012) AutoZoom();
		}

		/// <summary>
		/// Prevent a CTRL+V pasting when we have the Paste button disabled, e.g. when pictures are on the clipboard
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		void OnDomKeyPress(object sender, DomKeyEventArgs e)
		{
		    const uint DOM_VK_INSERT = 0x2D;
            if ((e.CtrlKey && e.KeyChar == 'v') || (e.ShiftKey && e.KeyCode == DOM_VK_INSERT)) //someone was using shift-insert to do the paste
			{
				if (_pasteCommand==null /*happend in calendar config*/ || !_pasteCommand.Enabled)
				{
					Debug.WriteLine("Paste not enabled, so ignoring.");
					e.PreventDefault();
				}
				else if(_browser.CanPaste && Clipboard.ContainsText())
				{
					e.PreventDefault(); //we'll take it from here, thank you very much
					PasteFilteredText();
				}
			}
		}

		private void PasteFilteredText()
		{
            //Remove everything from the clipboard except the unicode text (e.g. remove messy html from ms word)
			var originalText = Clipboard.GetText(TextDataFormat.UnicodeText);
            //setting clears everything else:
			Clipboard.SetText(originalText, TextDataFormat.UnicodeText);
			_browser.Paste();
		}

		void OnShowContextMenu(object sender, GeckoContextMenuEventArgs e)
		{
			var m = e.ContextMenu.MenuItems.Add("Edit Stylesheets in Stylizer", new EventHandler(OnOpenPageInStylizer));
			m.Enabled = !string.IsNullOrEmpty(GetPathToStylizer());

			e.ContextMenu.MenuItems.Add("Open Page in System Browser", new EventHandler(OnOpenPageInSystemBrowser));

            e.ContextMenu.MenuItems.Add("Copy Troubleshooting Information", new EventHandler(OnGetTroubleShootingInformation));
		}
        public void OnGetTroubleShootingInformation(object sender, EventArgs e)
        {
            //we can imagine doing a lot more than this... the main thing I wanted was access to the <link> paths for stylesheets,
            //as those can be the cause of errors if Bloom is using the wrong version of some stylesheet, and it might not do that
            //on a developer/ support-person computer.
            var builder = new StringBuilder();

            foreach (string label in ErrorReport.Properties.Keys)
            {
                 builder.AppendLine(label + ": " + ErrorReport.Properties[label] + Environment.NewLine);
            }
            
            builder.AppendLine();

            builder.AppendLine(File.ReadAllText(_url));
            Clipboard.SetText(builder.ToString());
            MessageBox.Show("Debugging information has been placed on your clipboard. You can paste it into an email.");
        }

		public void OnOpenPageInSystemBrowser(object sender, EventArgs e)
		{
			var  temp = Palaso.IO.TempFile.WithExtension(".htm");
			File.Copy(_url, temp.Path,true); //we make a copy because once Bloom leaves this page, it will delete it, which can be an annoying thing to have happen your editor
			Process.Start(temp.Path);
		}

		public void OnOpenPageInStylizer(object sender, EventArgs e)
		{
			string path = Path.GetTempFileName().Replace(".tmp",".html");
			File.Copy(_url, path,true); //we make a copy because once Bloom leaves this page, it will delete it, which can be an annoying thing to have happen your editor
			Process.Start(GetPathToStylizer(), path);
		}
		public static string GetPathToStylizer()
		{
			return FileLocator.LocateInProgramFiles("Stylizer.exe", false, new string[] { "Skybound Stylizer 5" });
		}



        void OnBrowser_DomClick(object sender, DomEventArgs e)
        {
          //this helps with a weird condition: make a new page, click in the text box, go over to another program, click in the box again.
            //it loses its focus.
            _browser.WebBrowserFocus.Activate();//trying to help the disappearing cursor problem
            
            EventHandler handler = OnBrowserClick;
            if (handler != null)
                handler(this, e);
        }


        void _browser_Navigating(object sender, GeckoNavigatingEventArgs e)
        {
			if (e.Uri.OriginalString.ToLower().StartsWith("http") && !e.Uri.OriginalString.ToLower().Contains("bloom"))
			{
				e.Cancel = true;
				Process.Start(e.Uri.OriginalString); //open in the system browser instead
			}

			Debug.WriteLine("Navigating " + e.Uri);
        }
		
		private void CleanupAfterNavigation(object sender, GeckoNavigatedEventArgs e)
		{
		

			//_setInitialZoomTimer.Enabled = true;

			Application.Idle += new EventHandler(Application_Idle);

           //NO. We want to leave it around for debugging purposes. It will be deleted when the next page comes along, or when this class is disposed of
//    		if(_tempHtmlFile!=null)
//    		{
//				_tempHtmlFile.Dispose();
//    			_tempHtmlFile = null;
//    		}
            //didn't seem to do anything:  _browser.WebBrowserFocus.SetFocusAtFirstElement();
    	}

		void Application_Idle(object sender, EventArgs e)
		{
            if (_disposed)
                return;

			Application.Idle -= new EventHandler(Application_Idle);

			ZoomToFullWidth();

			//this is the only safe way I've found to do a programatic zoom: trigger a resize event at idle time!
			//NB: if we instead directly call AutoZoom() here, we get a accessviolation pretty easily

			//But even though on my machine this doesn't crash, switching between books makes the resizing
			//stop working, so that even manually reziing the window won't get us a new zoom
/*			var original = Size.Height;
			Size = new Size(Size.Width, Size.Height + 1);
			Size = new Size(Size.Width, original);
	*/	}

    	public void Navigate(string url, bool cleanupFileAfterNavigating)
        {
            _url=url; //TODO: fix up this hack. We found that deleting the pdf while we're still showing it is a bad idea.
			if(cleanupFileAfterNavigating && !_url.EndsWith(".pdf"))
			{
				SetNewTempFile(TempFile.TrackExisting(url));
			}
            UpdateDisplay();
        }

        //NB: make sure the <base> is set correctly, 'cause you don't know where this method will 
        //save the file before navigating to it.
        public void Navigate(XmlDocument dom)
        {
        	_pageDom =(XmlDocument) dom;//.CloneNode(true); //clone because we want to modify it a bit

			/*	This doesn't work for the 1st book shown, or when you change book sizes.
			 * But it's still worth doing, becuase without it, we have this annoying re-zoom every time we look at different page.
			*/
			XmlElement body = (XmlElement) _pageDom.GetElementsByTagName("body")[0];
        	var scale = GetScaleToShowWholeWidthOfPage();
			if (scale > 0f)
			{
				body.SetAttribute("style", GetZoomCSS(scale));
			}
        	XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom);
        	SetNewTempFile(TempFile.CreateHtm5FromXml(dom));
			_url = _tempHtmlFile.Path;
            UpdateDisplay();
        }

		private static string GetZoomCSS(float scale)
		{
			//return "";
			return string.Format("-moz-transform: scale({0}); -moz-transform-origin: 0 0", scale.ToString(CultureInfo.InvariantCulture));
		}

		private void SetNewTempFile(TempFile tempFile)
    	{
     		if(_tempHtmlFile!=null)
    		{
				try
				{
					_tempHtmlFile.Dispose();
				}
				catch(Exception)
				{
						//not worth talking to the user about it. Just abandon it in the Temp directory.
#if DEBUG
					throw;
#endif
				}

    		}
    		_tempHtmlFile = tempFile;
    	}

    	

        private void UpdateDisplay()
        {
            if (!_browserIsReadyToNavigate)
                return;

            if (_url!=null)
            {
                _browser.Visible = true;
				_browser.Navigate(_url);
			}
        }



	    private void _afterValidatingTimer_Tick(object sender, EventArgs e)
		{
			_afterValidatingTimer.Enabled = false;
			//LoadPageDomFromBrowser();
			//AutoZoom();
		}
    	/// <summary>
		/// What's going on here: the browser is just /editting displaying a copy of one page of the document.
		/// So we need to copy any changes back to the real DOM.  
		/// </summary>
		private void LoadPageDomFromBrowser()
    	{
			if (_pageDom == null)
                return;

			// As of august 2012 textareas only occur in the Calendar
            if (_pageDom.SelectNodes("//textarea").Count > 0)
            {
                //This approach was to force an onblur so that we can get at the actual user-edited value.
                //This caused problems, with Bloom itself (the Shell) not knowing that it is active.
                //_browser.WebBrowserFocus.Deactivate();
                //_browser.WebBrowserFocus.Activate();

                // Now, we just do the blur directly. 
                var activeElement = _browser.Window.Document.ActiveElement;
                if (activeElement != null)
                    activeElement.Blur();
            }

    		var body = _browser.Document.GetElementsByTagName("body");
			if (body.Length ==0)	//review: this does happen... onValidating comes along, but there is no body. Assuming it is a timing issue.
				return;

			var content = body[0].InnerHtml;
    		XmlDocument dom;

			//todo: deal with exception that can come out of this
			try
			{
				dom = XmlHtmlConverter.GetXmlDomFromHtml(content, false);
				var bodyDom = dom.SelectSingleNode("//body");

				if (_pageDom == null)
					return;

				var destinationDomPage = _pageDom.SelectSingleNode("//body/div[contains(@class,'bloom-page')]");
				if (destinationDomPage == null)
					return;
				var expectedPageId = destinationDomPage["id"];

				var browserPageId = bodyDom.SelectSingleNode("//body/div[contains(@class,'bloom-page')]");
				if (browserPageId == null)
					return;//why? but I've seen it happen

				var thisPageId = browserPageId["id"];
				if(expectedPageId != thisPageId)
				{
					Palaso.Reporting.ErrorReport.NotifyUserOfProblem("Bloom encountered an error saving that page (unexpected page id)");
					return;
				}
				_pageDom.GetElementsByTagName("body")[0].InnerXml = bodyDom.InnerXml;

				var userModifiedStyleSheet = _browser.Document.StyleSheets.FirstOrDefault(s =>
					{
						var titleNode = s.OwnerNode.GetSingleElement("@title");
						if (titleNode == null)
							return false;
						return titleNode.NodeValue == "userModifiedStyles";
					});

				if (userModifiedStyleSheet != null)
				{
					/* why are we bothering to walk through the rules instead of just copying the html of the style tag? Because that doesn't
					 * actually get updated when the javascript edits the stylesheets of the page. Well, the <style> tag gets created, but
					 * rules don't show up inside of it. So
					 * this won't work: _pageDom.GetElementsByTagName("head")[0].InnerText = userModifiedStyleSheet.OwnerNode.OuterHtml;
					 */
					var styles = new StringBuilder();
					styles.AppendLine("<style title='userModifiedStyles' type='text/css'>");
					foreach (var cssRule in userModifiedStyleSheet.CssRules)
					{
						styles.AppendLine(cssRule.CssText);
					}
					styles.AppendLine("</style>");
					Debug.WriteLine("*User Modified Stylesheet in browser:"+styles);
					_pageDom.GetElementsByTagName("head")[0].InnerXml = styles.ToString();
				}

				//enhance: we have jscript for this: cleanup()... but running jscript in this method was leading the browser to show blank screen 
//				foreach (XmlElement j in _pageDom.SafeSelectNodes("//div[contains(@class, 'ui-tooltip')]"))
//				{
//					j.ParentNode.RemoveChild(j);
//				}
//				foreach (XmlAttribute j in _pageDom.SafeSelectNodes("//@ariasecondary-describedby | //@aria-describedby"))
//				{
//					j.OwnerElement.RemoveAttributeNode(j);
//				}

			}
			catch(Exception e)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e, "Sorry, Bloom choked on something on this page (invalid incoming html).\r\n\r\n+{0}", e);
				return;
			}

			

			try
			{ 
				XmlHtmlConverter.ThrowIfHtmlHasErrors(_pageDom.OuterXml);
			}
			catch (Exception e)
			{
				var exceptionWithHtmlContents = new Exception(content);
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e, "Sorry, Bloom choked on something on this page (validating page).\r\n\r\n+{0}", e.Message);
			}

		}

        private void OnUpdateDisplayTick(object sender, EventArgs e)
        {
            UpdateEditButtons();
        }

        /// <summary>
        /// This is needed if we want to save before getting a natural Validating event.
        /// </summary>
        public void ReadEditableAreasNow()
        {
			if (_url != "about:blank")
			{
		//		RunJavaScript("Cleanup()");
					//nb: it's important not to move this into LoadPageDomFromBrowser(), which is also called during validation, becuase it isn't allowed then
				LoadPageDomFromBrowser();
			}
        }

        public void Copy()
        {
            _browser.CopySelection();
        }

        /// <summary>
        /// add a jscript source file
        /// </summary>
        /// <param name="filename"></param>
        public void AddScriptSource(string filename)
        {
			if (!File.Exists(Path.Combine(Path.GetDirectoryName(_url), filename)))
				throw new FileNotFoundException(filename);

            GeckoDocument doc = WebBrowser.Document;
            var head = doc.GetElementsByTagName("head").First();
            GeckoScriptElement script = doc.CreateElement("script") as GeckoScriptElement;
            script.Type = "text/javascript";
            script.Src = filename;
            head.AppendChild(script);
        }

        public void AddScriptContent(string content)
        {
            GeckoDocument doc = WebBrowser.Document;
            var head = doc.GetElementsByTagName("head").First();
            GeckoScriptElement script = doc.CreateElement("script") as GeckoScriptElement;
            script.Type = "text/javascript";
            script.Text = content;
            head.AppendChild(script);
        }

        public void RunJavaScript(string script)
        {
			//NB: someday, look at jsdIDebuggerService, which has an Eval

			//TODO: work on getting the ability to get a return value: http://chadaustin.me/2009/02/evaluating-javascript-in-an-embedded-xulrunnergecko-window/ , EvaluateStringWithValue, nsiscriptcontext,  

 
        	WebBrowser.Navigate("javascript:void(" +script+")");
        	// from experimentation (at least with a script that shows an alert box), the script isn't run until this happens:
        	//var filter = new TestMessageFilter();
        	//Application.AddMessageFilter(filter);
				Application.DoEvents(); 


        	//NB: Navigating and Navigated events are never raised. I'm going under the assumption for now that the script blocks    	
       }



        /* snippets
         * 
         * //           _browser.WebBrowser.Navigate("javascript:void(document.getElementById('output').innerHTML = 'test')");
//            _browser.WebBrowser.Navigate("javascript:void(alert($.fn.jquery))");
//            _browser.WebBrowser.Navigate("javascript:void(alert($(':input').serialize()))");
            //_browser.WebBrowser.Navigate("javascript:void(document.getElementById('output').innerHTML = form2js('form','.',false,null))");
            //_browser.WebBrowser.Navigate("javascript:void(alert($(\"form\").serialize()))");

            */
    	public event EventHandler GeckoReady;

    	public void RaiseGeckoReady()
    	{
    		EventHandler handler = GeckoReady;
    		if (handler != null) handler(this, null);
    	}

		public void ShowHtml(string html)
		{
			_browser.LoadHtml(html);
		}

		private void Browser_Resize(object sender, EventArgs e)
		{
			ZoomToFullWidth();
		}

		private float GetScaleToShowWholeWidthOfPage()
		{
			if (_browser != null)
			{
				var div = _browser.Document.ActiveElement;
				if (div != null)
				{
					div = div.GetElements("//div[contains(@class, 'bloom-page')]").FirstOrDefault();
					if (div != null)
					{
						if (div.ScrollWidth > _browser.Width)
						{
							var widthWeNeed = div.ScrollWidth + 100 + 100/*for qtips*/;
							return ((float)_browser.Width) / widthWeNeed;
							
						}
						else
						{
							return 1.0f;
						}
					}
				}
			}
			return 0f;
		}

		private void ZoomToFullWidth()
		{
			var scale = GetScaleToShowWholeWidthOfPage();
			if(scale>0f)
			{
				SetZoom(scale);
			}
		}

		private void SetZoom(float scale)
		{
/*			//Dangerous. See https://bitbucket.org/geckofx/geckofx-11.0/issue/12/setfullzoom-doesnt-work
			//and if I get it to work (by only calling it from onresize, it stops working after you navigate
 
			var geckoMarkupDocumentViewer = _browser.GetMarkupDocumentViewer();
			if (geckoMarkupDocumentViewer != null)
			{
				geckoMarkupDocumentViewer.SetFullZoomAttribute(scale);
			}
*/
			//so we stick it in the css instead
			_browser.Document.Body.Style.CssText = string.Format("-moz-transform: scale({0}); -moz-transform-origin: 0 0", scale.ToString(CultureInfo.InvariantCulture));
			_browser.Window.ScrollTo(0,0);
		}
    }

}
