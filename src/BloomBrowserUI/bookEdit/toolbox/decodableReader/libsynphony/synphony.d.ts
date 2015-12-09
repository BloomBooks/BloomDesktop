/// <reference path="bloom_lib.d.ts" />

declare class LanguageData {

    LangName: string;
    LangID: string;
    LanguageSortOrder: string[];
    ProductivityGPCSequence: string[];
    Numbers: number[];
    GPCS: any[];
    VocabularyGroupsDescriptions: any[];
    VocabularyGroups: number;
    group1: any[];
    UseFullGPCNotation: boolean;

    addGrapheme(grapheme: string): void;
    addGrapheme(grapheme: string[]): void;
    addWord(word: string, freq?: number): void;
    addWord(word: string[], freq?: number): void;
}

declare class libSynphony {

  dbGet(key: string): any;
  dbSet(key: string, value: any): void;
  stringToSentences(textHTML: string): textFragment[];
  langDataFromString(langDataString: string): boolean;
  getWordsFromHtmlString(textHTML: string): string[];
  processVocabularyGroups(optionalLangData?: LanguageData): void;
  chooseVocabGroups(aSelectedGroups: string[]): any;

  selectGPCWordNamesWithArrayCompare(aDesiredGPCs: string[], aKnownGPCs: string[], restrictToKnownGPCs: boolean,
                                     allowUpperCase: boolean, aSyllableLengths: number[], aSelectedGroups: string[],
                                     aPartsOfSpeech: string[]): string[];

  selectGPCWordsFromCache(aDesiredGPCs: string[], aKnownGPCs: string[], restrictToKnownGPCs: boolean,
                          allowUpperCase: boolean, aSyllableLengths: number[], aSelectedGroups: string[],
                          aPartsOfSpeech: string[]): string[];
}

declare var lang_data: LanguageData;
declare var libsynphony: libSynphony;