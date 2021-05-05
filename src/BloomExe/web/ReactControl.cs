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
		private string _reactComponentName;
		private string _urlQueryString;
		// props to provide to the react component
		public object Props;

		public ReactControl()
		{
			InitializeComponent();
			BackColor = Color.White;// we use a different color in design mode
		}

		[Browsable(true), Category("Setup")]
		public string ReactComponentName
		{
			get { return _reactComponentName; }
			set { _reactComponentName = value; }
		}

		[Browsable(true), Category("Setup")]
		public string UrlQueryString
		{
			get { return _urlQueryString; }
			set { _urlQueryString = value; }
		}

		public bool UseEditContextMenu;

		private void ReactControl_Load(object sender, System.EventArgs e)
		{
			if (this.DesignModeAtAll())
			{
				_settingsDisplay.Visible = true;
				_settingsDisplay.Text = $"ReactControl{Environment.NewLine}{Environment.NewLine}React Component Name: {_reactComponentName}{Environment.NewLine}{Environment.NewLine}Remember to add the component to the map in WireUpReact.ts";
				return;
			}

			_settingsDisplay.Visible = false;

			var tempFile = TempFile.WithExtension("htm");
			tempFile.Detach(); // the browser control will clean it up

			var props = Props==null ? "{}":JsonConvert.SerializeObject(Props);

			RobustFile.WriteAllText(tempFile.Path, $@"<!DOCTYPE html>
				<html>
				<head>
					<meta charset = 'UTF-8' />
					<script src = '/commonBundle.js' ></script>
					<script src = '/wireUpBundle.js' ></script>
					<script>
						window.onload = () => {{
							const rootDiv = document.getElementById('reactRoot');
							window.wireUpReact(rootDiv,'{_reactComponentName}', {props});
						}};
					</script>
				</head>
				<body>
					<div id='reactRoot' class='{_reactComponentName}'>Component should replace this</div>
				</body>
				</html>");


			// The Size setting is needed on Linux to keep the browser from coming up as a small
			// rectangle in the upper left corner...
			var browser = new Browser
			{
				Dock = DockStyle.Fill,
				Location = new Point(3, 3),
				Size = new Size(Width - 6, Height - 6),
				BackColor = Color.White
			};
			if (UseEditContextMenu)
				browser.ContextMenuProvider = args => {
					args.ContextMenu.MenuItems.Add(new MenuItem(L10NSharp.LocalizationManager.GetString("Common.Copy", "Copy"),
							(s1, e1) => { browser.WebBrowser.CopySelection(); }));
					args.ContextMenu.MenuItems.Add(new MenuItem(L10NSharp.LocalizationManager.GetString("Common.SelectAll", "Select all"),
							(s1, e1) => { browser.WebBrowser.SelectAll(); }));
					return true;
				};

			var dummy = browser.Handle; // gets the WebBrowser created

			// If the control gets added before it has navigated somewhere,
			// it shows as solid black, despite setting the BackColor to white.
			// So just don't show it at all until it contains what we want to see.
			browser.WebBrowser.DocumentCompleted += (unused, args) =>
			{
				Controls.Add(browser);
			};
			browser.NavigateToTempFileThenRemoveIt(tempFile.Path, _urlQueryString);
		}
	}
}
