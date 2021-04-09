using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bloom.Utils
{
	public static class TextUtils
	{
		// Removes trailing newline characters of both varieties, both \r and \n
		public static string TrimEndNewlines(string text)
		{
			int index = text.Length - 1;
			while (text[index] == '\r' || text[index] == '\n')
			{
				--index;
			}

			string cleaned = text.Substring(0, index + 1);
			return cleaned;
		}

		/// <summary>
		/// Escapes text, if needed, so that it can be given to elements where useMnemonic is true (which is the default value)
		/// This is mostly relevant for Label.Text
		/// Precondition: The result is only valid as long as useMnemonic does not chagne.
		/// </summary>
		/// <param name="text">The un-escaped text (the value you want the user to see)</param>
		/// <param name="useMnemonic">The value of useMnemonic on the WinForms control the text will go in. If true, then the text will be escaped. If false, the text will be returned as is.
		/// This parameter is here so that you don't accidentally escape it when no escaping is required.
		/// </param>
		/// <returns>The escaped version of the text</returns>
		public static string EscapeForWinForms(string text, bool useMnemonic) => useMnemonic ? text?.Replace("&", "&&") : text;
		public static string UnescapeWinForms(string text, bool useMnemonic) => useMnemonic ? text?.Replace("&&", "&") : text;
	}
}
