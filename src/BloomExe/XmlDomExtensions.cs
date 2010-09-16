﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace Bloom
{
	public static class XmlDomExtensions
	{
		public static XmlDocument StripXHtmlNameSpace(this XmlDocument node)
		{
			XmlDocument x = new XmlDocument();
			x.LoadXml(node.OuterXml.Replace("xmlns", "xmlnsNeutered"));
			return x;
		}

		public static void AddStyleSheet(this XmlDocument dom, string cssFilePath)
		{
			var head = dom.SelectSingleNodeHonoringDefaultNS("//head");
			AddSheet(dom, head, cssFilePath);
		}

		private static void AddSheet(this XmlDocument dom, XmlNode head, string cssFilePath)
		{
			var link = dom.CreateElement("link", "http://www.w3.org/1999/xhtml");
			link.SetAttribute("rel", "stylesheet");
			link.SetAttribute("href", "file://" + cssFilePath);
			link.SetAttribute("type", "text/css");
			head.AppendChild(link);
		}
	}
}