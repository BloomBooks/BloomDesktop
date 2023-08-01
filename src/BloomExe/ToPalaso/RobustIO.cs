using System;
using System.IO;
using SIL.Code;

namespace Bloom.ToPalaso
{
	public class RobustIO
	{
		public static void RequireThatDirectoryExists(string path)
		{
			bool exists = false;
			RetryUtility.Retry(() =>
			{
				exists = Directory.Exists(path);
			});
			if (!exists)
			{
				throw new ArgumentException($"The path '{path}' does not exist.");
			}
		}
	}
}
