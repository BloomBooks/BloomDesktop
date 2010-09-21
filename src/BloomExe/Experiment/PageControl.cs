using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using Palaso.Xml;
using Skybound.Gecko;

namespace Bloom
{
	public partial class PageControl : UserControl
	{
		protected GeckoWebBrowser _browser;
		bool _alreadyLoaded=false;
		protected string _docPath;
		private List<string> _sheetPaths = new List<string>();
		private string _currentPageId;

		public PageControl(bool useThumbNailMode)
		{
			InitializeComponent();
			_browser = new GeckoWebBrowser();
			_browser.Parent = this;
			_browser.Dock = DockStyle.Fill;
			this.Load += new EventHandler(Form1_Load);

		   }

		public string DocumentPath
		{
			set
			{
				_docPath = value;

				if (_alreadyLoaded)
				{
					LoadNow();
				}
			}
		}

		protected virtual void LoadNow()
		{
			XmlDocument dom = new XmlDocument();
			dom.Load(_docPath);
			XmlNamespaceManager namespaceManager = new XmlNamespaceManager(dom.NameTable);
			namespaceManager.AddNamespace("x", "http://www.w3.org/1999/xhtml");
			var head = dom.SelectSingleNode("//x:head", namespaceManager);
			AddSheets(dom, head);

			foreach(XmlElement node in  dom.SafeSelectNodes("//x:input", namespaceManager))
			{
				node.SetAttribute("onblur", "", "this.setAttribute('newValue',this.value);");
			}
			foreach (XmlElement node in dom.SafeSelectNodes("//x:textarea", namespaceManager))
			{
				node.SetAttribute("onblur", "", "this.setAttribute('newValue',this.value);");
			}

			foreach (XmlElement node in dom.SafeSelectNodes("//x:div[contains(@class, 'page')]", namespaceManager))
			{
				if(string.IsNullOrEmpty(_currentPageId))
				{
					_currentPageId = node.GetStringAttribute("id");
				}
				if (node.GetStringAttribute("id") != _currentPageId)
				{
					node.SetAttribute("style", "", "display:none");
				}
			}

			string tempPath = _docPath.Replace(".htm", "-"+this.Name+".htm");// Path.GetTempFileName() + ".htm";
			using(var writer = XmlWriter.Create(tempPath))
			{
				dom.WriteContentTo(writer);
				writer.Close();
			}
			_browser.Navigate(tempPath);

		}

		public void ShowPage(string id)
		{
			_currentPageId = id;
			LoadNow();
		}


		private void AddSheets(XmlDocument dom, XmlNode head)
		{
			foreach (var path in _sheetPaths)
			{
				var link = dom.CreateElement("link", "http://www.w3.org/1999/xhtml");
				link.SetAttribute("rel", "stylesheet");
				link.SetAttribute("href", "file://"+path);
				link.SetAttribute("type", "text/css");
				head.AppendChild(link);
			}
		}

		void Form1_Load(object sender, EventArgs e)
		{
			_alreadyLoaded =true;
			if(!string.IsNullOrEmpty(_docPath))
				LoadNow();
		}



		public void AddStyleSheet(string path)
		{
			_sheetPaths.Add(path);
		}

		public void RefreshContents()
		{
			if (_alreadyLoaded)
			{
				LoadNow();
			}
		}
	}

	public class EditPageControl : PageControl
	{
		public EditPageControl() : base(false)
		{
			this.Validating += new System.ComponentModel.CancelEventHandler(EditPageControl_Validating);
		}

		void EditPageControl_Validating(object sender, System.ComponentModel.CancelEventArgs e)
		{
			SaveHtml();
		}


		public void SaveHtml()
		{
			//this gives us just html, not xhtml //var page = _browser.Document.DocumentElement.InnerHtml;
			//_browser.SaveDocument(path, "application/xhtml+xml");
			XmlDocument dom = new XmlDocument();
			dom.Load(_docPath);
			XmlNamespaceManager namespaceManager = new XmlNamespaceManager(dom.NameTable);
			namespaceManager.AddNamespace("x", "http://www.w3.org/1999/xhtml");
			foreach (XmlElement node in dom.SafeSelectNodes("//x:input", namespaceManager))
			{
				var id = node.GetAttribute("id");
				node.SetAttribute("value", _browser.Document.GetElementById(id).GetAttribute("newValue") );
			}
			foreach (XmlElement node in dom.SafeSelectNodes("//x:textarea", namespaceManager))
			{
				var id = node.GetAttribute("id");
				if (string.IsNullOrEmpty(id))
				{
				   // Debug.Fail();
				}
				else
				{
					var value = _browser.Document.GetElementById(id).GetAttribute("newValue");
					if (!string.IsNullOrEmpty(value))
					{
						node.InnerText = value;
					}
				}
			}
			var tempPath = _docPath.Replace(".htm", "-edited.htm");
			dom.Save(tempPath);
			File.Replace(tempPath, _docPath, _docPath+ ".bak", true);
		}

	}
}
