using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Bloom.web.controllers
{
	public class OrthographyConverter
	{
		private Dictionary<string, string> mappings;
		private IOrderedEnumerable<int> keyLengthsThatHaveMappings;

		#region Construction and/or Initialization
		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="filename">The filename containing the conversion settings. It should be tab-delimited with 2 columns. The 1st column is a sequence of 1 or more characters in the source language. The 2nd column contains a sequence of characteres to map to in the target language.</param>
		public OrthographyConverter(string filename)
		{
			InitializeMappings(filename);
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="mappings">Construct the mapping from an existing dictionary (done by reference)</param>
		public OrthographyConverter(Dictionary<string, string> mappings)
		{
			InitializeMappings(mappings);
		}

		/// <summary>
		/// Initialize the mappings from a file
		/// </summary>
		/// <param name="filename">The filename containing the conversion settings. It should be tab-delimited with 2 columns. The 1st column is a sequence of 1 or more characters in the source language. The 2nd column contains a sequence of characteres to map to in the target language.</param>
		protected void InitializeMappings(string filename)
		{
			this.mappings = new Dictionary<string, string>();

			string[] lines = File.ReadAllLines(filename);
			foreach (var line in lines)
			{
				// Allow empty lines and # comments.  (See BL-7023)
				if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
					continue;
				// Allow any number of spaces or tabs to separate fields on a line.  (See BL-7023)
				string[] fields = line.Split(new [] {'\t', ' '}, StringSplitOptions.RemoveEmptyEntries);
				if (fields.Length >= 2)
				{
					Debug.Assert(!mappings.ContainsKey(fields[0]), $"Invalid Orthography Conversion settings ({filename}). Duplicate key: \"{fields[0]}\".");
					this.mappings[fields[0]] = fields[1];	// Nothing particularly good to do if a duplicate exists, so might as well overwrite it.
				}
				else
				{
					Debug.Assert(false, $"OrthographyConverter: Parsed line with invalid format (2+ fields expected): \"{line}\"");
				}
			}

			FinalizeInitialization();
		}

		/// <summary>
		/// Initialize the mappings directly from a dictionary (by reference)
		/// </summary>
		protected void InitializeMappings(Dictionary<string, string> mappings)
		{
			this.mappings = mappings;
			FinalizeInitialization();
		}

		/// <summary>
		/// Must be called at the end of any initialization of mappings 
		/// </summary>
		protected void FinalizeInitialization()
		{
			// When we apply the mapping, it is really helpful for us to know
			// 1) What is the longest prefix?
			// 2) All the valid lengths of the prefixes
			// So we create this list to help us look up that info easily later
			this.keyLengthsThatHaveMappings =
				this.mappings.Keys.Select(x => x.Length)	// the number of characters in the key (unmapped) side of the mapping
				.Where(x => x > 0)							// Ensure no empty strings
				.Distinct()									// Make distinct count
				.OrderByDescending(x => x);					// Sorted it from biggest to smallest			
		}
		#endregion

		/// <summary>
		/// Takes an unmapped string and applies this orthography conversion to it.
		/// </summary>
		public string ApplyMappings(string unmapped)
		{
			StringBuilder mapped = new StringBuilder();

			if (String.IsNullOrEmpty(unmapped))
			{
				return unmapped;
			}

			// Determine whether some prefix of this string matches any of the orthography conversion rules
			// We can just calculate all the prefixes that could possibly match a rule based on prefix length.
			// We want the longest matched rules to take precedence, so we start with the longest possible prefix and work our way down.
			// Once we have the prefix, we can just check if it exists in the mapping dictionary.
			// There's no need to enumerate all the other rules of the same length in the dictionary because for a single given length, only one rule could be able to match.
			int index = 0;
			while (index < unmapped.Length)
			{
				bool wasMatched = false;

				foreach (var possibleKeyLength in this.keyLengthsThatHaveMappings)
				{
					if (index + possibleKeyLength > unmapped.Length)
					{
						// Not enough chars left to construct this prefix. Continue on to the next prefix to check.
						continue;
					}
					string prefix = unmapped.Substring(index, possibleKeyLength);
					string replacement;
					if (this.mappings.TryGetValue(prefix, out replacement))
					{
						mapped.Append(replacement);
						index += prefix.Length;
						wasMatched = true;
						break;
					}
				}

				if (!wasMatched)
				{
					// If nothing was matched, we can only safely advance 1. (Not by this.keyLengthsWithMappings.Max())
					// For example, even if this index didn't have a trigram match, there could be a trigram match on the next index.
					// So can only advance by 1, not by 3.
					mapped.Append(unmapped[index]);
					++index;
				}
			}

			return mapped.ToString();
		}

		#region Filename Parsing
		// Given a filename, determines if it matches the expected format. If so, returns a tuple containing the source and target language codes.
		// If not, returns null instead.
		// Expected format: convert_[sourceLang](-[sourceScript])_to_[targetLang](-[targetScript]).txt
		public static Tuple<string, string> ParseSourceAndTargetFromFilename(string path)
		{
			string basename = Path.GetFileName(path);
			string[] fields = basename.Split('_');

			if (fields.Length != 4)
			{
				return null;
			}
			else if (fields[0] != "convert" || fields[2] != "to")
			{
				return null;
			}
			else if (!basename.EndsWith(".txt"))
			{
				return null;
			}

			string source = fields[1];
			string target = fields[3];
			var extensionStartIndex = target.LastIndexOf('.');
			target = target.Substring(0, extensionStartIndex);

			return Tuple.Create(source, target);
		}
		#endregion


	}
}
