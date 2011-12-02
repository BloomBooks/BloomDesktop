using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using BloomTemp;
using Mark.Tidy;

namespace Bloom
{
	public class XmlHtmlConverter
	{
		public static XmlDocument GetXmlDomFromHtmlFile(string path)
		{
			return GetXmlDomFromHtml(File.ReadAllText(path));
		}

		public static XmlDocument GetXmlDomFromHtml(string content)
		{
			var dom = new XmlDocument();
			using (var tidy = new Mark.Tidy.Document(content))
			{

				//TODO get in         <meta charset="UTF-8">



				tidy.ShowWarnings = false;
				tidy.Quiet = true;
				tidy.AddTidyMetaElement = false;
				tidy.OutputXml = true;
				tidy.DocType = DocTypeMode.Omit;//when it supports html5, then we will let it out it
				tidy.CleanAndRepair();
				using (var temp = new TempFile())
				{
					tidy.Save(temp.Path);
					dom.Load(temp.Path);
				}
			}
			return dom;
		}
	}
}
