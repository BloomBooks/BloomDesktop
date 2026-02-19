/**
 * language_data.test.js
 *
 * Tests for the LanguageData class
 *
 * Created Jun 4, 2014 by Hopper
 *
 */
import { describe, it, expect, beforeEach, afterEach, beforeAll } from "vitest";
import { theOneLibSynphony, LanguageData } from "./synphony_lib.js";
import "./bloomSynphonyExtensions"; //add several functions to LanguageData

describe("LanguageData", function () {
    beforeEach(function () {
        //
    });

    afterEach(function () {
        //
    });

    it("createNewLanguageData", function () {
        var langData = new LanguageData();

        // check numbers
        expect(langData.Numbers.length).toBe(10);
        expect(langData.Numbers[0]).toBe(0);
        expect(langData.Numbers[9]).toBe(9);
    });

    it("addGraphemes", function () {
        var langData = new LanguageData();
        langData.addGrapheme("a");
        langData.addGrapheme("z");

        // check results
        expect(langData.GPCS.length).toBe(2);
        expect(langData.GPCS[0].GPC).toBe("a");
        expect(langData.GPCS[1].GPC).toBe("z");
    });

    it("addWords", function () {
        var langData = new LanguageData();
        langData.addWord("and");
        langData.addWord("or");

        // check results
        expect(langData.group1.length).toBe(2);
        expect(langData.group1[0].Name).toBe("and");
        expect(langData.group1[1].Name).toBe("or");
    });

    it("addWordsWithGpcForm", function () {
        var langData = new LanguageData();
        langData.addGrapheme([
            "a",
            "b",
            "c",
            "d",
            "e",
            "f",
            "g",
            "h",
            "i",
            "j",
            "k",
            "l",
            "m",
            "n",
            "o",
            "p",
            "q",
            "r",
            "s",
            "t",
            "u",
            "v",
            "w",
            "x",
            "y",
            "z",
            "st",
            "ph",
        ]);
        langData.addWord("and");
        langData.addWord("or");
        langData.addWord("staph");

        // check results
        expect(langData.GPCS.length).toBe(28);
        expect(langData.group1.length).toBe(3);
        expect(langData.group1[0].GPCForm).toEqual(["a", "n", "d"]);
        expect(langData.group1[1].GPCForm).toEqual(["o", "r"]);
        expect(langData.group1[2].GPCForm).toEqual(["st", "a", "ph"]);
    });

    it("addWordWithGpcFormAndGraphemeNotInList", function () {
        var langData = new LanguageData();
        langData.addGrapheme(["a", "b", "c"]);
        langData.addWord("bat");

        // check results
        expect(langData.GPCS.length).toBe(3);
        expect(langData.group1.length).toBe(1);
        expect(langData.group1[0].GPCForm).toEqual(["b", "a", "t"]);
    });

    it("addWordWithGpcFormAndCharWithLargeUnicodeValue", function () {
        var langData = new LanguageData();
        // \x100026
        langData.addGrapheme(["a", "b", "􀀦"]);
        langData.addWord("a􀀦b");

        // check results
        expect(langData.GPCS.length).toBe(3);
        expect(langData.group1.length).toBe(1);
        expect(langData.group1[0].GPCForm).toEqual(["a", "􀀦", "b"]);
    });

    it("addWordWithGpcFormAndCharWithLargeUnicodeValueNotInList", function () {
        var langData = new LanguageData();
        langData.addGrapheme(["a", "b", "c"]);
        // \x100026
        langData.addWord("a􀀦b");

        // check results
        expect(langData.GPCS.length).toBe(3);
        expect(langData.group1.length).toBe(1);
        expect(langData.group1[0].GPCForm).toEqual(["a", "􀀦", "b"]);
    });

    it("parseLangDataString", function () {
        var langStr =
            "setLangData({" +
            '"LangName": "Tok Pisin",' +
            '"LangID": "tkp",' +
            '"LanguageSortOrder": ["A","B","D","E","F","G","H","I","J","K","L","M","N","O","P","R","S","T","U","V","W","Y","a","b","d","e","f","g","h","i","j","k","l","m","n","o","p","q","r","s","t","u","v","w","y"],' +
            '"ProductivityGPCSequence": ["a","s","u","t","n","m","i","p","l","e","r","k","o","b","ai","d","g","h","w","ng","f","j","y","v","-"],' +
            '"Numbers": [0,1,2,3,4,5,6,7,8,9],' +
            '"UseFullGPCNotation": false,' +
            "GPCS: [" +
            '    {"GPC":"a","GPCuc":"A","Grapheme":"a","Phoneme":"a","Category":"vowel","Combining":"false","Frequency":1081,"TokenFreq":113732,"IPA":"","Alt":[]},' +
            '    {"GPC":"e","GPCuc":"E","Grapheme":"e","Phoneme":"e","Category":"vowel","Combining":"false","Frequency":499,"TokenFreq":66367,"IPA":"","Alt":[]},' +
            '    {"GPC":"i","GPCuc":"I","Grapheme":"i","Phoneme":"i","Category":"vowel","Combining":"false","Frequency":774,"TokenFreq":130387,"IPA":"","Alt":[]},' +
            '    {"GPC":"o","GPCuc":"O","Grapheme":"o","Phoneme":"o","Category":"vowel","Combining":"false","Frequency":369,"TokenFreq":89196,"IPA":"","Alt":[]},' +
            '    {"GPC":"u","GPCuc":"U","Grapheme":"u","Phoneme":"u","Category":"vowel","Combining":"false","Frequency":324,"TokenFreq":31559,"IPA":"","Alt":[]},' +
            '    {"GPC":"b","GPCuc":"B","Grapheme":"b","Phoneme":"b","Category":"consonant","Combining":"false","Frequency":201,"TokenFreq":31722,"IPA":"","Alt":[]},' +
            '    {"GPC":"d","GPCuc":"D","Grapheme":"d","Phoneme":"d","Category":"consonant","Combining":"false","Frequency":129,"TokenFreq":14005,"IPA":"","Alt":[]},' +
            '    {"GPC":"f","GPCuc":"F","Grapheme":"f","Phoneme":"f","Category":"consonant","Combining":"false","Frequency":57,"TokenFreq":760,"IPA":"","Alt":[]},' +
            '    {"GPC":"g","GPCuc":"G","Grapheme":"g","Phoneme":"g","Category":"consonant","Combining":"false","Frequency":82,"TokenFreq":16485,"IPA":"","Alt":[]},' +
            '    {"GPC":"h","GPCuc":"H","Grapheme":"h","Phoneme":"h","Category":"consonant","Combining":"false","Frequency":81,"TokenFreq":6405,"IPA":"","Alt":[]},' +
            '    {"GPC":"j","GPCuc":"J","Grapheme":"j","Phoneme":"j","Category":"consonant","Combining":"false","Frequency":47,"TokenFreq":3111,"IPA":"","Alt":[]},' +
            '    {"GPC":"k","GPCuc":"K","Grapheme":"k","Phoneme":"k","Category":"consonant","Combining":"false","Frequency":349,"TokenFreq":36808,"IPA":"","Alt":[]},' +
            '    {"GPC":"l","GPCuc":"L","Grapheme":"l","Phoneme":"l","Category":"consonant","Combining":"false","Frequency":419,"TokenFreq":94605,"IPA":"","Alt":[]},' +
            '    {"GPC":"m","GPCuc":"M","Grapheme":"m","Phoneme":"m","Category":"consonant","Combining":"false","Frequency":492,"TokenFreq":78981,"IPA":"","Alt":[]},' +
            '    {"GPC":"n","GPCuc":"N","Grapheme":"n","Phoneme":"n","Category":"consonant","Combining":"false","Frequency":420,"TokenFreq":63639,"IPA":"","Alt":[]},' +
            '    {"GPC":"p","GPCuc":"P","Grapheme":"p","Phoneme":"p","Category":"consonant","Combining":"false","Frequency":319,"TokenFreq":46393,"IPA":"","Alt":[]},' +
            '    {"GPC":"r","GPCuc":"R","Grapheme":"r","Phoneme":"r","Category":"consonant","Combining":"false","Frequency":382,"TokenFreq":20830,"IPA":"","Alt":[]},' +
            '    {"GPC":"s","GPCuc":"S","Grapheme":"s","Phoneme":"s","Category":"consonant","Combining":"false","Frequency":677,"TokenFreq":56827,"IPA":"","Alt":[]},' +
            '    {"GPC":"t","GPCuc":"T","Grapheme":"t","Phoneme":"t","Category":"consonant","Combining":"false","Frequency":435,"TokenFreq":51270,"IPA":"","Alt":[]},' +
            '    {"GPC":"v","GPCuc":"V","Grapheme":"v","Phoneme":"v","Category":"consonant","Combining":"false","Frequency":25,"TokenFreq":5792,"IPA":"","Alt":[]},' +
            '    {"GPC":"w","GPCuc":"W","Grapheme":"w","Phoneme":"w","Category":"consonant","Combining":"false","Frequency":76,"TokenFreq":9022,"IPA":"","Alt":[]},' +
            '    {"GPC":"y","GPCuc":"Y","Grapheme":"y","Phoneme":"y","Category":"consonant","Combining":"false","Frequency":26,"TokenFreq":12444,"IPA":"","Alt":[]},' +
            '    {"GPC":"q","GPCuc":"Q","Grapheme":"q","Phoneme":"q","Category":"consonant","Combining":"false","Frequency":0,"TokenFreq":0,"IPA":"","Alt":[]},' +
            '    {"GPC":"ai","GPCuc":"Ai","Grapheme":"ai","Phoneme":"ai","Category":"vowel","Combining":"false","Frequency":153,"TokenFreq":20735,"IPA":"","Alt":[]},' +
            '    {"GPC":"ng","GPCuc":"Ng","Grapheme":"ng","Phoneme":"ng","Category":"consonant","Combining":"false","Frequency":68,"TokenFreq":37930,"IPA":"","Alt":[]},' +
            '    {"GPC":"-","GPCuc":"-","Grapheme":"-","Phoneme":"-","Category":"other","Combining":"false","Frequency":2,"TokenFreq":268,"IPA":"","Alt":[]}' +
            "]," +
            '"VocabularyGroupsDescriptions": [],' +
            '"VocabularyGroups": 1,' +
            '"group1": [' +
            '    {"Name":"i","Count":34379,"Group":1,"PartOfSpeech":"","GPCForm":["i"],"WordShape":"v","Syllables":1,"Reverse":["i"],"wiv":"i"},' +
            '    {"Name":"long","Count":15967,"Group":1,"PartOfSpeech":"","GPCForm":["l","o","ng"],"WordShape":"cvc","Syllables":1,"Reverse":["ng","o","l"],"wic":"l","wfc":"ng","wmv":["o"]},' +
            '    {"Name":"na","Count":15611,"Group":1,"PartOfSpeech":"","GPCForm":["n","a"],"WordShape":"cv","Syllables":1,"Reverse":["a","n"],"wic":"n","wfv":"a"},' +
            '    {"Name":"ol","Count":14803,"Group":1,"PartOfSpeech":"","GPCForm":["o","l"],"WordShape":"vc","Syllables":1,"Reverse":["l","o"],"wiv":"o","wfc":"l"},' +
            '    {"Name":"bilong","Count":14383,"Group":1,"PartOfSpeech":"","GPCForm":["b","i","l","o","ng"],"WordShape":"cvcvc","Syllables":2,"Reverse":["ng","o","l","i","b"],"wic":"b","wfc":"ng","wmv":["i","o"],"wmc":["l"]},' +
            '    {"Name":"em","Count":10548,"Group":1,"PartOfSpeech":"","GPCForm":["e","m"],"WordShape":"vc","Syllables":1,"Reverse":["m","e"],"wiv":"e","wfc":"m"},' +
            '    {"Name":"dispela","Count":5592,"Group":1,"PartOfSpeech":"","GPCForm":["d","i","s","p","e","l","a"],"WordShape":"cvccvcv","Syllables":3,"Reverse":["a","l","e","p","s","i","d"],"wic":"d","wfv":"a","wmv":["i","e"],"wmcc":["s,p"],"wmc":["l"]},' +
            '    {"Name":"yupela","Count":5533,"Group":1,"PartOfSpeech":"","GPCForm":["y","u","p","e","l","a"],"WordShape":"cvcvcv","Syllables":3,"Reverse":["a","l","e","p","u","y"],"wic":"y","wfv":"a","wmv":["u","e"],"wmc":["p","l"]},' +
            '    {"Name":"mi","Count":5319,"Group":1,"PartOfSpeech":"","GPCForm":["m","i"],"WordShape":"cv","Syllables":1,"Reverse":["i","m"],"wic":"m","wfv":"i"},' +
            '    {"Name":"olsem","Count":5258,"Group":1,"PartOfSpeech":"","GPCForm":["o","l","s","e","m"],"WordShape":"vccvc","Syllables":2,"Reverse":["m","e","s","l","o"],"wiv":"o","wfc":"m","wmcc":["l,s"],"wmv":["e"]}' +
            "]" +
            "})";

        var langData = theOneLibSynphony.parseLangDataString(langStr);

        expect(langData.ProductivityGPCSequence).toEqual([
            "a",
            "s",
            "u",
            "t",
            "n",
            "m",
            "i",
            "p",
            "l",
            "e",
            "r",
            "k",
            "o",
            "b",
            "ai",
            "d",
            "g",
            "h",
            "w",
            "ng",
            "f",
            "j",
            "y",
            "v",
            "-",
        ]);
        expect(langData.GPCS.length).toEqual(26);
        expect(langData.group1.length).toEqual(10);
        expect(langData.group1[4].Name).toEqual("bilong");
        expect(langData.group1[4].Count).toEqual(14383);
    });
});
