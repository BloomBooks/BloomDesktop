// Defines an object to hold Synphony data from the specified file
var SynphonyApi = function() {
    this.stages = [];
    this.levels = [];
    this.source = "";
};

SynphonyApi.prototype.loadSettings = function(fileContent)
{
    if (!fileContent) {
        return;
    }
    var data;
    this.source = fileContent;
    // Note: for some reason this try...catch doesn't work. Errors in the json stop the program.
    // One web site hinted that the actual parsing is done in another thread and thus is not
    // considered to be inside this try...catch.
    try {
        var json = fileContent.replace(/(\r\n|\n|\r|\t)/gm, " ");
        var data = JSON.parse(json);
    }
    catch(e) {alert(e);}
    var levels = data.Levels;
    if (levels != null) {
        this.levels = [];
        for (var i = 0; i < levels.length; i++) {
            this.addLevel(jQuery.extend(new Level((i + 1).toString()), levels[i]));
        }
    }
    // Todo: load stage data.
};

function FindOrCreateConfigDiv() {
    var dialogContents = $("body").find("div#synphonyConfig");
    if (!dialogContents.length) {
        dialogContents = $("<div id='synphonyConfig' title='Synphony Configuration'/>").appendTo($("body"));

        dialogContents.append("<textarea id = 'synphonyData' rows='20' cols='70'></textarea>");
    }
    return dialogContents;
}


// Show the configuration dialog. If the user clicks OK, send the new file to C#, then call whenChanged()
// to let the caller update the UI.
SynphonyApi.prototype.showConfigDialog = function(whenChanged) {
    // Todo: this should launch the new API JohnH designed, not just this crude textarea editor.
    var dialogContents = FindOrCreateConfigDiv();
    $("#synphonyData").html(this.source);
    var _this = this;
    var dlg = $(dialogContents).dialog({
        autoOpen: "true",
        modal: "true",
        //zIndex removed in newer jquery, now we get it in the css
        buttons: {
            "OK": function () {
                _this.loadSettings($("#synphonyData").val(), false);
                event = document.createEvent('MessageEvent');
                var origin = window.location.protocol + '//' + window.location.host;
                // I don't know what all the other parameters mean, but the first is the name of the event the
                // C# is listening for, and must be exactly the string here. The fourth is the new content
                // of the file.
                event.initMessageEvent ('saveDecodableLevelSettingsEvent', true, true, _this.source, origin, 1234, window, null);
                document.dispatchEvent (event);
                $(this).dialog("close");
                whenChanged();
            },
            "Cancel": function () {
                $(this).dialog("close");
            }
        }
    });
};

// This is at least useful for testing; maybe for real use.
SynphonyApi.prototype.AddStage = function(stage)
{
    this.stages.push(stage);
};

SynphonyApi.prototype.addStageWithWords = function(name, words)
{
    var stage = new Stage(name);
    stage.incrementFrequencies(words);
    this.stages.push(stage);
};

// Defines an object to hold data about one stage in the decodable books tool
var Stage = function(name) {
    this.name = name;
    this.words = {}; // We will add words as properties to this, using it as a map. Value of each is its frequency.
};

Stage.prototype.getName = function() {
    return this.name;
};

Stage.prototype.getWords = function() {
    return Object.getOwnPropertyNames(this.words);
};

Stage.prototype.getFrequency = function(word) {
    return this.words[word];
};

// This is useful for creating test fakes. May or may not be for real API.
Stage.prototype.incrementFrequencies = function(input) {
    var items = input.split(' ');
    for (var i = 0; i < items.length; i++) {
        var item = items[i];
        var old = this.words[item];
		if (!old) {
            old = 0;
        }
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