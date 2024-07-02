using System.Collections.Generic;
using System.Xml;
using Bloom.Book;
using Bloom.Publish.BloomPub;
using Bloom.SafeXml;
using NUnit.Framework;

namespace BloomTests.Publish
{
    [TestFixture]
    public class BloomReaderFileMakerTests
    {
        // Got this pasting from notepad and word with the extra line in between the Qs already, into Bloom 4.2.
        [TestCase("<p>first<br>one<br>*two<br><br>second<br>*aa<br>bb<br></p>")]
        // Got this by typing instead of pasting in Bloom
        [TestCase(
            @"<p>first<br></p><p>one</p><p>*two</p><p><br></p><p>second</p><p>*aa</p><p>bb</p>"
        )]
        // got this from pasting from notepad into Bloom 4.1
        [TestCase(("<p>first<br>one<br>*two<br></p><p><br></p><p>second<br>*aa<br>bb</p>"))]
        // got this from pasting from notepad into Firefox 59
        [TestCase("<p>first<br>one<br>*two<br></p><p><br></p><p>second<br>*aa<br>bb<br></p>")]
        // This approximates what we got in BL-5920; don't know how the zero-width-non-joiner got in there, but most lines had them.
        // Features: no paragraphs, \200c (zero width-non-joiner) which was causing the parser to miss the break between questions
        [TestCase("first<br>one<br>*two<br>\u200C<br>second<br>*aa<br>bb")]
        //Got this by pasting into the quiz page when the Talking Book was open
        // This fails. Enable when fixing BL-5910
        /*[TestCase(@"<p><span id='b352ae2e-8394-4d6b-82a3-367521cbafb5' class='audio-sentence'>first <br></span>
                    <span id='b6376333-986a-4b23-91ea-31028e19da07' class='audio-sentence'>one <br></span>
                    <span id='i3636d82b-4d87-4b21-9a36-3b020a957abd' class='audio-sentence'>*two</span></p>
                    <p><span id='i7e03445c-1936-428d-8911-e829f55ae863' class='audio-sentence'>second <br></span>
                    <span id='i2b681f7e-a315-4584-8524-0f4809c3bf18' class='audio-sentence'>*aa <br></span>
                    <span id='i34524d36-0a96-4788-8131-991bd855bb77' class='audio-sentence'>bb <br> <br></span></p>")]
                    */
        public void ExtractQuestionGroups_ParsesCorrectly(string contents)
        {
            contents = contents.Replace("<br>", "<br/>"); // convert from html to xml
            var page = SafeXmlDocument.Create();
            page.LoadXml(
                @"<div><div class='bloom-editable' lang='abc'>" + contents + "</div></div>"
            );
            var questionGroups = new List<QuestionGroup>();
            BloomPubMaker.ExtractQuestionGroups(page.DocumentElement, questionGroups);
            Assert.AreEqual(1, questionGroups.Count);
            Assert.AreEqual(2, questionGroups[0].questions.Length);
            Assert.AreEqual("first", questionGroups[0].questions[0].question);
            Assert.AreEqual(2, questionGroups[0].questions[0].answers.Length);
            Assert.AreEqual("one", questionGroups[0].questions[0].answers[0].text);
            Assert.AreEqual("two", questionGroups[0].questions[0].answers[1].text);
            Assert.IsFalse(questionGroups[0].questions[0].answers[0].correct);
            Assert.IsTrue(questionGroups[0].questions[0].answers[1].correct);

            Assert.AreEqual("second", questionGroups[0].questions[1].question);
            Assert.AreEqual(2, questionGroups[0].questions[1].answers.Length);
            Assert.AreEqual("aa", questionGroups[0].questions[1].answers[0].text);
            Assert.AreEqual("bb", questionGroups[0].questions[1].answers[1].text);
            Assert.IsTrue(questionGroups[0].questions[1].answers[0].correct);
            Assert.IsFalse(questionGroups[0].questions[1].answers[1].correct);
        }

        // note: as far as I can tell, this test case uses <br> in a way that will not actually be found in the editor.
        // It is copied from BloomReaderPublishTests where it was in a kind of way-above-unit test.
        [TestCase(
            @" <p>Where is the USA?<br></br>
                                South America<br></br>
                                *North America<br></br>
                                Europe<br></br>
                                Asia</p>

                                <p></p>

                                <p>Where does the platypus come from?<br></br>
                                *Australia<br></br>
                                Papua New Guinea<br></br>
                                Africa<br></br>
                                Peru</p>

                                <p></p>

                                <p>What is an Emu?<br></br>
                                A fish<br></br>
                                An insect<br></br>
                                A spider<br></br>
                                * A bird</p>

                                <p></p>

                                <p>Where do emus live?<br></br>
                                New Zealand<br></br>
                                * Farms in the USA<br></br>
                                England<br></br>
                                Wherever</p>

                                <p></p>"
        )]
        public void ExtractQuestionGroups_Long_ParsesCorrectly(string contents)
        {
            var page = SafeXmlDocument.Create();
            page.LoadXml(
                @"<div><div class='bloom-editable' lang='abc'>" + contents + "</div></div>"
            );
            var questionGroups = new List<QuestionGroup>();
            BloomPubMaker.ExtractQuestionGroups(page.DocumentElement, questionGroups);
            Assert.AreEqual(1, questionGroups.Count);
            Assert.AreEqual(4, questionGroups[0].questions.Length);
        }
    }
}
