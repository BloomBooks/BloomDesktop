using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using L10NSharp;

namespace Bloom
{
	/// <summary>
	/// A place to put messages that may be reusable
	/// </summary>
	public class CommonMessages
	{
		public static string GetPleaseClickHereForHelpMessage(string pathtoProblemFile)
		{
			var template2 = LocalizationManager.GetString("Common.ClickHereForHelp",
				"Please click [here] to get help from the Bloom support team.",
				"[here] will become a link. Keep the brackets to mark the translated text that should form the link.");

			var pattern = new Regex(@"\[(.*)\]");
			if (!pattern.IsMatch(template2))
			{
				// If the translator messed up and didn't mark the bit that should be the hot link, we'll make the whole sentence hot.
				// So it will be something like "Please click here to get help from the Bloom support team", and you can click anywhere
				// on the sentence.
				template2 = "[" + template2 + "]";
			}

			var part2 = pattern.Replace(template2,
				$"<a href='/bloom/api/teamCollection/reportBadZip?file={UrlPathString.CreateFromUnencodedString(pathtoProblemFile).UrlEncoded}'>$1</a>");
			return part2;
		}

		public static string GetProblemWithBookMessage(string bookName)
		{
			// Enhance (YAGNI): if we use this for book problems NOT in the TC system, this will need a param
			// to allow leaving out that part of the message, probably an alternative L10N item.
			var template = LocalizationManager.GetString("TeamCollection.ProblemWithBook",
				"There is a problem with the book \"{0}\" in the Team Collection system.");
			return String.Format(template, bookName);
		}
	}
}
