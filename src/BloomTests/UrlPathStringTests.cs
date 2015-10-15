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
			Assert.AreEqual("test me", UrlPathString.CreateFromUnencodedString("test me").NotEncoded);
		}

		//make sure we don't double-encode
		[Test]
		public void CreateFromUnencodedString_StringWasAlreadyEncode_Adapts()
		{
			Assert.AreEqual("test me", UrlPathString.CreateFromUnencodedString("test%20me").NotEncoded);
		}
		[Test]
		public void Equals_AreEqual_True()
		{
			Assert.IsTrue(UrlPathString.CreateFromUrlEncodedString("test me").Equals(UrlPathString.CreateFromUrlEncodedString("test " + "me")));
		}
		[Test]
		public void Equals_AreNotEqual_False()
		{
			Assert.IsFalse(UrlPathString.CreateFromUrlEncodedString("test me").Equals(UrlPathString.CreateFromUrlEncodedString("test him")));
		}
		[Test]
		public void EqualityOperator_AreEqual_True()
		{
			Assert.IsTrue(UrlPathString.CreateFromUrlEncodedString("test me") == UrlPathString.CreateFromUrlEncodedString("test "+"me"));
		}
		[Test]
		public void EqualityOperator_AreNotEqual_False()
		{
			Assert.IsFalse(UrlPathString.CreateFromUrlEncodedString("test me") == UrlPathString.CreateFromUrlEncodedString("different"));
		}
		[Test]
		public void EqualityOperator_OneIsNull_False()
		{
			Assert.IsFalse(UrlPathString.CreateFromUrlEncodedString("test me") == null);
		}
	}
}
