// Copyright (c) 2015 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using Bloom;
using NUnit.Framework;

namespace BloomTests
{
	[TestFixture]
	public class UrlPathStringTests
	{
		[Test]
		public void UrlEncoded_toUrlEncoded_Retained()
		{
			Assert.AreEqual("test%20me", UrlPathString.CreateFromUrlEncodedString("test%20me").UrlEncoded);
		}
		[Test]
		public void UrlEncoded_toUnencoded_Correct()
		{
			Assert.AreEqual("test me", UrlPathString.CreateFromUrlEncodedString("test%20me").NotEncoded);
		}
		[Test]
		public void Unencoded_toUrlEncoded_Correct()
		{
			Assert.AreEqual("test%20me", UrlPathString.CreateFromUrlEncodedString("test me").UrlEncoded);
		}
		[Test]
		public void Unencoded_toUnencoded_Correct()
		{
			Assert.AreEqual("test me", UrlPathString.CreateFromUrlEncodedString("test me").NotEncoded);
		}
	}
}
