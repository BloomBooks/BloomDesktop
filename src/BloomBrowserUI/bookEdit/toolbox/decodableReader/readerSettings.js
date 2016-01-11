var ReaderSettings = (function () {
    function ReaderSettings() {
        this.levels = [];
        this.stages = [];
        this.letters = '';
        this.moreWords = '';
        this.useAllowedWords = 0;
    }
    return ReaderSettings;
})();
var ReaderStage = (function () {
    function ReaderStage(name) {
        this.sightWords = '';
        this.letters = '';
        this.allowedWordsFile = '';
        this.name = name;
    }
    ReaderStage.prototype.getName = function () {
        return this.name;
    };
    ReaderStage.prototype.setAllowedWordsString = function (fileContents) {
        this.allowedWords = fileContents.split(/[,]/);
    };
    return ReaderStage;
})();
var ReaderLevel = (function () {
    function ReaderLevel(name) {
        this.thingsToRemember = [];
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
function ReaderSettingsReplacer(key, value) {
    if (key === 'name')
        return undefined;
    return value;
}
