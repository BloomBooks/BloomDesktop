// Defines an object to hold Synphony data from the specified file
var SynphonyApi = function() {
    this.stages = [];
    this.levels = [];
    this.source = "";
};

SynphonyApi.prototype.loadSettings = function(fileContent)
{
    if (!fileContent)
        return;

    var data;
    this.source = fileContent;

    data = JSON.parse(fileContent);

    if (!lang_data) lang_data = new LanguageData();

    lang_data.addGrapheme(data.letters.split(' '));
    lang_data.addGrapheme(data.letterCombinations.split(' '));

    var levels = data.Levels;
    if (levels) {
        this.levels = [];
        for (var i = 0; i < levels.length; i++) {
            this.addLevel(jQuery.extend(new Level((i + 1).toString()), levels[i]));
        }
    }

    var stgs = data.stages;
    if (stgs) {
        this.stages = [];
        for (var i = 0; i < stgs.length; i++) {
            var newStage = jQuery.extend(true, new Stage((i + 1).toString()), stgs[i]);
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

    var dialogContents = FindOrCreateConfigDiv(this.getScriptPath());
    var h = 580;
    var w = 720;

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

SynphonyApi.prototype.addStageWithWords = function(name, words, sightWords)
{
    var stage = new Stage(name);
    stage.addWords(words);
    stage.sightWords = sightWords;
    this.stages.push(stage);
};

SynphonyApi.prototype.getScriptPath = function() {

    var src = $('script[src$="synphonyApi.js"]').attr('src').replace('synphonyApi.js', '').replace(/\\/g, '/');
    if (!src) return '';
    return src;
};

SynphonyApi.prototype.addWords = function(words) {

    if (!lang_data) lang_data = new LanguageData();
    for (var i = 0; i < words.length; i++)
        lang_data.addWord(words[i]);
};

// Defines an object to hold data about one stage in the decodable books tool
var Stage = function(name) {
    this.name = name;
    this.words = {}; // We will add words as properties to this, using it as a map. Value of each is its frequency.
    this.sightWords = ''; // a space-delimited string of sight words
};

Stage.prototype.getName = function() {
    return this.name;
};

Stage.prototype.getWords = function() {
    return Object.getOwnPropertyNames(this.words);
};

Stage.prototype.getWordObjects = function() {

    var wordObjects = [];
    var words = this.getWords();
    var wordName;
    for (var i = 0; i < words.length; i++) {
        wordName = words[i];
        wordObjects.push({"Name": wordName, "Count": this.words[wordName]});
    }
    return wordObjects;
};

Stage.prototype.getFrequency = function(word) {
    return this.words[word];
};

/**
 *
 * @param {mixed} input Either an array of strings (words), or a string containing a space-delimited list of words
 */
Stage.prototype.addWords = function(input) {

    var items;
    if (Array.isArray(input))
        items = input;
    else
        items = input.split(' ');

    for (var i = 0; i < items.length; i++) {
        var item = items[i];
        var old = this.words[item] || 0;
        this.words[item] = old + 1;
    }
};

SynphonyApi.prototype.getStages = function() {
    return this.stages;
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