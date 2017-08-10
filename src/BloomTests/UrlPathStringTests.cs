// Copyright (c) 2015 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using Bloom;
using NUnit.Framework;

namespace BloomTests
{
	[TestFixture]
	public class UrlPathStringTests
	{
#if NotAnyMore //We changed the behavior for BL-3259
		[Test]
		public void UrlEncodedWithPlusToMeanSpace_toUrlEncoded_Retained()
		{
			//sometimes things encode a space a '+' instead of %20
			Assert.AreEqual("test%20me", UrlPathString.CreateFromUrlEncodedString("test+me").UrlEncoded);
		}
#endif
		[Test]
		public void Unencoded_RoundTripTortureTest()
		{
			var fileName = "bread + cinnamon & sugar = 100% yum.JPG";
			Assert.AreEqual(fileName, UrlPathString.CreateFromUnencodedString(fileName).NotEncoded);
		}
		[Test]
		public void Encoded_RoundTripTortureTest()
		{
			var url = "bread%20%2b%20cinnamon%20%26%20sugar%20%3d%20100%25%20yum.jpg";
			Assert.AreEqual(url, UrlPathString.CreateFromUrlEncodedString(url).UrlEncoded);
		}

		[Test]
		public void UrlEncodedWithPlusToMeanPlus_UnEncoded_PlusRetained()
		{
			//sometimes things encode a space a '+' instead of %20
			Assert.AreEqual("one+one = two", UrlPathString.CreateFromUrlEncodedString("one+one%20=%20two").NotEncoded);
		}
		[Test]
		public void UrlEncoded_withPercent_toUrlEncoded_PercentRetained()
		{
			Assert.AreEqual("OneHundred%25", UrlPathString.CreateFromUrlEncodedString("OneHundred%25").UrlEncoded);
		}
		[Test]
		public void UnEncoded_withPercent_toUrlEncoded_PercentRetained()
		{
			Assert.AreEqual("OneHundred%25", UrlPathString.CreateFromUnencodedString("OneHundred%").UrlEncoded);
		}
		[Test]
		public void UrlEncoded_withPercent_toNotEncoded_PercentRetained()
		{
			Assert.AreEqual("OneHundred%", UrlPathString.CreateFromUrlEncodedString("OneHundred%25").NotEncoded);
		}
		[Test]
		public void UrlEncoded_withSpace_toUrlEncoded_SpaceEntityRetained()
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
		[Test]
		public void PathOnly_LooksEncodedButSetStrictlyTreatAsEncodedTrue_RoundTrips()
		{
			//this checks that PathOnly doesn't do processing in ambiguous mode, undoing the information we gave it to be strict
			Assert.AreEqual("test%20me", UrlPathString.CreateFromUnencodedString("test%20me", true).PathOnly.NotEncoded);
		}

		[Test]
		public void QueryOnly_HasQuery_ReturnsIt()
		{
			Assert.That(UrlPathString.CreateFromUnencodedString("test%20me?12345").QueryOnly.NotEncoded, Is.EqualTo("?12345"));
		}

		[Test]
		public void QueryOnly_NoQuery_ReturnsEmpty()
		{
			Assert.That(UrlPathString.CreateFromUnencodedString("test%20me").QueryOnly.NotEncoded, Is.EqualTo(""));
		}

		[Test]
		public void CreateFromUnencodedString_LooksEncodedButSetStrictlyTreatAsEncodedTrue_RoundTrips()
		{
			Assert.AreEqual("test%20me", UrlPathString.CreateFromUnencodedString("test%20me", true).NotEncoded);
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
		public void UnencodedWithPlus_RoundTripable()
		{
			Assert.AreEqual("test+me", UrlPathString.CreateFromUnencodedString("test+me").NotEncoded);
		}

		[Test]
		public void UnencodedWithPlusAndSpace_RoundTripable()
		{
			Assert.AreEqual("test + me", UrlPathString.CreateFromUnencodedString("test + me").NotEncoded);
		}

		[Test]
		public void UnencodedWithAmpersand_RoundTripable()
		{
			Assert.AreEqual("test&me", UrlPathString.CreateFromUnencodedString("test&me").NotEncoded);
			Assert.AreEqual("test & me", UrlPathString.CreateFromUnencodedString("test & me").NotEncoded);
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

		[Test]
		public void HtmlEncodedWithAmpersand_RoundTripable()
		{
			var s = "one&amp;two";
			Assert.AreEqual(s, UrlPathString.CreateFromHtmlXmlEncodedString(s).HtmlXmlEncoded);
		}

		[Test]
		public void CreateFromHtmlXmlEncodedString_WithAmpersand_UnencodedAsExpected()
		{
			Assert.AreEqual("one&two", UrlPathString.CreateFromHtmlXmlEncodedString("one&amp;two").NotEncoded);
		}
	}
}
