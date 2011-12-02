using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using BloomTemp;
using TidyManaged;


namespace Bloom
{
	public class XmlHtmlConverter
	{

		/// <summary>
		///
		/// </summary>
		/// <param name="content"></param>
		/// <exception cref="">Throws if there are parsing errors</exception>
		/// <returns></returns>
		public static XmlDocument GetXmlDomFromHtmlFile(string path)
		{
			return GetXmlDomFromHtml(File.ReadAllText(path));
		}

		/// <summary>
		///
		/// </summary>
		/// <param name="content"></param>
		/// <exception cref="">Throws if there are parsing errors</exception>
		/// <returns></returns>
		public static XmlDocument GetXmlDomFromHtml(string content)
		{
			var dom = new XmlDocument();
			using (var tidy =  TidyManaged.Document.FromString(content))
			{

				//TODO get in         <meta charset="UTF-8">



				tidy.ShowWarnings = false;
				tidy.Quiet = true;
				tidy.AddTidyMetaElement = false;
				tidy.OutputXml = true;
				tidy.DocType = DocTypeMode.Omit;//when it supports html5, then we will let it out it

				//not using tidy.writeback becuase it's unclear what it would do in the case of an error... we'd rather not write back if it's garbage

				using (var log = new MemoryStream())
				{
					tidy.CleanAndRepair(log);
					string errors = ASCIIEncoding.ASCII.GetString(log.ToArray());
					if(!string.IsNullOrEmpty(errors))
					{
						throw new ApplicationException(errors);
					}
				}
				using (var temp = new TempFile())
				{
					tidy.Save(temp.Path);
					dom.Load(temp.Path);
				}
			}
			return dom;
		}

		/// <summary>
		/// Beware... htmltidy doesn't consider such things as a second <body> element to warrant any more than a "warning", so this won't throw!
		/// </summary>
		/// <param name="content"></param>
		public static void ThrowIfHtmlHasErrors(string content)
		{
			using (var tidy = TidyManaged.Document.FromString(content))
			{
				tidy.ShowWarnings = false;
				tidy.Quiet = true;
				tidy.AddTidyMetaElement = false;
				tidy.OutputXml = true;
				tidy.DocType = DocTypeMode.Omit;//when it supports html5, then we will let it out it

				using (var log = new MemoryStream())
				{
					tidy.CleanAndRepair(log);
					string errors = ASCIIEncoding.ASCII.GetString(log.ToArray());
					if (!string.IsNullOrEmpty(errors))
					{
						throw new ApplicationException(errors);
					}
				}
			}
		}
	}
}
