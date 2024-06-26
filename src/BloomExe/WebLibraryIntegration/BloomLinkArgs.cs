﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using SIL.Network;

namespace Bloom.WebLibraryIntegration
{
    /// <summary>
    /// This class represents an instance of a hyperlink which, when activated, causes a bloom book to be downloaded and opened.
    ///
    /// Such a link looks like bloom://localhost/order?orderFile={path}, where path is appropriate urlencoded.
    /// Optionally, it may be followed by &title={title}, where title is the book title (urlencoded).
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

        //todo fix comment
        /// <summary>
        /// The name "orderFile" is a hold over from when we used to use an actual file with .BloomBookOrder extension.
        /// Those are now obsolete, but the URL maintains its general form so as not to break downloads in older Blooms.
        /// Now, "orderFile" is the prefix of the book's folder on S3. For example,
        /// andrew_polk%40sil.org%2f7195f6af-caa2-44b1-8aab-df0703ab5c4a%2f
        /// where %2f is the url encoding for a slash.
        /// </summary>
        public const string kOrderFile = "orderFile";
        public const string kBloomUrlPrefix =
            kBloomScheme + "://" + kLocalHost + "/" + kOrder + "?";

        /// <summary>
        /// The url extracted from the overall order where we can find the bloom book order file.
        /// </summary>
        public string OrderUrl { get; set; }
        public string Title { get; set; }

        public bool ForEdit { get; }

        public string DatabaseId { get; }

        public BloomLinkArgs(string url)
        {
            if (!url.StartsWith(kBloomUrlPrefix))
                throw new ArgumentException(
                    String.Format("unrecognized BloomLinkArgs URL string: {0}", url)
                );
            // I think we can't use the standard HttpUtility because we are trying to stick to the .NET 4.0 Client profile
            var queryData = url.Substring(kBloomUrlPrefix.Length);
            var qparams = queryData.Split('&');
            var parts = qparams[0].Split('=');
            if (parts.Length != 2 || parts[0] != kOrderFile)
                throw new ArgumentException(
                    String.Format("badly formed BloomLinkArgs URL string: {0}", url)
                );
            OrderUrl = HttpUtility.UrlDecode(parts[1]);
            if (qparams.Length > 1 && qparams[1].StartsWith("title="))
                Title = HttpUtility.UrlDecode(qparams[1].Substring("title=".Length));
            if (qparams.Any(x => x == "forEdit=true"))
                ForEdit = true;
            if (qparams.Any(x => x.StartsWith("database-id=")))
                DatabaseId = qparams
                    .First(x => x.StartsWith("database-id="))
                    .Substring("database-id=".Length);
        }
    }
}
