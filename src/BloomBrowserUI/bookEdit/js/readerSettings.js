/**
* Decodable Leveled Reader Settings
*/
var ReaderSettings = (function () {
    function ReaderSettings() {
        this.levels = [];
        this.stages = [];
        this.letters = '';
        this.moreWords = '';
    }
    return ReaderSettings;
})();

// Defines an object to hold data about one stage in the decodable books tool
var ReaderStage = (function () {
    function ReaderStage(name) {
        this.sightWords = '';
        this.letters = '';
        this.name = name;
    }
    ReaderStage.prototype.getName = function () {
        return this.name;
    };
    return ReaderStage;
})();

// Defines an object to hold data about one level in the leveled reader tool
var ReaderLevel = (function () {
    function ReaderLevel(name) {
        this.thingsToRemember = [];
        // For each of these, 0 signifies unlimited.
        this.maxWordsPerPage = 0;
        this.maxWordsPerSentence = 0;
        this.maxWordsPerBook = 0;
        this.maxUniqueWordsPerBook = 0;
        this.name = name;
    }
    ReaderLevel.prototype.getName = function () {
        return this.name;
    };

    ReaderLevel.prototype.getMaxWordsPerPage = function () {
        return this.maxWordsPerPage || 0;
    };

    ReaderLevel.prototype.getMaxWordsPerSentence = function () {
        return this.maxWordsPerSentence || 0;
    };

    ReaderLevel.prototype.getMaxWordsPerBook = function () {
        return this.maxWordsPerBook || 0;
    };

    ReaderLevel.prototype.getMaxUniqueWordsPerBook = function () {
        return this.maxUniqueWordsPerBook || 0;
    };
    return ReaderLevel;
})();

/**
* This is a callback function passed to JSON.stringify so that the json string returned only contains the fields
* we wish to write to the hard drive.
* @param key
* @param value
* @returns {*}
*/
function ReaderSettingsReplacer(key, value) {
    // we do not want to save the "name" value
    if (key === 'name')
        return undefined;

    return value;
}
//# sourceMappingURL=readerSettings.js.map
