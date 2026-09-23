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
            Assert.AreEqual(
                "test%20me",
                UrlPathString.CreateFromUrlEncodedString("test+me").UrlEncoded
            );
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
            Assert.AreEqual(
                "one+one = two",
                UrlPathString.CreateFromUrlEncodedString("one+one%20=%20two").NotEncoded
            );
        }

        [Test]
        public void UrlEncoded_withPercent_toUrlEncoded_PercentRetained()
        {
            Assert.AreEqual(
                "OneHundred%25",
                UrlPathString.CreateFromUrlEncodedString("OneHundred%25").UrlEncoded
            );
        }

        [Test]
        public void UnEncoded_withPercent_toUrlEncoded_PercentRetained()
        {
            Assert.AreEqual(
                "OneHundred%25",
                UrlPathString.CreateFromUnencodedString("OneHundred%").UrlEncoded
            );
        }

        [Test]
        public void UrlEncoded_withPercent_toNotEncoded_PercentRetained()
        {
            Assert.AreEqual(
                "OneHundred%",
                UrlPathString.CreateFromUrlEncodedString("OneHundred%25").NotEncoded
            );
        }

        [Test]
        public void UrlEncoded_withSpace_toUrlEncoded_SpaceEntityRetained()
        {
            Assert.AreEqual(
                "test%20me",
                UrlPathString.CreateFromUrlEncodedString("test%20me").UrlEncoded
            );
        }

        [Test]
        public void UrlEncoded_toUnencoded_Correct()
        {
            Assert.AreEqual(
                "test me",
                UrlPathString.CreateFromUrlEncodedString("test%20me").NotEncoded
            );
        }

        [Test]
        public void Unencoded_toUrlEncoded_Correct()
        {
            Assert.AreEqual(
                "test%20me",
                UrlPathString.CreateFromUrlEncodedString("test me").UrlEncoded
            );
        }

        [Test]
        public void Unencoded_toUnencoded_Correct()
        {
            Assert.AreEqual(
                "test me",
                UrlPathString.CreateFromUnencodedString("test me").NotEncoded
            );
        }

        [Test]
        public void PathOnly_HasQuery_StripsQuery()
        {
            Assert.AreEqual(
                "test me",
                UrlPathString.CreateFromUnencodedString("test me?12345").PathOnly.NotEncoded
            );
        }

        [Test]
        public void PathOnly_HasNoQuery_ReturnsAll()
        {
            Assert.AreEqual(
                "test me",
                UrlPathString.CreateFromUnencodedString("test me").PathOnly.NotEncoded
            );
        }

        [Test]
        public void PathOnly_AmbiguousInput_RoundTrips()
        {
            Assert.AreEqual(
                "test+me",
                UrlPathString.CreateFromUnencodedString("test+me").PathOnly.NotEncoded
            );
        }

        [Test]
        public void PathOnly_LooksEncoded_IsNotDecodedAgain()
        {
            // PathOnly is a slice of an already-decoded string, so it must not decode.
            Assert.AreEqual(
                "test%20me",
                UrlPathString.CreateFromUnencodedString("test%20me").PathOnly.NotEncoded
            );
        }

        [Test]
        public void QueryOnly_HasQuery_ReturnsIt()
        {
            Assert.That(
                UrlPathString.CreateFromUnencodedString("test me?12345").QueryOnly.NotEncoded,
                Is.EqualTo("?12345")
            );
        }

        [Test]
        public void QueryOnly_NoQuery_ReturnsEmpty()
        {
            Assert.That(
                UrlPathString.CreateFromUnencodedString("test me").QueryOnly.NotEncoded,
                Is.EqualTo("")
            );
        }

        /// <summary>
        /// The counterpart of PathOnly_LooksEncoded_IsNotDecodedAgain. Before BL-16669 QueryOnly
        /// went through the guessing path while PathOnly did not, so a query containing something
        /// shaped like an escape was decoded a second time. Both are slices of the same
        /// already-decoded string, so neither should decode.
        /// </summary>
        [Test]
        public void QueryOnly_LooksEncoded_IsNotDecodedAgain()
        {
            Assert.That(
                UrlPathString.CreateFromUnencodedString("cat.png?name=a%20b").QueryOnly.NotEncoded,
                Is.EqualTo("?name=a%20b")
            );
        }

        [Test]
        public void CreateFromUnencodedString_LooksEncoded_IsStillTakenLiterally()
        {
            Assert.AreEqual(
                "test%20me",
                UrlPathString.CreateFromUnencodedString("test%20me").NotEncoded
            );
        }

        /// <summary>
        /// The bug behind BL-16669: a real file name can contain a '%' followed by two hex digits
        /// ("photo%41.jpg"). Say it is unencoded -- which is what nearly every caller can say --
        /// and the name survives a trip out to the browser as a src and back again.
        /// </summary>
        [Test]
        public void FileNameContainsPercentThenHexDigits_SurvivesRoundTrip()
        {
            // Sanity check: this is exactly what the guessing overload would do to it, which is
            // why callers holding a file name must not use that one.
            Assert.That(
                UrlPathString.CreateFromPossiblyEncodedString("photo%41.jpg").NotEncoded,
                Is.EqualTo("photoA.jpg"),
                "the guessing overload is expected to mangle this name"
            );

            var encoded = UrlPathString.CreateFromUnencodedString("photo%41.jpg").UrlEncoded;
            Assert.That(encoded, Is.EqualTo("photo%2541.jpg"));
            // ...and the browser's request for that src decodes back to the real file name.
            Assert.That(
                UrlPathString.CreateFromUrlEncodedString(encoded).NotEncoded,
                Is.EqualTo("photo%41.jpg")
            );
        }

        //make sure we don't double-encode
        [Test]
        public void CreateFromPossiblyEncodedString_ObviousStringWasAlreadyEncoded_Adapts()
        {
            Assert.AreEqual(
                "test me",
                UrlPathString.CreateFromPossiblyEncodedString("test%20me").NotEncoded
            );
            Assert.AreEqual(
                "test%me",
                UrlPathString.CreateFromPossiblyEncodedString("test%25me").NotEncoded
            );
            Assert.AreEqual(
                "John&John",
                UrlPathString.CreateFromPossiblyEncodedString("John%26John").NotEncoded
            );
        }

        /// <summary>
        /// Even when it decides the string is encoded, a '+' stays a '+': it is far likelier to be
        /// a real character in a file name than an encoded space (BL-3259).
        /// </summary>
        [Test]
        public void CreateFromPossiblyEncodedString_DecodesButLeavesPlusAlone()
        {
            Assert.AreEqual(
                "one + one = two",
                UrlPathString.CreateFromPossiblyEncodedString("one + one%20=%20two").NotEncoded
            );
        }

        /// <summary>
        /// With nothing that looks like an escape, the guessing overload leaves the string alone,
        /// which is what makes it safe for the handful of callers that cannot know.
        /// </summary>
        [Test]
        public void CreateFromPossiblyEncodedString_NothingLooksEncoded_TakenLiterally()
        {
            Assert.AreEqual(
                "100% of the time",
                UrlPathString.CreateFromPossiblyEncodedString("100% of the time").NotEncoded
            );
        }

        //note however that a + sign is really ambiguous, and we've decided that since the method name
        //says that the input is unencoded, we should then assume it is really a plus sign.
        [Test]
        public void UnencodedWithPlus_RoundTripable()
        {
            Assert.AreEqual(
                "test+me",
                UrlPathString.CreateFromUnencodedString("test+me").NotEncoded
            );
        }

        [Test]
        public void UnencodedWithPlusAndSpace_RoundTripable()
        {
            Assert.AreEqual(
                "test + me",
                UrlPathString.CreateFromUnencodedString("test + me").NotEncoded
            );
        }

        [Test]
        public void UnencodedWithAmpersand_RoundTripable()
        {
            Assert.AreEqual(
                "test&me",
                UrlPathString.CreateFromUnencodedString("test&me").NotEncoded
            );
            Assert.AreEqual(
                "test & me",
                UrlPathString.CreateFromUnencodedString("test & me").NotEncoded
            );
        }

        [Test]
        public void Equals_AreEqual_True()
        {
            Assert.IsTrue(
                UrlPathString
                    .CreateFromUrlEncodedString("test me")
                    .Equals(UrlPathString.CreateFromUrlEncodedString("test " + "me"))
            );
        }

        [Test]
        public void Equals_AreNotEqual_False()
        {
            Assert.IsFalse(
                UrlPathString
                    .CreateFromUrlEncodedString("test me")
                    .Equals(UrlPathString.CreateFromUrlEncodedString("test him"))
            );
        }

        [Test]
        public void EqualityOperator_AreEqual_True()
        {
            Assert.IsTrue(
                UrlPathString.CreateFromUrlEncodedString("test me")
                    == UrlPathString.CreateFromUrlEncodedString("test " + "me")
            );
        }

        [Test]
        public void EqualityOperator_AreNotEqual_False()
        {
            Assert.IsFalse(
                UrlPathString.CreateFromUrlEncodedString("test me")
                    == UrlPathString.CreateFromUrlEncodedString("different")
            );
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
            Assert.AreEqual(
                "one&two",
                UrlPathString.CreateFromHtmlXmlEncodedString("one&amp;two").NotEncoded
            );
        }
    }
}
