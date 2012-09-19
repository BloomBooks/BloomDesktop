using System;
using System.Xml;
using Bloom;
using Palaso.IO;

namespace Bloom_ChorusPlugin
{
	/// <summary>
	/// Given an html path, this can give it as xhtml, let you make changes,
	/// and then give it back as html when you're done.
	/// </summary>
	public class HtmlFileForMerging : IDisposable
	{
		private readonly string _pathToHtml;
		private TempFile xmlFile;

		public HtmlFileForMerging(string pathToHtml)
		{
			_pathToHtml = pathToHtml;
			xmlFile = TempFile.WithExtension("xhtm");
		}

		public string GetPathToXHtml()
		{
			XmlHtmlConverter.GetXmlDomFromHtmlFile(_pathToHtml).Save(xmlFile.Path);
			return xmlFile.Path;
		}

		public void SaveHtml()
		{
			var dom = new XmlDocument();
			dom.Load(xmlFile.Path);
			XmlHtmlConverter.SaveDOMAsHtml5(dom, _pathToHtml);
		}

		public void Dispose()
		{
			xmlFile.Dispose();
		}
	}
}
