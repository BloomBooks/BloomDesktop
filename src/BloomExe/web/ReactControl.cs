using SIL.IO;
using SIL.Windows.Forms.Extensions;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Windows.Forms.Extensions;

namespace Bloom.web
{
	/// <summary>
	/// Hosts a Web Browser rooted by the named React component
	/// </summary>
	// Possible future enhancement: wouldn't be hard to add the ability to pass
	// an object as the props for the control. This would be helpful in
	// cases where supplying the props from the parent c# would mean we didn't
	// have to create an API for this component. However, since we eventually
	// want to get rid of WinForms entirely, it's not yet clear to me if we
	// would eventually have to create that api anyways? (Could be cases where
	// the eventual JS/TS parent control would have the values needed for the props
	// without an API existing for that same data. As I say, unclear).

	public partial class ReactControl : UserControl
	{
		private string _javascriptBundleName;
		private string _reactComponentName;
		// props to provide to the react component
		public object Props;

		public static ReactControl Create(string _javascriptBundleName)
		{
			return new ReactControl()
			{
				JavascriptBundleName = _javascriptBundleName
			};
		}

		/* Ideally this would be private but I don't know how to do that without messing up winform Designer code that uses it */
		internal ReactControl()
		{
			InitializeComponent();
			BackColor = Color.White;// we use a different color in design mode
		}

		[Browsable(true), Category("Setup")]
		public string JavascriptBundleName
		{
			get { return _javascriptBundleName; }
			set { _javascriptBundleName = value; }
		}

		public bool UseEditContextMenu;
		private Browser _browser;

		private void ReactControl_Load(object sender, System.EventArgs e)
		{
			if (this.DesignModeAtAll())
			{
				_settingsDisplay.Visible = true;
				_settingsDisplay.Text = $"ReactControl{Environment.NewLine}{Environment.NewLine}Javascript Bundle: {_javascriptBundleName}{Environment.NewLine}{Environment.NewLine}Remember to call WireUpForWinforms() from the bundle.";
				return;
			}

			_settingsDisplay.Visible = false;

			var tempFile = MakeTempFile();


			// The Size setting is needed on Linux to keep the browser from coming up as a small
			// rectangle in the upper left corner...
			_browser = new Browser
			{
				Dock = DockStyle.Fill,
				Location = new Point(3, 3),
				Size = new Size(Width - 6, Height - 6),
				BackColor = Color.White
				
			};
			// This is mainly so that the mailTo: link in the Team Collection settings panel
			// works with the user's default mail program rather than whatever GeckoFx60 does,
			// which seems to be very buggy (BL-10050). However, I'm pretty sure there are
			// no external links or mailTos in any of our ReactControls that we want the
			// browser to handle itself, so for now, I'm making them all use the more reliable
			// code in Bloom. This means, for example, that the "See how it works here" link
			// will open the URL in the user's default browser.
			_browser.OnBrowserClick += Browser.HandleExternalLinkClick;
			if (UseEditContextMenu)
				_browser.ContextMenuProvider = args => {
					args.ContextMenu.MenuItems.Add(new MenuItem(L10NSharp.LocalizationManager.GetString("Common.Copy", "Copy"),
							(s1, e1) => { _browser.WebBrowser.CopySelection(); }));
					args.ContextMenu.MenuItems.Add(new MenuItem(L10NSharp.LocalizationManager.GetString("Common.SelectAll", "Select all"),
							(s1, e1) => { _browser.WebBrowser.SelectAll(); }));
					return true;
				};

			var dummy = _browser.Handle; // gets the WebBrowser created

			// If the control gets added before it has navigated somewhere,
			// it shows as solid black, despite setting the BackColor to white.
			// So just don't show it at all until it contains what we want to see.
			_browser.WebBrowser.DocumentCompleted += (unused, args) =>
			{
				Controls.Add(_browser);
			};
			_browser.NavigateToTempFileThenRemoveIt(tempFile.Path);
		}

		// If given the localization changed event, the control will automatically reload
		// when the event is raised.
		public void SetLocalizationChangedEvent(LocalizationChangedEvent localizationChangedEvent)
		{
			localizationChangedEvent.Subscribe(unused =>
			{
				Reload();
			});
		}

		public void Reload()
		{
			if (_browser == null)
				return;
			var tempFile = MakeTempFile();
			_browser.NavigateToTempFileThenRemoveIt(tempFile.Path);
		}

		private TempFile MakeTempFile()
		{
			var tempFile = TempFile.WithExtension("htm");
			tempFile.Detach(); // the browser control will clean it up

			var props = Props == null ? "{}" : JsonConvert.SerializeObject(Props);

			if (_javascriptBundleName == null)
			{
				throw new ArgumentNullException("React Control needs a _javascriptBundleName");
			}

			var bundleNameWithExtension = _javascriptBundleName;
			if (!bundleNameWithExtension.EndsWith(".js"))
			{
				bundleNameWithExtension += ".js";
			}

			RobustFile.WriteAllText(tempFile.Path, $@"<!DOCTYPE html>
				<html style='height:100%'>
				<head>
					<meta charset = 'UTF-8' />
					<script src = '/commonBundle.js' ></script>
                    <script src = '/{bundleNameWithExtension}'></script>
					<script>
						window.onload = () => {{
							const rootDiv = document.getElementById('reactRoot');
							window.wireUpRootComponentFromWinforms(rootDiv, {props});
						}};
					</script>					
				</head>
				<body style='margin:0; height:100%'>
					<div id='reactRoot' style='height:100%'>Javascript should have replaced this. Make sure that the javascript bundle '{bundleNameWithExtension}' includes a single call to WireUpForWinforms()</div>
				</body>
				</html>");
			return tempFile;
		}
	}
}
