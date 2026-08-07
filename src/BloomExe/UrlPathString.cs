using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;

namespace Bloom
{
    /// <summary>
    /// A wrapper around string designed to reduced bugs introduced by losing track of the encoded/unencoded state of a string.
    /// It does this by requiring users to specify what they are putting in/getting out, and it keeps track.
    /// </summary>
    /// <remarks>
    /// ENCODING CONVENTIONS FOR FILE NAMES STORED IN A BOOK
    ///
    /// Search for "encoding conventions" to find the places that depend on this.
    ///
    /// Not everything in a book that names a file is URL-encoded, and which is which is not
    /// guessable, so it is written down here once.
    ///
    /// URL-ENCODED (call CreateFromUrlEncodedString to read them):
    ///   - img @src, video source @src, iframe @src for widgets: these are real URLs. The
    ///     browser resolves them and asks our server for them, and the server decodes the path
    ///     exactly once, so they have to arrive encoded.
    ///   - data-backgroundaudio (the music tool). Surprisingly this one IS encoded, unlike its
    ///     neighbours below: the toolbox writes it through
    ///     ToolboxToolReactAdaptor.encodeAndSetPageAttr and reads it back through
    ///     getBloomPageAttrDecoded, which are encodeURIComponent/decodeURIComponent.
    ///
    /// NOT URL-ENCODED -- stored as the plain file name (BL-16669):
    ///   - data-sound, data-correct-sound, data-wrong-sound. The editor writes these with a
    ///     bare setAttribute (GameTool.tsx, canvasControlRegistry.ts), and three places in C#
    ///     use the value directly as a file-system path: the set of audio files still in use
    ///     (BookStorage, which DELETES anything not in it), the set packaged for publishing
    ///     (BookFileFilter), and copying a sound in with a template page (Book).
    ///   - The bloomDataDiv entries that name an image, notably coverImage and licenseImage.
    ///     Book.cs writes these decoded and actively normalizes them back to the plain name.
    ///
    /// Why "not encoded" for something as URL-ish as a sound that ends up in a src: because far
    /// more of our code treats these as file names than as URLs. Encoding them instead would mean
    /// the three file-system consumers above no longer find any name containing a space, and the
    /// first of them would delete the file as unused.
    ///
    /// The cost of that choice, which we accept: the consumers that DO build a URL from these
    /// values just concatenate them, so a sound whose name contains a percent escape
    /// ("beep%41.mp3") fails to play. That is true both in BloomPlayer and in our own editor
    /// (GameTool.tsx's playSound does "audio/" + name). Fixing it belongs in those two places, by
    /// encoding when the URL is built rather than by storing the name encoded.
    ///
    /// Note that this is all about URL encoding. XML/HTML encoding is a separate matter and is
    /// handled for us: SetAttribute and InnerText escape on write and unescape on read, so a '&amp;'
    /// or an apostrophe in a file name is safe in either convention. The exception is markup or
    /// XPath built by string concatenation, where nothing escapes anything for you.
    /// </remarks>
    public class UrlPathString
    {
        private readonly string _notEncoded;

        /*	file: red & green, One + One
            URL-in-query-portion encoded? red+%26+green, One+%2B+One
            HTML/XML encoded: red &amp; green
            HttpUtility.UrlPathEncode: red%20&%20green,   One%20+%20One
        */

        /// <summary>
        /// NOTE: Assumes '+' is literal. See BL-3259
        /// </summary>
        public static UrlPathString CreateFromUrlEncodedString(string encoded)
        {
            encoded = encoded.Replace("+", "%2B");
            return new UrlPathString(HttpUtility.UrlDecode(encoded));
        }

        /// <param name="strictlyTreatAsUnencoded">Pass true when you know the string really is
        /// unencoded -- typically because it is a file name you just read from (or wrote to) the
        /// file system. Otherwise the guessing described below applies, and a genuine file name
        /// that happens to contain something shaped like an escape ("photo%41.jpg") is silently
        /// decoded into a different name ("photoA.jpg"). See BL-16669.</param>
        public static UrlPathString CreateFromUnencodedString(
            string unencoded,
            bool strictlyTreatAsUnencoded = false
        )
        {
            unencoded = unencoded.Trim();

            // During the refactoring that lead to this class, one code path
            // essentially didn't trust that the string was already decoded.
            // Assuming that was done for a good reason, that behavior is
            // formalized here. It would seem to be a small risk (makes it
            // impossible to have, say "%20" in your actual file name).
            // However, a '+' in the name is much more likely, and so blindly
            // re-encoding is a problem. So the algorithm is that if the
            // symbol is ambiguous (like '+'), assume it is unencoded (because that's
            // the name of the method) but if it's obviously encoded, then
            // decode it.

            if (!strictlyTreatAsUnencoded && Regex.IsMatch(unencoded, "%[A-Fa-f0-9]{2}"))
                unencoded = HttpUtility.UrlDecode(unencoded.Replace("+", "%2B")); // preserve + as + (as above)
            return new UrlPathString(unencoded);
        }

