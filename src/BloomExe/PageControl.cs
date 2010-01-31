using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Skybound.Gecko;
using System.Xml.Linq;

namespace BloomApp
{
	public partial class PageControl : UserControl
	{
		private GeckoWebBrowser _browser;
		bool _alreadyLoaded=false;
		private string _docPath;
		private List<string> _sheetPaths = new List<string>();

		public PageControl()
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

		private void LoadNow()
		{
			XmlDocument dom = new XmlDocument();
			dom.Load(_docPath);
			XmlNamespaceManager namespaceManager = new XmlNamespaceManager(dom.NameTable);
			namespaceManager.AddNamespace("x", "http://www.w3.org/1999/xhtml");
			var head = dom.SelectSingleNode("//x:head", namespaceManager);
			foreach (var path in _sheetPaths)
			{
				var link = dom.CreateElement("link", "http://www.w3.org/1999/xhtml");
				link.SetAttribute("rel", "stylesheet");
				link.SetAttribute("href", "file://"+path);
				link.SetAttribute("type", "text/css");
				head.AppendChild(link);
			}
			string tempPath = _docPath.Replace(".htm", "-"+this.Name+".htm");// Path.GetTempFileName() + ".htm";
			using(var writer = XmlWriter.Create(tempPath))
			{
				dom.WriteContentTo(writer);
				writer.Close();
			}
			_browser.Navigate(tempPath);
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
	}
}
