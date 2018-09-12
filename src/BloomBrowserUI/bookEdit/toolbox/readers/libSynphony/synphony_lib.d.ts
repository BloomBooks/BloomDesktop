/// <reference path="bloomSynphonyExtensions.d.ts" />
import { TextFragment } from "./bloomSynphonyExtensions";

export class LanguageData {
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

export class LibSynphony {
    setExtraSentencePunctuation(extra: string): void;
    stringToSentences(textHTML: string): TextFragment[];
    langDataFromString(langDataString: string): boolean;
    getWordsFromHtmlString(textHTML: string): string[];
    processVocabularyGroups(optionalLangData?: LanguageData): void;
    chooseVocabGroups(aSelectedGroups: string[]): any;

    selectGPCWordNamesWithArrayCompare(
        aDesiredGPCs: string[],
        aKnownGPCs: string[],
        restrictToKnownGPCs: boolean,
        allowUpperCase: boolean | undefined,
        aSyllableLengths: number[],
        aSelectedGroups: string[],
        aPartsOfSpeech: string[]
    ): string[];

    selectGPCWordsFromCache(
        aDesiredGPCs: string[],
        aKnownGPCs: string[],
        restrictToKnownGPCs: boolean,
        allowUpperCase: boolean | undefined,
        aSyllableLengths: number[],
        aSelectedGroups: string[],
        aPartsOfSpeech: string[]
    ): string[];

    wrap_words_extra(
        storyHTML: string,
        aWords: any,
        cssClass: string,
        extra: string
    );
    checkStory(
        aFocusWordList: any,
        aWordCumulativeList: any,
        aGPCsKnown: any,
        storyHTML: any,
        sightWords: any
    );
}

export var theOneLanguageDataInstance: LanguageData;
export var theOneLibSynphony: LibSynphony;
export function ResetLanguageDataInstance(): void;

//export function StoryCheckResults(focus_words, cumulative_words, possible_words, sight_words, remaining_words, readableWordCount, totalWordCount);
