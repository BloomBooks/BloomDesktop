// Defines an object to hold Synphony data from the specified file
var SynphonyApi = function() {
    this.stages = [];
    this.levels = [];
    this.source = "";
};

/**
 * Decodable Leveled Reader Settings
 * @returns {DLRSettings}
 */
var DLRSettings = function() {
    this.letters = '';
    this.letterCombinations = '';
    this.levels = [];
    this.stages = [];
};

SynphonyApi.prototype.loadSettings = function(fileContent) {

    if (!lang_data) lang_data = new LanguageData();

    if (!fileContent) return;

    var data = jQuery.extend(new DLRSettings(), JSON.parse(fileContent));
    if (data.letters === '') return;

    this.source = fileContent;

    lang_data.addGrapheme(data.letters.split(' '));
    lang_data.addGrapheme(data.letterCombinations.split(' '));
    lang_data.addWord(data.moreWords.split(' '));

    var lvls = data.levels;
    if (lvls) {
        this.levels = [];
        for (var i = 0; i < lvls.length; i++) {
            this.addLevel(jQuery.extend(new Level(i), lvls[i]));
        }
    }

    var stgs = data.stages;
    if (stgs) {
        this.stages = [];
        for (var i = 0; i < stgs.length; i++) {
            var newStage = jQuery.extend(true, new Stage(i+1), stgs[i]);
            this.AddStage(newStage);
        }
    }
};

function FindOrCreateConfigDiv(path) {
    var dialogContents = $("body").find("div#synphonyConfig");
    if (!dialogContents.length) {
        dialogContents = $("<div id='synphonyConfig' title='Synphony Configuration'/>").appendTo($("body"));

        var url = path.replace(/\/js\/$/, '/readerSetup/ReaderSetup.htm').replace(/file:\/\/(\w)/, 'file:///$1');
        var html = '<iframe id="settings_frame" src="' + url + '" scrolling="no" style="width: 100%; height: 100%; border-width: 0; margin: 0" id="setup_frame" onload="document.getElementById(\'settings_frame\').contentWindow.postMessage(\'Data\\n\' + model.getSynphony().source, \'*\');"></iframe>';
        dialogContents.append(html);
    }
    return dialogContents;
    //document.getElementById(\'settings_frame\').contentWindow.postMessage(\'Data\\n\' + this.source, \'*\');
}

// Show the configuration dialog. If the user clicks OK, send the new file to C#, then call whenChanged()
// to let the caller update the UI.
SynphonyApi.prototype.showConfigDialog = function(whenChanged) {

    var dialogContents = FindOrCreateConfigDiv(this.getScriptDirectory());
    var h = 580;
    var w = 720;

    // This height and width will fit inside the "1024 x 586 Low-end netbook with windows Taskbar" settings
    if ((document.body.scrollWidth < 723) || (window.innerHeight < 583)) {
        h = 460;
        w = 580;
    }

    var dlg = $(dialogContents).dialog({
        autoOpen: "true",
        modal: "true",
        buttons: {
            "OK": function () {
                document.getElementById('settings_frame').contentWindow.postMessage('OK', '*');
            },
            "Cancel": function () {
                $(this).dialog("close");
            }
        },
        height: h,
        width: w
    });
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
        lang_data.addWord(wordNames[i], words[wordNames[i]]);
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

Stage.prototype.getFrequency = function(word) {
    return this.words[word];
};

/**
 *
 * @param {Int} stageNumber Optional. If present, returns all stages up to and including stageNumber. If missing, returns all stages.
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
    return this.maxWordsPerPage;
};

Level.prototype.getMaxWordsPerSentence = function() {
    return this.maxWordsPerSentence;
};

Level.prototype.getMaxWordsPerBook = function() {
    return this.maxWordsPerBook;
};

Level.prototype.getMaxUniqueWordsPerBook = function() {
    return this.maxUniqueWordsPerBook;
};

SynphonyApi.prototype.getLevels = function() {
    return this.levels;
};

SynphonyApi.prototype.addLevel = function(aLevel) {
    this.levels.push(aLevel);
};