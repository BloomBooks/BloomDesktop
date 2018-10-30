using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Bloom.Book
{
	public struct VersionRequirement
	{
		public string BloomDesktopMinVersion { get; set; }
		public string BloomReaderMinVersion { get; set; }
		public string FeatureId { get; set; }
		public string FeaturePhrase { get; set; }
	}

	public class VersionComparer<T> : IComparer<T>
	{
		int IComparer<T>.Compare(T version1, T version2)
		{
			string version1Str = version1.ToString();
			string version2Str = version2.ToString();

			return VersionComparer.CompareToVersion(version1Str, version2Str);
		}
	}

	public class VersionComparer
	{
		public static bool IsLessThanVersion(string version1, string version2)
		{
			if (version1 == null || version2 == null)
			{
				return false;
			}

			return CompareToVersion(version1, version2) < 0;
		}

		public static int CompareToVersion(string version1, string version2)
		{
			// I have no idea what CompareTo is supposed to return... it should return "false" for all the boolean comparisons you ask about a null, but that doesn't make sense in CompareTo land.
			Debug.Assert(version1 != null, "version1 must not be null");
			Debug.Assert(version2 != null, "version2 must not be null");

			if (version1 == version2)
			{
				// Equal
				return 0;
			}

			int nextIndexToProcess1 = 0;
			int nextIndexToProcess2 = 0;
			do
			{
				int? nextPart1 = ExtractNextPartOfVersionNumber(version1, ref nextIndexToProcess1);
				int? nextPart2 = ExtractNextPartOfVersionNumber(version2, ref nextIndexToProcess2);

				// If they are null, I basically treat it the same as if they didn't exist.
				if (nextPart1 == null)
				{
					if (nextPart2 == null)
					{
						return 0;
					}
					else
					{
						return -1;
					}
				}
				else if (nextPart2 == null)
				{
					return 1;
				}
				else
				{
					if (nextPart1 < nextPart2)
					{
						return -1;
					}
					else if (nextPart1 > nextPart2)
					{
						return 1;
					}
					else
					{
						// They are equal... instead of returning 0, let's look at the next part
						// (which we do by doing nothing right now, and just contining on to the next iteration.)
					}
				}
			} while (nextIndexToProcess1 < version1.Length && nextIndexToProcess2 < version2.Length);

			if (nextIndexToProcess1 >= version1.Length)
			{
				if (nextIndexToProcess2 == version2.Length)
				{
					// They both ended at the same time without reporting any difference.
					// Therefore they are completley requal.
					return 0;
				}
				else
				{
					// Version 1 ended before Version 2.
					// Consider this x.y vs. x.y.z which we could treat as x.y.0 vs. x.y.z and we should return "less than"

					// Check: if the rest of Version 2 is just "0" or "0.0" or so on... these are basically meaningless which would mean these strings are equal despite not being identical.
					string remainder = version2.Substring(nextIndexToProcess2);
					string significantDigits = remainder.Replace(".", "").Replace("0", "");
					if (significantDigits.Trim().Length == 0)
					{
						return 0;
					}

					return -1;
				}
			}
			else
			{
				// Version 1 has not ended, but Version 2 must've
				// x.y.z vs x.y  (x.y.0)
				Debug.Assert(nextIndexToProcess2 >= version2.Length, "Code expects Version2 string to have ended but it has not.");

				string remainder = version1.Substring(nextIndexToProcess2);
				string significantDigits = remainder.Replace(".", "").Replace("0", "");
				if (significantDigits.Trim().Length == 0)
				{
					return 0;
				}

				return 1;
			}
		}

		// Extracts a version number of form x.y[.z][.w] etc.
		internal static int? ExtractNextPartOfVersionNumber(string version, ref int index)
		{
			int limit1 = version.IndexOf('.', index);
			string nextPart;
			if (limit1 < 0)
			{
				nextPart = version.Substring(index);
				index = version.Length;
			}
			else
			{
				nextPart = version.Substring(index, limit1 - index);
				index = limit1 + 1;
			}

			int nextPartInt;
			if (int.TryParse(nextPart, out nextPartInt))
			{
				return nextPartInt;
			}
			else
			{
				return null;
			}
		}
	}
}
