
/**
 * Decodable Leveled Reader Settings
 */
class ReaderSettings {
    levels: ReaderLevel[] = [];
    stages: ReaderStage[] = [];
    letters: string = '';
    moreWords: string = '';

}

// Defines an object to hold data about one stage in the decodable books tool
class ReaderStage {

    name: string;
    sightWords: string = '';
    letters: string = '';

    constructor(name: string) {
        this.name = name;
    }

    getName(): string {
        return this.name;
    }
}

// Defines an object to hold data about one level in the leveled reader tool
class ReaderLevel {

    name: string;
    thingsToRemember: string[] = [];

    // For each of these, 0 signifies unlimited.
    maxWordsPerPage: number = 0;
    maxWordsPerSentence: number = 0;
    maxWordsPerBook: number = 0;
    maxUniqueWordsPerBook: number = 0;

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
}

/**
 * This is a callback function passed to JSON.stringify so that the json string returned only contains the fields
 * we wish to write to the hard drive.
 * @param key
 * @param value
 * @returns {*}
 */
function ReaderSettingsReplacer(key: string, value: any): any {

    // we do not want to save the "name" value
    if (key === 'name') return undefined;

    return value;
}