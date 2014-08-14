// Defines an object to hold Synphony data from the specified file
var SynphonyApi = function() {
    this.stages = [];
    this.levels = [];
    this.source = "";
};

/**
 * Decodable Leveled Reader Settings
 */
var DLRSettings = function() {
    this.levels = [];
    this.stages = [];
    this.letters = '';
    this.letterCombinations = '';
};

SynphonyApi.prototype.loadSettings = function(fileContent) {

    if (!lang_data) lang_data = new LanguageData();

    if (!fileContent) return;

    var data = jQuery.extend(new DLRSettings(), JSON.parse(fileContent));
    if (data.letters === '') return;

    this.source = fileContent;

    lang_data.addGrapheme(data.letters.split(' '));
    lang_data.addGrapheme(data.letterCombinations.split(' '));
    lang_data.addInsensitiveWord(data.moreWords.split(' '));

    var lvls = data.levels;
    if (lvls) {
        this.levels = [];
        for (var i = 0; i < lvls.length; i++) {
            this.addLevel(jQuery.extend(true, new Level(i+1), lvls[i]));
        }
    }

    var stgs = data.stages;
    if (stgs) {
        this.stages = [];
        for (var j = 0; j < stgs.length; j++) {
            this.AddStage(jQuery.extend(true, new Stage(j+1), stgs[j]));
        }
    }
};

SynphonyApi.fireCSharpEvent = function(eventName, eventData) {

    var event = new MessageEvent(eventName, {'view' : window, 'bubbles' : true, 'cancelable' : true, 'data' : eventData});
    document.dispatchEvent(event);
};

// This is at least useful for testing; maybe for real use.
SynphonyApi.prototype.AddStage = function(stage)
{
    this.stages.push(stage);
};

/**
 * Gets a URI that points to the directory containing the "synphonyApi.js" file.
 * @returns {String}
 */
SynphonyApi.prototype.getScriptDirectory = function() {

    var src = $('script[src$="synphonyApi.js"]').attr('src').replace('synphonyApi.js', '').replace(/\\/g, '/');
    if (!src) return '';
    return src;
};

/**
 * Add a list of words to the lang_data object
 * @param {Object} words The keys are the words, and the values are the counts
 */
SynphonyApi.prototype.addWords = function(words) {

    if (!words) return;

    var wordNames = Object.keys(words);

    if (!lang_data) lang_data = new LanguageData();
    for (var i = 0; i < wordNames.length; i++) {
        lang_data.addInsensitiveWord(wordNames[i], words[wordNames[i]]);
    }
};

// Defines an object to hold data about one stage in the decodable books tool
var Stage = function(name) {
    this.name = name;
    this.sightWords = ''; // a space-delimited string of sight words
};

Stage.prototype.getName = function() {
    return this.name;
};

/**
 *
 * @param {int} [stageNumber] Optional. If present, returns all stages up to and including stageNumber. If missing, returns all stages.
 * @returns {Array} An array of Stage objects
 */
SynphonyApi.prototype.getStages = function(stageNumber) {

    if (typeof stageNumber === 'undefined')
        return this.stages;
    else
        return _.first(this.stages, stageNumber);
};


// Defines an object to hold data about one level in the leveled reader tool
var Level = function(name) {
    this.name = name;
    this.thingsToRemember = [];

    // For each of these, 0 signifies unlimited.
    this.maxWordsPerPage = 0;
    this.maxWordsPerSentence = 0;
    this.maxWordsPerBook = 0;
    this.maxUniqueWordsPerBook = 0;

};

Level.prototype.getName = function() {
    return this.name;
};

Level.prototype.getMaxWordsPerPage = function() {
    return this.maxWordsPerPage || 0;
};

Level.prototype.getMaxWordsPerSentence = function() {
    return this.maxWordsPerSentence || 0;
};

Level.prototype.getMaxWordsPerBook = function() {
    return this.maxWordsPerBook || 0;
};

Level.prototype.getMaxUniqueWordsPerBook = function() {
    return this.maxUniqueWordsPerBook || 0;
};

SynphonyApi.prototype.getLevels = function() {
    return this.levels;
};

SynphonyApi.prototype.addLevel = function(aLevel) {
    this.levels.push(aLevel);
};