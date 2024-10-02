using SIL.IO;
using SIL.Windows.Forms.Extensions;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Bloom.Utils;
using Newtonsoft.Json;

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

        // props to provide to the react component
        public object Props;

        public static ReactControl Create(string _javascriptBundleName)
        {
            return new ReactControl() { JavascriptBundleName = _javascriptBundleName };
        }

        /* Ideally this would be private but I don't know how to do that without messing up winform Designer code that uses it */
        internal ReactControl()
        {
            InitializeComponent();
            BackColor = Color.White; // we use a different color in design mode
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
                _settingsDisplay.Text =
                    $"ReactControl{Environment.NewLine}{Environment.NewLine}Javascript Bundle: {_javascriptBundleName}{Environment.NewLine}{Environment.NewLine}Remember to call WireUpForWinforms() from the bundle.";
                return;
            }

            _settingsDisplay.Visible = false;

            var tempFile = MakeTempFile();

            // The Size setting is needed on Linux to keep the browser from coming up as a small
            // rectangle in the upper left corner...
            //_browser = new GeckoFxBrowser
            _browser = BrowserMaker.MakeBrowser();
            var browserControl = _browser;

            browserControl.Dock = DockStyle.Fill;
            browserControl.Location = new Point(3, 3);
            browserControl.Size = new Size(Width - 6, Height - 6);

            // currently this is used only in ReactDialog. E.g., "Report a problem".
            if (UseEditContextMenu)
                _browser.ContextMenuProvider = (adder) =>
                {
                    adder.Add(
                        L10NSharp.LocalizationManager.GetString("Common.Copy", "Copy"),
                        (s1, e1) =>
                        {
                            _browser.CopySelection();
                        }
                    );
                    adder.Add(
                        L10NSharp.LocalizationManager.GetString("Common.SelectAll", "Select all"),
                        (s1, e1) =>
                        {
                            _browser.SelectAll();
                        }
                    );
                    return true;
                };

            _browser.EnsureHandleCreated();

            // If the control gets added before it has navigated somewhere,
            // it shows as solid black, despite setting the BackColor to white.
            // So just don't show it at all until it contains what we want to see.
            // Note, this means any alerts that come up in the initialization code will freeze things,
            // since the user can't see them to respond. Don't use alerts in the initialization code!
            _browser.DocumentCompleted += (unused, args) =>
            {
                if (this.IsDisposed)
                    return;
                Controls.Add((UserControl)_browser); //review this cast

                // This allows us to bring up a react control/dialog with focus already set to a specific element.
                // For example, for BloomMessageBox, we set the Cancel button to have focus so the user
                // can hit the Enter key to close the dialog.
                // The first attempt to allow this behavior called root.click() in WireUpWinform.tsx
                // which then caused Browser.OnBrowser_DomClick to fire which called
                // WebBrowserFocus.Activate(). But that was causing the Shell to lose focus.
                // The problem was that Browser didn't have a Parent at that point.
                // By making the Activate call here, we seem to solve both issues. See BL-11092.
                _browser.ActivateFocussed();
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

            // We insert this as the initial background color of the HTML element
            // to prevent a flash of white while the React is rendering.
            var backColor = MiscUtils.ColorToHtmlCode(BackColor);

            // The 'body' height: auto rule keeps a winforms tab that only contains a ReactControl
            // from unnecessary scrolling.
            RobustFile.WriteAllText(
                tempFile.Path,
                $@"<!DOCTYPE html>
				<html style='height:100%'>
				<head>
					<title>ReactControl ({_javascriptBundleName})</title>
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
				<body style='margin:0; height:100%; display: flex; flex: 1; flex-direction: column; background-color:{backColor};'>
					<div id='reactRoot' style='height:100%'>Javascript should have replaced this. Make sure that the javascript bundle '{bundleNameWithExtension}' includes a single call to WireUpForWinforms()</div>
				</body>
				</html>"
            );
            return tempFile;
        }
    }
}
