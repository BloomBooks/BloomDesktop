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
	}
}
