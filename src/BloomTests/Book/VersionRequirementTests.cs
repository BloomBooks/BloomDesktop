using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Book;
using NUnit.Framework;


namespace BloomTests.Book
{
	class VersionRequirementTests
	{
		[TestCase("1.3", "2.0", -1)]
		[TestCase("4.3", "4.4", -1)]
		[TestCase("1.9", "1.10", -1)]   // Exercise a case that will cause a naive lexicographical test to fail
		[TestCase("4.4.0.0", "4.4", 0)] // Note: The actual assembly version in the assembly is x.y.z.w, so it's important to get this right
		[TestCase("1.0", "1.0.1", -1)]
		[TestCase("1.2.3", "1.2.2.9", 1)]
		public void CompareToVersion(string version1, string version2, int expectedResult)
		{
			int result = VersionComparer.CompareToVersion(version1, version2);
			Assert.AreEqual(expectedResult, result);

			int commutativeResult = VersionComparer.CompareToVersion(version2, version1);
			Assert.AreEqual(-expectedResult, commutativeResult, "Commutative test");
		}
	}
}
