/// <reference path="../../../typings/underscore/underscore.d.ts" />

/**
 * Decodable Leveled Reader Settings
 */
export class ReaderSettings {
    public levels: ReaderLevel[] = [];
    public stages: ReaderStage[] = [];
    public letters: string = "";
    public sentencePunct: string = "";
    public moreWords: string = "";
    public useAllowedWords: number = 0;
}

// Defines an object to hold data about one stage in the decodable books tool
export class ReaderStage {
    public name: string;
    public sightWords: string = "";
    public letters: string = "";
    public words: string[];
    public allowedWordsFile: string = "";
    public allowedWords: string[];

    constructor(name: string) {
        this.name = name;
    }

    public getName(): string {
        return this.name;
    }

    public setAllowedWordsString(fileContents: string): void {
        // the list of words is being cleaned and deduped by the server
        this.allowedWords = fileContents.split(/[,]/);
    }
}

// Defines an object to hold data about one level in the leveled reader tool
export class ReaderLevel {
    public name: string;
    public thingsToRemember: string[] = [];

    // For each of these, 0 signifies unlimited.
    public maxWordsPerSentence: number = 0;
    public maxWordsPerPage: number = 0;
    public maxWordsPerBook: number = 0;
    public maxUniqueWordsPerBook: number = 0;
    public maxGlyphsPerWord: number = 0;
    public maxSentencesPerPage: number = 0;
    public maxAverageWordsPerSentence: number = 0;
    public maxAverageWordsPerPage: number = 0;
    public maxAverageSentencesPerPage: number = 0;
    public maxAverageGlyphsPerWord: number = 0;

    constructor(name: string) {
        this.name = name;
    }

    public getName(): string {
        return this.name;
    }

    public getMaxWordsPerPage(): number {
        return this.maxWordsPerPage || 0;
    }

    public getMaxWordsPerSentence(): number {
        return this.maxWordsPerSentence || 0;
    }

    public getMaxWordsPerBook(): number {
        return this.maxWordsPerBook || 0;
    }

    public getMaxUniqueWordsPerBook(): number {
        return this.maxUniqueWordsPerBook || 0;
    }

    public getMaxAverageWordsPerSentence(): number {
        return this.maxAverageWordsPerSentence || 0;
    }

    public getMaxAverageWordsPerPage(): number {
        return this.maxAverageWordsPerPage || 0;
    }

    public getMaxAverageGlyphsPerWord(): number {
        return this.maxAverageGlyphsPerWord || 0;
    }

    public getMaxAverageSentencesPerPage(): number {
        return this.maxAverageSentencesPerPage || 0;
    }

    public getMaxSentencesPerPage(): number {
        return this.maxSentencesPerPage || 0;
    }

    public getMaxGlyphsPerWord(): number {
        return this.maxGlyphsPerWord || 0;
    }
}
