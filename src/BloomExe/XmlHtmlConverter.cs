using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using BloomTemp;
using Palaso.Xml;
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
			using (var temp = new TempFile())
			{
				File.WriteAllText(temp.Path, content, Encoding.UTF8);
				using (var tidy = TidyManaged.Document.FromFile(temp.Path))
				{
					tidy.ShowWarnings = false;
					tidy.Quiet = true;
					tidy.WrapAt = 0;// prevents textarea wrapping.
					tidy.AddTidyMetaElement = false;
					tidy.OutputXml = true;
					tidy.CharacterEncoding = EncodingType.Utf8;
					tidy.InputCharacterEncoding = EncodingType.Utf8;
					tidy.OutputCharacterEncoding = EncodingType.Utf8;
					tidy.DocType = DocTypeMode.Omit; //when it supports html5, then we will let it out it
					var errors = tidy.CleanAndRepair();
					if (!string.IsNullOrEmpty(errors))
					{
						throw new ApplicationException(errors + "\r\n\r\n" + content);
					}
					var newContents = tidy.Save();
					try
					{
						newContents = newContents.Replace("&nbsp;", "&#160;"); //REVIEW: 1) are there others? &amp; and such are fine.  2) shoul we to convert back to &nbsp; on save?
						dom.LoadXml(newContents);
					}
					catch (Exception e)
					{
						var exceptionWithHtmlContents = new Exception(e.Message + "\r\n\r\n" + newContents);
						throw exceptionWithHtmlContents;
					}
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


		/// <summary>
		/// If an element has empty contents, like <textarea></textarea>, browsers will sometimes drop the end tag, so that now, when we read it back into xml,
		/// anything following the <textarea> will be interpreted as part of the <textarea>!  This method makes sure such tags are never totally empty.
		/// </summary>
		/// <param name="dom"></param>
		public static void MakeXmlishTagsSafeForInterpretationAsHtml(XmlDocument dom)
		{
			foreach (XmlElement node in dom.SafeSelectNodes("//textarea"))
			{
				if (!node.HasChildNodes)
				{
					node.AppendChild(node.OwnerDocument.CreateTextNode(""));
				}
			}
			foreach (XmlElement node in dom.SafeSelectNodes("//div"))
			{
				if (!node.HasChildNodes)
				{
					node.AppendChild(node.OwnerDocument.CreateTextNode(""));
				}
			}

			foreach (XmlElement node in dom.SafeSelectNodes("//p")) //without  this, an empty paragraph suddenly takes over the subsequent elements. Browser sees <p></p> and thinks... let's just make it <p>, shall we? Stupid optional-closing language, html is....
			{
				if (!node.HasChildNodes)
				{
					node.AppendChild(node.OwnerDocument.CreateTextNode(""));
				}
			}

			foreach (XmlElement node in dom.SafeSelectNodes("//span"))
			{
				if (string.IsNullOrEmpty(node.InnerText) && node.ChildNodes.Count == 0)
				{
					node.InnerText = " ";
				}
			}

			foreach (XmlElement node in dom.SafeSelectNodes("//script"))
			{
				if (string.IsNullOrEmpty(node.InnerText) && node.ChildNodes.Count == 0)
				{
					node.InnerText = " ";
				}
			}
		}
	}
}
