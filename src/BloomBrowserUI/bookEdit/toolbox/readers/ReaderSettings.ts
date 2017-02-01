/// <reference path="../../../typings/underscore/underscore.d.ts" />

/**
 * Decodable Leveled Reader Settings
 */
export class ReaderSettings {
    levels: ReaderLevel[] = [];
    stages: ReaderStage[] = [];
    letters: string = '';
    moreWords: string = '';
    useAllowedWords: number = 0;
}

// Defines an object to hold data about one stage in the decodable books tool
export class ReaderStage {

    name: string;
    sightWords: string = '';
    letters: string = '';
    words: string[];
    allowedWordsFile: string = '';
    allowedWords: string[];

    constructor(name: string) {
        this.name = name;
    }

    getName(): string {
        return this.name;
    }

    setAllowedWordsString(fileContents: string): void {
        // the list of words is being cleaned and deduped by the server
        this.allowedWords = fileContents.split(/[,]/);
    }
}

// Defines an object to hold data about one level in the leveled reader tool
export class ReaderLevel {

    name: string;
    thingsToRemember: string[] = [];

    // For each of these, 0 signifies unlimited.
    maxWordsPerPage: number = 0;
    maxWordsPerSentence: number = 0;
    maxWordsPerBook: number = 0;
    maxUniqueWordsPerBook: number = 0;
    maxAverageWordsPerSentence: number = 0;

    constructor(name: string) {
        this.name = name;
    }

    getName(): string {
        return this.name;
    }

    getMaxWordsPerPage(): number {
        return this.maxWordsPerPage || 0;
    }

    getMaxWordsPerSentence(): number {
        return this.maxWordsPerSentence || 0;
    }

    getMaxWordsPerBook(): number {
        return this.maxWordsPerBook || 0;
    }

    getMaxUniqueWordsPerBook(): number {
        return this.maxUniqueWordsPerBook || 0;
    }

    getMaxAverageWordsPerSentence(): number {
        return this.maxAverageWordsPerSentence || 0;
    }
}
