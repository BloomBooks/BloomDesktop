using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Gecko;
using Palaso.IO;

namespace Bloom.Edit
{
    /// <summary>
    /// This class manages the far-right pane which initially is contains an accordion
    /// control with child pages for the leveled reader tools, the decodable reader tools, and
    /// elements that can be added to a page.
    /// 
    /// By design it is as thin a wrapper as possible around the browser. Eventually we expect to merge
    /// all the controls on the main window into a single web browser control.
    /// </summary>
    public partial class ReaderToolsView : UserControl, IReaderToolsView
    {
        public GeckoWebBrowser Browser { get; private set; }
        private ReaderToolsModel _model;

        public delegate ReaderToolsView Factory();//autofac uses this

        public ReaderToolsView(ReaderToolsModel model)
        {
            _model = model;
            _model.View = this;
            InitializeComponent();
            Browser = new GeckoWebBrowser();
            Browser.Dock = DockStyle.Fill;
            var path = FileLocator.GetFileDistributedWithApplication("BloomBrowserUI/bookEdit/html", "ReaderTools.htm");
            Browser.HandleCreated += (sender, args) =>
            {
                Browser.Navigate("file:///" + path);
                Browser.DocumentCompleted += Browser_Navigated; // Once we're headed for the right path, we want to know when we arrive.
            };
            Browser.DomClick += BrowserOnDomClick;
            Controls.Add(Browser);
            // This is supposed to make the background of the actual HTML match the color set on this parent control.
            // It doesn't work, probably because there is some rule in HTML about the overall background color.
            // Currently I work around this by setting the same color in the HTML itself.
            BackColorChanged += (sender, args) => Browser.BackColor = this.BackColor;
        }

        void Browser_Navigated(object sender, EventArgs e)
        {
            Browser.DocumentCompleted -= Browser_Navigated;
            AttemptPostNavigationInit();
        }

        /// <summary>
        /// Once navigation has progressed far enough so that we can locate elements of the DOM by ID,
        /// we can update a few of them to reflect the current state of things.
        /// This is currently triggered by the best event we can find, but it is not entirely reliable.
        /// If we can't yet get elements by ID, we add an idle event and keep trying until we can.
        /// </summary>
        private void AttemptPostNavigationInit()
        {
            if (Browser.DomDocument.GetElementById("wordList") != null)
            {
                Application.Idle -= RunPostNavigationInitWhenIdle; // Once we can do the init, we don't need this callback
                _model.PostNavigationInitialize();
                return;
            }
            Application.Idle += RunPostNavigationInitWhenIdle;
        }

        void RunPostNavigationInitWhenIdle(object sender, EventArgs e)
        {
            AttemptPostNavigationInit();
        }

        /// <summary>
        /// Triggered by clicks in the dom, look for ones on elements with ids and let the model handle them.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="geckoDomEventArgs"></param>
        private void BrowserOnDomClick(object sender, DomEventArgs geckoDomEventArgs)
        {
            var element = geckoDomEventArgs.Target.CastToGeckoElement();
            var idAttr = element.Attributes["id"];
            if (idAttr != null)
            {
                _model.ControlClicked(idAttr.NodeValue);
                return;
            }
        }
    }

    /// <summary>
    /// Ways the model can call back to the real gui.
    /// </summary>
    interface IReaderToolsView
    {
        /// <summary>
        /// Currently methods in the model that use this are typically overridden in a test stub for test purposes.
        /// </summary>
        GeckoWebBrowser Browser { get; }
    }
}
