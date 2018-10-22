export declare class DataGPC {
    GPC: string;
    GPCuc: string;
    Grapheme: string;
    Phoneme: string;
    Category: string;
    Combining: string;
    Frequency: number;
    TokenFreq: number;
    IPA: string;
    Alt: string[];
}

export declare class DataWord {
    Name: string;
    Count: number;
    Group: number;
    PartOfSpeech: string;
    GPCForm: string[];
    WordShape: string;
    Syllables: number;
    Reverse: string[];
    html: string;
    isSightWord: boolean;

    constructor(optionalWord?: string);
}

export declare class TextFragment {
    text: string;
    isSentence: boolean;
    isSpace: boolean;
    words: string[];

    constructor(str: string, isSpace: boolean);

    wordCount(): number;
}

export declare function clearWordCache();
