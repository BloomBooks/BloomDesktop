using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Palaso.Network;

namespace Bloom.WebLibraryIntegration
{
	/// <summary>
	/// This class represents an instance of a hyperlink which, when activated, causes a bloom book to be downloaded and opened.
	///
	/// Such a link looks like bloom://localhost/order?orderFile={path}, where path is appropriate urlencoded.
	///
	/// To allow Bloom to be automatically started when such a link is activated requires some registry entries:
	///
	/// The key HKEY_CLASSES_ROOT\bloom\shell\open\command must contain as its default value
	/// a string which is the path to Bloom.exe in quotes, followed by " %1". For example,
	///
	///     "C:\palaso\bloom-desktop\Output\Debug\Bloom.exe" "%1"
	///
	/// In addition, the HKEY_CLASSES_ROOT\bloom key must have a default value of "URL:BLOOM Protocol" and another
	/// string value called "URL Protocol" (no value). (Don't ask me why...Alistair may know.)
	///
	/// One way to set these up on a developer machine is to edit the file bloom link.reg in the project root directory
	/// so that it contains the correct path to your exe, then double-click it.
	///
	/// When a properly-formed link is followed, a new instance of Bloom is started up and passed the URL as its one
	/// command-line argument. This is recognized and handled in Program.Main().
	///
	/// Todo: Make installer set up the registry entries.
	/// Todo Linux: probably something quite different needs to be done to make Bloom the handler for bloom:// URLs.
	/// </summary>
	public class BloomLinkArgs
	{
		/// <summary>Internet Access Protocol identifier that indicates that this is a FieldWorks link: the bit before the ://</summary>
		public const string kBloomScheme = "bloom";
		/// <summary>Indicates that this link should be handled by the local computer</summary>
		public const string kLocalHost = "localhost";
		/// <summary>Command-line argument: This is redundant for now, but just in case Bloom comes to handle any other kinds of URLs</summary>
		public const string kOrder = "order";
		/// <summary>
		/// The one-and-only argument in a bloom order link: the path to the file.
		/// </summary>
		public const string kOrderFile = "orderFile";
		public const string kBloomUrlPrefix = kBloomScheme + "://" + kLocalHost + "/" + kOrder + "?";

		/// <summary>
		/// The url extracted from the overall order where we can find the bloom book order file.
		/// </summary>
		public string OrderUrl { get; set; }

		public BloomLinkArgs(string url)
		{
			if (!url.StartsWith(kBloomUrlPrefix))
				throw new ArgumentException(String.Format("unrecognized BloomLinkArgs URL string: {0}", url));
			// I think we can't use the standard HttpUtility because we are trying to stick to the .NET 4.0 Client profile
			var query = HttpUtilityFromMono.UrlDecode(url.Substring(kBloomUrlPrefix.Length));
			var parts = query.Split('=');
			if (parts.Length != 2 || parts[0] != kOrderFile)
				throw new ArgumentException(String.Format("badly formed BloomLinkArgs URL string: {0}", url));
			OrderUrl = parts[1];
		}
	}
}
