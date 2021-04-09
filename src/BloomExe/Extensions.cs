using System;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Utils;

namespace Bloom
{
	public static class Extensions
	{
		/// <summary>
		/// Returns a localhost URL to a file
		/// </summary>
		/// <param name="fileName">The raw filename used by the operating system</param>
		/// <returns>A well-formed, singly-encoded URL (special characters in the filename will be duly escaped, except for directory separators, which will be converted to '/' (if necessary))</returns>

		public static string ToLocalhost(this string fileName)
		{
			// don't do this if it is done already
			if (fileName.StartsWith(BloomServer.ServerUrlWithBloomPrefixEndingInSlash)) return fileName;

			return BloomServer.ServerUrlWithBloomPrefixEndingInSlash + fileName.EscapeFileNameForHttp();
		}

		public static string FromLocalhost(this string uri)
		{
			if (uri.StartsWith(BloomServer.ServerUrlWithBloomPrefixEndingInSlash))
				uri = uri.Substring(BloomServer.ServerUrlWithBloomPrefixEndingInSlash.Length).UnescapeFileNameForHttp();
			return uri;
		}

		private static readonly char[] kDirectorySeparators = new char[] { '\\', '/' };

		/// <summary>
		/// Escapes a number of characters that need it for our url/http processing.
		/// </summary>
		/// <remarks>
		/// Note that calls to EscapeFileNameForHttp() must be matched by an equal number of
		/// subsequent calls to UnescapeFileNameForHttp().  (Normally each is called once.)
		/// </remarks>
		public static string EscapeFileNameForHttp(this string fileName)
		{
			// Our original implementation manually escaped the following characters: '%', ':', '#', '?', and '(', ')' (In BL-117, parenthesis in the URL was not working correctly)
			// Now we rely on a System.Uri function, which is more thorough about escaping characters.
			var escapedPathComponents = fileName.Split(kDirectorySeparators).Select(Uri.EscapeDataString);
			string escapedFileName = String.Join("/", escapedPathComponents);
			return escapedFileName;
		}

		/// <summary>
		/// Remove the escaping of characters that need it for our url/http processing to restore
		/// a valid file pathname.
		/// </summary>
		/// <remarks>
		/// Note that calls to UnescapeFileNameForHttp() must be matched by an equal number of
		/// previous calls to EscapeFileNameForHttp().  (Normally each is called once.)
		/// </remarks>
		public static string UnescapeFileNameForHttp(this string uri)
		{
			// Our original implementation manually unescaped the escape sequences for the following characters: ' ', ':', '#', '?', '(', ')', and '%'
			// Now we just wrap a System.Uri function (which is more thorough about unescaping escape sequences)
			// and convert it into an extension method
			return Uri.UnescapeDataString(uri);
		}

		public static int ToInt(this bool value)
		{
			if (value) return 1;
			return 0;
		}

		public static void AppendLineFormat(this StringBuilder sb, string format, params object[] args)
		{
			sb.AppendLine(string.Format(format, args));
		}

		public static void SizeTextRectangleToText(this ToolStripItemTextRenderEventArgs args)
		{
			var textSize = TextRenderer.MeasureText(args.Graphics, args.Text, args.TextFont);
			const int padding = 2;

			var rc = args.TextRectangle;
			var changed = false;

			// adjust the rectangle to fit the calculated text size
			if (rc.Width < textSize.Width + padding)
			{
				var diffX = textSize.Width + 2 - rc.Width;
				rc.X -= diffX / 2;
				rc.Width += diffX;
				changed = true;
			}

			if (rc.Height < textSize.Height + padding)
			{
				var diffY = textSize.Height + 2 - rc.Height;
				rc.Y -= diffY / 2;
				rc.Height += diffY;
				changed = true;
			}

			// if nothing changed, return now
			if (!changed) return;

			args.TextRectangle = rc;
		}

		/// <summary>
		/// Gets the Text property, un-escaping it if necessary
		/// </summary>
		public static string GetTextSafely(this Label label)
		{
			return TextUtils.UnescapeWinForms(label.Text, label.UseMnemonic);
		}

		/// <summary>
		/// Sets the Text property to {text}, escaping it if necessary.
		/// Precondition: Only valid as long as {label.UseMnemonic} does not change.
		/// </summary>
		/// <param name="text">The un-escaped value of text (that which should be read by user)</param>
		public static void SetTextSafely(this Label label, string text)
		{
			label.Text = TextUtils.EscapeForWinForms(text, label.UseMnemonic);
		}

		/// <summary>
		/// Gets the Text property, un-escaping it if necessary
		/// </summary>
		public static string GetTextSafely(this Button button)
		{
			return TextUtils.UnescapeWinForms(button.Text, button.UseMnemonic);
		}

		/// <summary>
		/// Sets the Text property to {text}, escaping it if necessary.
		/// Precondition: Only valid as long as {label.UseMnemonic} does not change.
		/// </summary>
		/// <param name="text">The un-escaped value of text (that which should be read by user)</param>
		public static void SetTextSafely(this Button button, string text)
		{
			button.Text = TextUtils.EscapeForWinForms(text, button.UseMnemonic);
		}
	}
}