        /// <summary>
        /// In these strings, "&" would be &amp;  space would just be " "
        /// </summary>
        public static UrlPathString CreateFromHtmlXmlEncodedString(string encoded)
        {
            return new UrlPathString(HttpUtility.HtmlDecode(encoded));
        }

        public string UrlEncoded
        {
            get
            {
                //HttpUtility.UrlEncode gives spaces as "+" which is only good for query strings, not @src attributes
                //HttpUtility.UrlPathEncode, on the other hand, encodes % as %, when it we want %25.
                //Neither seems right.  We have to do a hack either way.
                //Since the docs ask you not to use UrlPathEncode, we'll use the other and hack it
                string standInForSpace = "_SpAcE_";
                //protect spaces from UrlEncode()
                var x = _notEncoded.Replace(" ", standInForSpace);
                x = HttpUtility.UrlEncode(x);
                //now do our own encoding for the protected space
                return x.Replace(standInForSpace, "%20");
            }
        }

        /// <summary>
        /// Get the URL encoding but with / and : decoded so that we get a normal looking URL path.
        /// </summary>
        /// <value>The URL encoded for http path.</value>
        public string UrlEncodedForHttpPath
        {
            get
            {
                var x = UrlEncoded;
                return x.Replace("%2f", "/", System.StringComparison.InvariantCultureIgnoreCase)
                    .Replace("%3a", ":", System.StringComparison.InvariantCultureIgnoreCase);
            }
        }

        public string HtmlXmlEncoded
        {
            get { return HttpUtility.HtmlEncode(_notEncoded); }
        }

        public string NotEncoded
        {
            get { return _notEncoded; }
        }

        /// <summary>
        /// Gives the portion of the path up to and not including the query portion of the url
        /// </summary>
        public UrlPathString PathOnly
        {
            get
            {
                //the 'true' here is to prevent us from getting a string with the instruction
                //to be strict about assuming it is unencoded, and then accidentally re-unencoding
                //it against that previous instruction, when we spil out the paths.
                return CreateFromUnencodedString(_notEncoded.Split('?')[0], true);
            }
        }

        /// <summary>
        /// Returns the query portion of the path, or an empty string if there is no query.
        /// </summary>
        public UrlPathString QueryOnly
        {
            get
            {
                var startQuery = _notEncoded.IndexOf("?");
                if (startQuery < 0)
                    return CreateFromUnencodedString("");
                return CreateFromUnencodedString(_notEncoded.Substring(startQuery));
            }
        }

        private UrlPathString(string notEncodedString)
        {
            _notEncoded = notEncodedString;
        }

        public override bool Equals(object obj)
        {
            var x = obj as UrlPathString;
            if (x == null)
                return false;
            return this.NotEncoded == x.NotEncoded;
        }

        protected bool Equals(UrlPathString other)
        {
            return string.Equals(_notEncoded, other._notEncoded);
        }

        public static bool operator ==(UrlPathString a, UrlPathString b)
        {
            // If both are null, or both are same instance, return true.
            if (System.Object.ReferenceEquals(a, b))
            {
                return true;
            }

            // If one is null, but not both, return false.
            if (((object)a == null) || ((object)b == null))
            {
                return false;
            }

            // Return true if the fields match:
            return a.NotEncoded == b.NotEncoded;
        }

        public static bool operator !=(UrlPathString a, UrlPathString b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return (_notEncoded != null ? _notEncoded.GetHashCode() : 0);
        }

        /// <summary>
        /// Some library books have been uploaded with the cover image filename URL encoded in the file instead of HTML/XML encoded.
        /// So if the file doesn't exist, try decoding to see if that may be the problem, but preserve the original path in case an
        /// error message is still needed.
        /// </summary>
        /// <param name="directory">path of the containing folder</param>
        /// <param name="filename">base filename to be combined with directory.  This may be modified by HttpUtility.UrlDecode().</param>
        /// <remarks>
        /// See https://silbloom.myjetbrains.com/youtrack/issue/BL-3901.
        /// and https://issues.bloomlibrary.org/youtrack/issue/BH-6143.
        /// and https://issues.bloomlibrary.org/youtrack/issue/BL-11145.
        /// </remarks>
        public static string GetFullyDecodedPath(string directory, ref string filename)
        {
            var path = System.IO.Path.Combine(directory, filename);
            if (!SIL.IO.RobustFile.Exists(path))
            {
                const string kUrlEncodedRegex = "%[0-9a-fA-F][0-9a-fA-F]";
                var filename1 = filename;
                while (Regex.IsMatch(filename1, kUrlEncodedRegex))
                {
                    filename1 = HttpUtility.UrlDecode(filename1);
                    var path1 = System.IO.Path.Combine(directory, filename1);
                    if (SIL.IO.RobustFile.Exists(path1))
                    {
                        filename = filename1;
                        return path1;
                    }
                }
            }
            return path;
        }

        // Helpful, for example, when looking at test output.
        public override string ToString()
        {
            return _notEncoded;
        }
    }
}
