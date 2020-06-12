import XRegExp from "xregexp";

/**
 * bloom_xregexp_categories.js
 *
 * XRegExp categories for Bloom
 *
 * Created Apr 17, 2014 by Phil Hopper
 *
 * Load this file after xregex-all-min.js
 *
 */

export function setSentenceEndingPunctuationForBloom() {
    /******************************************************************************
     * Sentence Ending Punctuation
     *
     * \u0021 = Exclamation Mark
     * \u002E = Full Stop
     * \u003F = Question Mark
     * \u055C = Armenian Exclamation Mark
     * \u055E = Armenian Question Mark
     * \u0589 = Armenian Full Stop
     * \u061F = Arabic Question Mark
     * \u06D4 = Arabic Full Stop
     * \u0700 = Syriac End of Paragraph
     * \u0701 = Syriac Supralinear Full Stop
     * \u0702 = Syriac Sublinear Full Stop
     * \u0964 = Devanagari Danda
     * \u0965 = Devanagari Double Danda
     * \u104A = Myanmar Sign Little Section
     * \u104B = Myanmar Sign Section
     * \u1362 = Ethiopic Full Stop
     * \u1367 = Ethiopic Question Mark
     * \u1368 = Ethiopic Paragraph Separator
     * \u166E = Canadian Syllabics Full Stop
     * \u1803 = Mongolian Full Stop
     * \u1809 = Mongolian Manchu Full Stop
     * \u1944 = Limbu Exclamation Mark
     * \u1945 = Limbu Question Mark
     * \u203C = Double Exclamation Mark
     * \u203D = Interrobang
     * \u2047 = Double Question Mark
     * \u2048 = Question Exclamation Mark
     * \u2049 = Exclamation Question Mark
     * \u3002 = Ideographic Full Stop
     * \uFE52 = Small Full Stop
     * \uFE56 = Small Question Mark
     * \uFE57 = Small Exclamation Mark
     * \uFF01 = Fullwidth Exclamation Mark
     * \uFF0E = Fullwidth Full Stop
     * \uFF1F = Fullwidth Question Mark
     * \uFF61 = Halfwidth Ideographic Full Stop
     * \u00A7 = Section Sign (used for forced segment breaks w/o punctuation)
     ******************************************************************************/

    // This version is retained in case it may be needed somewhere outside Reader tools.
    // For reader tools, the definitive list is the copy in bloomSynphonyExtensions.js, method LibSynphony.setExtraSentencePunctuation
    // (which supports allowing the user to extend the list).
    XRegExp.addUnicodeData([
        {
            name: "SEP",
            alias: "Sentence_Ending_Punctuation",
            bmp:
                "\u0021\u002e\u003f\u055c\u055e\u0589\u061f\u06d4\u0700\u0701\u0702\u0964\u0965\u104a\u104b\u1362\u1367\u1368\u166e\u1803\u1809\u1944\u1945\u203c\u203d\u2047\u2048\u2049\u3002\ufe52\ufe56\ufe57\uff01\uff0e\uff1f\uff61\u00a7"
        }
    ]);
}

setSentenceEndingPunctuationForBloom();

/******************************************************************************
 * Unambiguous Paragraph Ending Punctuation
 * Source: http://www.unicode.org/reports/tr29
 *
 * \r     = Carriage Return (paragraph break)
 * \n     = Line Feed (paragraph break)
 * \u0085 = Next Line
 * \u2028 = Line Separator {Zl}
 * \u2029 = Paragraph Separator {Zp}
 *
 * \u0001 = Replacement char for html break tag
 * \u0002 = Replacement char for Windows line break (crlf)
 *
 ******************************************************************************/

XRegExp.addUnicodeData([
    {
        name: "PEP",
        alias: "Paragraph_Ending_Punctuation",
        bmp: "\r\n\u0001\u0002\u0085\u2028\u2029"
    }
]);

/******************************************************************************
 * Sentence Continuing Punctuation
 * Source: http://www.unicode.org/reports/tr29
 *
 * \u002C = COMMA
 * \u002D = HYPHEN-MINUS
 * \u003A = COLON
 * \u055D = ARMENIAN COMMA
 * \u060C = ARABIC COMMA
 * \u060D = ARABIC DATE SEPARATOR
 * \u07F8 = NKO COMMA
 * \u1802 = MONGOLIAN COMMA
 * \u1808 = MONGOLIAN MANCHU COMMA
 * \u2013 = EN DASH
 * \u2014 = EM DASH
 * \u3001 = IDEOGRAPHIC COMMA
 * \uFE10 = PRESENTATION FORM FOR VERTICAL COMMA
 * \uFE11 = PRESENTATION FORM FOR VERTICAL IDEOGRAPHIC COMMA
 * \uFE13 = PRESENTATION FORM FOR VERTICAL COLON
 * \uFE31 = PRESENTATION FORM FOR VERTICAL EM DASH
 * \uFE32 = PRESENTATION FORM FOR VERTICAL EN DASH
 * \uFE50 = SMALL COMMA
 * \uFE51 = SMALL IDEOGRAPHIC COMMA
 * \uFE55 = SMALL COLON
 * \uFE58 = SMALL EM DASH
 * \uFE63 = SMALL HYPHEN-MINUS
 * \uFF0C = FULLWIDTH COMMA
 * \uFF0D = FULLWIDTH HYPHEN-MINUS
 * \uFF1A = FULLWIDTH COLON
 * \uFF64 = HALFWIDTH IDEOGRAPHIC COMMA
 *
 ******************************************************************************/

XRegExp.addUnicodeData([
    {
        name: "SCP",
        alias: "Sentence_Continuing_Punctuation",
        bmp:
            ",-:\u055D\u060C\u060D\u07F8\u1802\u1808\u2013\u2014\u3001\uFE10\uFE11\uFE13\uFE31\uFE32\uFE50\uFE51\uFE55\uFE58\uFE63\uFF0C\uFF0D\uFF1A\uFF64"
    }
]);
