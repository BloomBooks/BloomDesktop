/**
 * split_sentences.test.js
 *
 * Trying out the unit tests for javascript
 *
 * Created Apr 22, 2014 by Phil Hopper
 *
 */

//dump it in (how else to activate the jquery extensions it adds?)
import './synphony_lib';
import {_} from "underscore";
import {theOneLibSynphony, LanguageData} from './synphony_lib';

describe("Splitting text into sentences", function() {

        beforeEach(function() {
                //
        });

        afterEach(function() {
                //
        });

        it("Split into sentences, get word count", function() {

                var inputText = "This is not sentence 2. \"This is 'sentence 2.'\"";
                var fragments = theOneLibSynphony.stringToSentences(inputText);
                var sentences = _.filter(fragments, function(frag) {
                        return frag.isSentence;
                });
                expect(sentences[0].wordCount()).toBe(5);
                expect(sentences[1].wordCount()).toBe(4);
        });

    it("Split into sentences, get word count (space is sentence-separating)", function() {

        var extraPunctuationToTest = ['\\u0020', '\\U0020'];
        var inputText = "One Two Three";

        for (var i = 0; i < extraPunctuationToTest.length; i++) {
            theOneLibSynphony.setExtraSentencePunctuation(extraPunctuationToTest[i]);
            var fragments = theOneLibSynphony.stringToSentences(inputText);
            var sentences = _.filter(fragments, function(frag) {
                return frag.isSentence;
            });
            expect(sentences.length).toBe(3);
            expect(sentences[0].wordCount()).toBe(1);
            expect(sentences[1].wordCount()).toBe(1);
            expect(sentences[2].wordCount()).toBe(1);
        }

        // Reset it for the next test
        theOneLibSynphony.setExtraSentencePunctuation('');
    });

    it("Split into sentences, get word count (space is sentence-separating) - Thai", function() {

        var extraPunctuationToTest = ['\\u0020', '\\U0020'];
        var inputText = "ฉัน​มี​ยุง​ใน​บ้าน ฉัน​มี​ยุง​ใน​บ้าน";

        for (var i = 0; i < extraPunctuationToTest.length; i++) {
            theOneLibSynphony.setExtraSentencePunctuation(extraPunctuationToTest[i]);
            var fragments = theOneLibSynphony.stringToSentences(inputText);
            var sentences = _.filter(fragments, function(frag) {
                return frag.isSentence;
            });
            expect(sentences.length).toBe(2);
            expect(sentences[0].wordCount()).toBe(5);
            expect(sentences[1].wordCount()).toBe(5);
        }

        // Reset it for the next test
        theOneLibSynphony.setExtraSentencePunctuation('');
    });

        it("Get total word count", function() {

                var inputText = "This is sentence 1. \"This is 'sentence 2.'\" this is sentence.3. This is the 4th sentence! Is this the 5th sentence? Is \"this\" \"sentence 6?\"";
                var words = theOneLibSynphony.getWordsFromHtmlString(inputText);

                expect(words.length).toBe(25);
        });

        it("Get unique word count", function() {

                var inputText = "This is sentence 1. \"This is 'sentence 2.'\" this is sentence.3. This is the 4th sentence! Is this the 5th sentence? Is \"this\" \"sentence 6?\"";
                var words = theOneLibSynphony.getUniqueWordsFromHtmlString(inputText);

                expect(words.length).toBe(10);
        });

        it("Tag around sentence", function() {

                var inputText = "This is sentence 1. <span class=\"test\">This is sentence 2.</span> This is sentence 3.";
                var fragments = theOneLibSynphony.stringToSentences(inputText);

                expect(fragments.length).toBe(5);
                expect(fragments[0].text).toBe('This is sentence 1.');
                expect(fragments[1].text).toBe(' ');
                expect(fragments[2].text).toBe('<span class=\"test\">This is sentence 2.</span>');
                expect(fragments[3].text).toBe(' ');
                expect(fragments[4].text).toBe('This is sentence 3.');
        });

        it("Tag between sentences", function() {
                var inputText = "This is sentence 1.<span class=\"test\"> </span>This is sentence 2. This is sentence 3.";
                var fragments = theOneLibSynphony.stringToSentences(inputText);

                expect(fragments.length).toBe(5);
                expect(fragments[0].text).toBe('This is sentence 1.');
                expect(fragments[1].text).toBe('<span class=\"test\"> </span>');
                expect(fragments[2].text).toBe('This is sentence 2.');
                expect(fragments[3].text).toBe(' ');
                expect(fragments[4].text).toBe('This is sentence 3.');
        });

        it("Tag between sentences extra space", function() {
                var inputText = "This is sentence 1. <span class=\"test\"> </span> This is sentence 2. This is sentence 3.";
                var fragments = theOneLibSynphony.stringToSentences(inputText);

                expect(fragments.length).toBe(5);
                expect(fragments[0].text).toBe('This is sentence 1.');
                expect(fragments[1].text).toBe(' ');
                expect(fragments[2].text).toBe('<span class=\"test\"> </span> This is sentence 2.');
                expect(fragments[3].text).toBe(' ');
                expect(fragments[4].text).toBe('This is sentence 3.');
        });

        it("Nbsp between sentences extra space", function () {
                var inputText = "This is sentence 1.&nbsp; This is sentence 2.";
                var fragments = theOneLibSynphony.stringToSentences(inputText);

                expect(fragments.length).toBe(3);
                expect(fragments[0].text).toBe('This is sentence 1.');
                expect(fragments[1].text).toBe('&nbsp; ');
                expect(fragments[2].text).toBe('This is sentence 2.');
        });

        it("Empty tag between sentences", function() {
                var inputText = "This is sentence 1.<span class=\"test\"></span>This is sentence 2. This is sentence 3.";
                var fragments = theOneLibSynphony.stringToSentences(inputText);

                expect(fragments.length).toBe(5);
                expect(fragments[0].text).toBe('This is sentence 1.');
                expect(fragments[1].text).toBe('<span class=\"test\"></span>');
                expect(fragments[2].text).toBe('This is sentence 2.');
                expect(fragments[3].text).toBe(' ');
                expect(fragments[4].text).toBe('This is sentence 3.');
        });

        it("Self-closing tag between sentences", function() {
                var inputText = "This is sentence 1.<img src=\"\" title=\"test\" />This is sentence 2. This is sentence 3.";
                var fragments = theOneLibSynphony.stringToSentences(inputText);

                expect(fragments.length).toBe(5);
                expect(fragments[0].text).toBe('This is sentence 1.');
                expect(fragments[1].text).toBe('<img src=\"\" title=\"test\" />');
                expect(fragments[2].text).toBe('This is sentence 2.');
                expect(fragments[3].text).toBe(' ');
                expect(fragments[4].text).toBe('This is sentence 3.');
        });

        it("Break tag between sentences", function() {
                var input = "This is sentence 1.<br>This is sentence 2.<br />\r\nThis is sentence 3.<br/>This is sentence 4.<br></br>This is sentence 5.";
                var expected = "This is sentence 1.<br />This is sentence 2.<br />\r\nThis is sentence 3.<br />This is sentence 4.<br />This is sentence 5.";
                var fragments = theOneLibSynphony.stringToSentences(input);

                var output = '';

                for (var i = 0; i < fragments.length; i++) {

                        var fragment = fragments[i];
                        output += fragment.text;
                }

                expect(output).toBe(expected);
        });

        it("Split word arrays into graphemes", function() {
                var cumulativeWords = ['one', 'two', 'three'];
                var focusWords = ['four', 'five', 'six'];

                var graphemes = _.uniq(_.union(cumulativeWords, focusWords).join('').split(''));
                console.log(graphemes);
                expect(graphemes.length).toBe(13);
        });

        it("Two consecutive sentences wrapped", function() {
                //
        });

        it("Two sentences wrapped with one tag", function() {
                //
        });

});
