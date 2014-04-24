// Defines an object to hold Synphony data from the specified file
var SynphonyApi = function() {
    this.stages = [];
    this.levels = [];
};

SynphonyApi.prototype.loadFile = function(pathname)
{
    // Todo PhilH: should load the specified file.
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
		if (old === null) {
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