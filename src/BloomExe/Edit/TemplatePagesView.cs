using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.web;
using Gecko;
using L10NSharp;
using Palaso.Xml;

namespace Bloom.Edit
{
	/// <summary>
	/// This class manages the Template Pages view which appears on the far right when authoring a book to which whole
	/// new pages can be added.
	/// </summary>
	/// <remarks>This class has an uncomfortable amount of code that is similar to WebThumbNailList since
	/// we converted it to directly display the HTML of the page templates. But there are enough subtle
	/// differences to make it hard to share code: this list of thumbnails has no left/right pairing,
	/// no drag-and-drop re-ordering, no page-numbering, no selected item. </remarks>
	public partial class TemplatePagesView : UserControl
	{
		private readonly BookSelection _bookSelection;
		private readonly TemplateInsertionCommand _templateInsertionCommand;
		private Browser _browser;
		private Dictionary<string, IPage> _pageMap = new Dictionary<string, IPage>();

		public delegate TemplatePagesView Factory();//autofac uses this

		public TemplatePagesView(BookSelection bookSelection, TemplateInsertionCommand templateInsertionCommand, NavigationIsolator isolator)
		{
			_bookSelection = bookSelection;
			_templateInsertionCommand = templateInsertionCommand;

			this.Font = SystemFonts.MessageBoxFont;
			InitializeComponent();

			if (!ReallyDesignMode)
			{
				_browser = new Browser();
				this._browser.BackColor = Color.DarkGray;
				this._browser.Dock = DockStyle.Fill;
				this._browser.Name = "_browser";
				this._browser.TabIndex = 0;
				_browser.ScaleToFullWidthOfPage = false;
				_browser.VerticalScroll.Visible = false;
				_browser.Isolator = isolator;
				this.Controls.Add(_browser);
				_browser.OnBrowserClick += OnBrowserClick;
				_browser.BringToFront(); // For docking to work right it must be 'in front' of the top-docked control
			}
		}

		private void OnBrowserClick(object sender, EventArgs e)
		{
			var ge = e as DomEventArgs;
			var target = (GeckoHtmlElement)ge.Target.CastToGeckoElement();
			while (target != null && (target.Attributes["class"] == null || target.Attributes["class"].NodeValue != "gridItem"))
				target = target.Parent;
			if (target == null)
				return;
			var id = target.Attributes["id"].NodeValue;
			var page = _pageMap[id] as Page;
			_templateInsertionCommand.Insert(page);
		}

		protected bool ReallyDesignMode
		{
			get
			{
				return (base.DesignMode || GetService(typeof(IDesignerHost)) != null) ||
					(LicenseManager.UsageMode == LicenseUsageMode.Designtime);
			}
		}

		public void Update()
		{
			//we don't want to spend time setting up our thumnails and such when actually the
			//user is on another tab right now
			Debug.Assert(Visible, "Shouldn't be slowing things down by calling this when it isn't visible");

			if (_bookSelection.CurrentSelection == null)
			{
			   Clear();
				return;
			}

			var templateBook = _bookSelection.CurrentSelection.FindTemplateBook();
			if(templateBook ==null)
			{
				Clear();
			}
			else
			{
				var pages = templateBook.GetTemplatePages();
				SetPages(pages);
			}
		}

		private void SetPages(IEnumerable<IPage> pages)
		{
			var frame = BloomFileLocator.GetFileDistributedWithApplication("BloomBrowserUI", "bookEdit", "BookPagesThumbnailList",
				"TemplatePagesThumbnailList.htm");
			var backColor = ColorToHtmlCode(BackColor);
			var htmlText = System.IO.File.ReadAllText(frame, Encoding.UTF8).Replace("DarkGray", backColor);
			var dom = new HtmlDom(htmlText);
			dom = _bookSelection.CurrentSelection.GetHtmlDomReadyToAddPages(dom);
			var pageDoc = dom.RawDom;
			pageDoc.AddStyleSheet(BloomFileLocator.GetFileDistributedWithApplication("BloomBrowserUI", "bookPreview", "css", @"previewMode.css").ToLocalhost());
			dom.AddJavascriptFile(BloomFileLocator.GetFileDistributedWithApplication("BloomBrowserUI", "bookPreview", "js", "bloomPreviewBootstrap.js").ToLocalhost());
			//dom.AddJavascriptFile(BloomFileLocator.GetFileDistributedWithApplication("BloomBrowserUI", "bookEdit", "js", "bloomEditing.js").ToLocalhost());
			var body = pageDoc.GetElementsByTagName("body")[0];
			var itemParent = body; // too simplistic?

			_pageMap.Clear();
			foreach (var page in pages)
			{
				var node = page.GetDivNodeForThisPage();
				if (node == null)
					continue; // or crash? How can this happen?
				var pageThumbnail = pageDoc.ImportNode(node, true);
				var cellDiv = pageDoc.CreateElement("div");
				cellDiv.SetAttribute("class", "gridItem");
				var itemId = ItemId(page);
				cellDiv.SetAttribute("id", itemId);
				_pageMap[itemId] = page;
				itemParent.AppendChild(cellDiv);


				//we wrap our incredible-shrinking page in a plain 'ol div. This makes it more
				// like the WebThumbNailList...we may eventually share more code.
				// Here we don't need to show a selection so it has no more specific purpose.
				var pageContainer = pageDoc.CreateElement("div");
				pageContainer.SetAttribute("class", "pageContainer");
				pageContainer.AppendChild(pageThumbnail);


				// And here it gets fragile. I can't figure out a CSS that will center the
				// page without knowing how wide it is. Copying the page style to the page container
				// allows us to apply appropriate classes to make them right.
				var pageClasses = pageThumbnail.GetStringAttribute("class");

				//enhance: there is doubtless code somewhere else that picks these size/orientations out elegantly
				if (pageClasses.ToLower().Contains("a5portrait"))
				{
					pageContainer.SetAttribute("class", "pageContainer A5Portrait");
				}
				if(pageClasses.ToLower().Contains("a6portrait"))
				{
					pageContainer.SetAttribute("class", "pageContainer A6Portrait");
				}
				if (pageClasses.ToLower().Contains("a4portrait"))
				{
					pageContainer.SetAttribute("class", "pageContainer A4Portrait");
				}
				if (pageClasses.ToLower().Contains("a4landscape"))
				{
					pageContainer.SetAttribute("class", "pageContainer A4Landscape");
				}

				cellDiv.AppendChild(pageContainer);
				var captionDiv = pageDoc.CreateElement("div");
				captionDiv.SetAttribute("class", "thumbnailCaption");
				cellDiv.AppendChild(captionDiv);
				var caption = page.Caption; // never want to use page numbers for template pages.
				captionDiv.InnerText = LocalizationManager.GetDynamicString("Bloom",
					"EditTab.ThumbnailCaptions." + caption, caption);
			}
			//_browser.WebBrowser.DocumentCompleted += WebBrowser_DocumentCompleted;
			//_verticalScrollDistance = _browser.VerticalScrollDistance;
			_browser.Navigate(dom, null);
		}

		private static string ItemId(IPage page)
		{
			return "template-" + page.Id;
		}

		string ColorToHtmlCode(Color color)
		{
			// thanks to http://stackoverflow.com/questions/982028/convert-net-color-objects-to-hex-codes-and-back
			return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
		}

		public void Clear()
		{
			SetPages(new IPage[] { });
		}

		public new bool Enabled
		{
			set { _browser.Enabled = value; }
		}
	}
}
