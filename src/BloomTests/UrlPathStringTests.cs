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
		public void UrlEncodedWithPlus_toUrlEncoded_Retained()
		{
			//sometimes things encode a space a '+' instead of %20
			Assert.AreEqual("test%20me", UrlPathString.CreateFromUrlEncodedString("test+me").UrlEncoded);
		}
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

		[Test]
		public void PathOnly_HasQuery_StripsQuery()
		{
			Assert.AreEqual("test me", UrlPathString.CreateFromUnencodedString("test%20me?12345").PathOnly.NotEncoded);

		}
		[Test]
		public void PathOnly_HasNoQuery_ReturnsAll()
		{
			Assert.AreEqual("test me", UrlPathString.CreateFromUnencodedString("test%20me").PathOnly.NotEncoded);
		}

		[Test]
		public void PathOnly_AmbiguousInput_RoundTrips()
		{
			Assert.AreEqual("test+me", UrlPathString.CreateFromUnencodedString("test+me").PathOnly.NotEncoded);
		}

		//make sure we don't double-encode
		[Test]
		public void CreateFromUnencodedString_ObviousStringWasAlreadyEncoded_Adapts()
		{
			Assert.AreEqual("test me", UrlPathString.CreateFromUnencodedString("test%20me").NotEncoded);
			Assert.AreEqual("test%me", UrlPathString.CreateFromUnencodedString("test%25me").NotEncoded);
			Assert.AreEqual("John&John", UrlPathString.CreateFromUnencodedString("John%26John").NotEncoded);
		}

		//note however that a + sign is really ambiguous, and we've decided that since the method name
		//says that the input is unencoded, we should then assume it is really a plus sign.
		[Test]
		public void UnencodedWithPlus_roundTripable()
		{
			Assert.AreEqual("test+me", UrlPathString.CreateFromUnencodedString("test+me").NotEncoded);
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
