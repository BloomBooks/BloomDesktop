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

    // For each of these, default to unlimited.
    public maxWordsPerPage: number = Infinity;
    public maxWordsPerSentence: number = Infinity;
    public maxWordsPerBook: number = Infinity;
    public maxUniqueWordsPerBook: number = Infinity;
    public maxAverageWordsPerSentence: number = Infinity;

    constructor(name: string) {
        this.name = name;
    }

    public getName(): string {
        return this.name;
    }

    public getMaxWordsPerPage(): number {
        return this.maxWordsPerPage || Infinity;
    }

    public getMaxWordsPerSentence(): number {
        return this.maxWordsPerSentence || Infinity;
    }

    public getMaxWordsPerBook(): number {
        return this.maxWordsPerBook || Infinity;
    }

    public getMaxUniqueWordsPerBook(): number {
        return this.maxUniqueWordsPerBook || Infinity;
    }

    public getMaxAverageWordsPerSentence(): number {
        return this.maxAverageWordsPerSentence || Infinity;
    }
}
