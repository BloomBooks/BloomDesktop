using System.Collections.Generic;
using System.Linq;
using Bloom.Utils;
using NUnit.Framework;

namespace BloomTests.Utils
{
	[TestFixture]
	class MiscUtilsTests
	{
		[Test]
		public void EscapeForCmd_DoubleQuotedString_WrappedInDoubleQuotes()
		{
			string inputCommand = "\"C:\\src\\Bloom Desktop 2\\output\\Debug\\Bloom.exe\" upload \"C:\\Bloom Collections\\Collection Name\" -u username@domain.com -d dev";
			var result = MiscUtils.EscapeForCmd(inputCommand);

			Assert.That(result, Is.EqualTo("\"\"C:\\src\\Bloom Desktop 2\\output\\Debug\\Bloom.exe\" upload \"C:\\Bloom Collections\\Collection Name\" -u username@domain.com -d dev\""));
		}
	}
}
