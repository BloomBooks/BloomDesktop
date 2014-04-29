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

XRegExp.addUnicodeData([{name: "SEP", alias: "Sentence_Ending_Punctuation", bmp: "\u0021\u002e\u003f\u055c\u055e\u0589\u061f\u06d4\u0700\u0701\u0702\u0964\u0965\u104a\u104b\u1362\u1367\u1368\u166e\u1803\u1809\u1944\u1945\u203c\u203d\u2047\u2048\u2049\u3002\ufe52\ufe56\ufe57\uff01\uff0e\uff1f\uff61\u00a7"}]);


/******************************************************************************
 * Sentence Trailing Punctuation
 *
 * \u0022 = Double Quote
 * \u0027 = Single Quote / Apostrophe
 * \u00bb = Right-Pointing Double Angle Quote
 * \u2019 = Right Single Quote
 * \u201d = Right Double Quote
 * \u203a = Right-Pointing Single Angle Quote
 * \u2e03 = Right Substitution Bracket
 * \u2e05 = Right Dotted Substitution Bracket
 * \u2e0a = Right Transposition Bracket
 * \u2e0d = Right Raised Omission Bracket
 * \u2e1d = Right Low Paraphrase Bracket
 * \u2e21 = Right Vertical Bar With Quill
 ******************************************************************************/

XRegExp.addUnicodeData([{name: "STP", alias: "Sentence_Trailing_Punctuation", bmp: "\u0022\u0027\u00bb\u2019\u201d\u203a\u2e03\u2e05\u2e0a\u2e0d\u2e1d\u2e21"}]);