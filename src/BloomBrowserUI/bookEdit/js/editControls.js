var SortType = {
    alphabetic: "alphabetic",
    byLength: "byLength",
    byFrequency: "byFrequency"
};

var EditControlsModel = function() {
    this.stageNumber = 1;
    this.levelNumber = 1;
    this.synphony = new SynphonyApi(); // default state
    this.sort = SortType.alphabetic;
};

EditControlsModel.prototype.incrementStage = function() {
    this.setStageNumber(this.stageNumber + 1);
};

EditControlsModel.prototype.decrementStage = function() {
    this.setStageNumber(this.stageNumber - 1);
};

EditControlsModel.prototype.setStageNumber = function(val) {
    var stages = this.synphony.getStages();
    if (val < 1 || val > stages.length) {
        return;
    }
    this.stageNumber = val;
    this.updateStageLabel();
    this.updateWordList();
    this.enableStageButtons();
};

EditControlsModel.prototype.updateStageLabel = function() {
    this.updateElementContent("stageNumber", this.synphony.getStages()[this.stageNumber - 1].getName());
}

EditControlsModel.prototype.incrementLevel = function() {
    this.setLevelNumber(this.levelNumber + 1);
};

EditControlsModel.prototype.decrementLevel = function() {
    this.setLevelNumber(this.levelNumber - 1);
};

EditControlsModel.prototype.setLevelNumber = function(val) {
    var levels = this.synphony.getLevels();
    if (val < 1 || val > levels.length) {
        return;
    }
    this.levelNumber = val;
    this.updateElementContent("levelNumber", levels[this.levelNumber - 1].getName());
    this.enableLevelButtons();
    this.updateLevelLimits();
};

EditControlsModel.prototype.sortByLength = function() {
    this.setSort(SortType.byLength);
};

EditControlsModel.prototype.sortByFrequency = function() {
    this.setSort(SortType.byFrequency);
};

EditControlsModel.prototype.sortAlphabetically = function() {
    this.setSort(SortType.alphabetic);
};

EditControlsModel.prototype.setSort = function(sortType) {
    this.sort = sortType;
    this.updateWordList();
    this.updateSortStatus();
};

EditControlsModel.prototype.updateSortStatus = function() {
    this.updateSelectedStatus("sortAlphabetic", this.sort == SortType.alphabetic);
    this.updateSelectedStatus("sortLength", this.sort == SortType.byLength);
    this.updateSelectedStatus("sortFrequency", this.sort == SortType.byFrequency);
};

var sortIconSelectedClass = "sortIconSelected"; // The class we apply to the selected sort icon
EditControlsModel.prototype.updateSelectedStatus = function(eltId, isSelected) {
    this.setPresenceOfClass(eltId, isSelected, sortIconSelectedClass);
};

// Should be called when the browser has loaded the page.
// It updates various things in the UI to be consistent with the state of things in the model.
EditControlsModel.prototype.postNavigationInit = function() {
    this.updateWordList();
    this.updateNumberOfStages();
    this.updateStageLabel();
    this.enableStageButtons();
    this.enableLevelButtons();
    this.updateLevelLimits();
};

EditControlsModel.prototype.updateNumberOfStages = function() {
    this.updateElementContent("numberOfStages", this.synphony.getStages().length.toString());
};

EditControlsModel.prototype.enableStageButtons = function() {
    this.updateDisabledStatus("decStage", this.stageNumber <= 1);
    this.updateDisabledStatus("incStage", this.stageNumber >= this.synphony.getStages().length);
};

var disabledIconClass = "disabledIcon"; // The class we apply to icons that are disabled.
EditControlsModel.prototype.updateDisabledStatus = function(eltId, isDisabled) {
    this.setPresenceOfClass(eltId, isDisabled, disabledIconClass);
};

// Find the element with the indicated ID, and make sure that it has the className in its class attribute if isWanted is true, and not otherwise.
// (Tests currently assume it will be added last, but this is not required.)
// (class names used with this method should not occur as substrings within a longer class name)
EditControlsModel.prototype.setPresenceOfClass = function(eltId, isWanted, className) {
    var old = this.getElementAttribute(eltId, "class");
    if (isWanted && old.indexOf(className) < 0) {
        this.setElementAttribute(eltId, "class", old + (old.length ? " " : "") + className);
    }
    else if (!isWanted && old.indexOf(className) >= 0) {
        this.setElementAttribute(eltId, "class", old.replace(className, "").replace("  ", " ").trim());
    }
};

EditControlsModel.prototype.enableLevelButtons = function() {
    this.updateDisabledStatus("decLevel", this.levelNumber <= 1);
    this.updateDisabledStatus("incLevel", this.levelNumber >= this.synphony.getLevels().length);
};

EditControlsModel.prototype.updateLevelLimits = function() {
    var level = this.synphony.getLevels()[this.levelNumber - 1];
    this.updateLevelLimit("maxWordsPerPage", level.getMaxWordsPerPage());
    this.updateLevelLimit("maxWordsPerPageBook", level.getMaxWordsPerPage());
    this.updateLevelLimit("maxWordsPerSentence", level.getMaxWordsPerSentence());
    this.updateLevelLimit("maxWordsPerBook", level.getMaxWordsPerBook());
    this.updateLevelLimit("maxUniqueWordsPerBook", level.getMaxUniqueWordsPerBook());
};

EditControlsModel.prototype.updateLevelLimit = function(id, limit) {
    if (limit != 0) {
        this.updateElementContent(id, limit.toString());
    }
    this.updateDisabledLimit(id, limit == 0);
};

var disabledLimitClass = "disabledLimit"; // The class we apply to max values that are disabled (0).
EditControlsModel.prototype.updateDisabledLimit = function(eltId, isDisabled) {
    this.setPresenceOfClass(eltId, isDisabled, disabledLimitClass);
};

EditControlsModel.prototype.updateWordList = function() {
    var stage = this.synphony.getStages()[this.stageNumber - 1];
    var words = stage.getWords();
    // All cases use localeCompare for alphabetic sort. This is not ideal; it will use whatever
    // locale the browser thinks is current. When we implement ldml-dependent sorting we can improve this.
    switch(this.sort) {
        case SortType.alphabetic:
            words.sort(function(a,b) { return a.localeCompare(b)});
            break;
        case SortType.byLength:
            words.sort(function(a,b) {
                if (a.length == b.length) {
                    return a.localeCompare(b);
                }
                return a.length - b.length;
            });
            break;
        case SortType.byFrequency:
            words.sort(function(a,b) {
                var aFreq = stage.getFrequency(a);
                var bFreq = stage.getFrequency(b);
                if (aFreq == bFreq) {
                    return a.localeCompare(b);
                }
                return bFreq - aFreq; // MOST frequent first
            });
            break;
    }
    // Enhance JohnT: is there a smarter way to decide # columns? Maybe the HTML could be made to do it itself?
    // "Organize this list in as many columns as fit" feels like a common task, but a quick search didn't reveal
    // an obvious existing solution.
    // Review JohnH (JohnT): should they be arranged across rows or down columns?
    var wordsPerRow = 3;
    if (words.length > 0) {
        var maxWordLength = 0;
        for (var i = 0; i < words.length; i++) {
            maxWordLength = Math.max(maxWordLength, words[i].length);
        }
        if (maxWordLength > 9) {
            wordsPerRow = 2; // a crude way of improving layout.
        }
    }
    var result = "";
    var wordIndex = 0;
    for (var i = 0; i < words.length; i++)
    {
        if (wordIndex == 0) {
            result += "<tr>";
        }
        result += "<td>" + words[i] + "</td>";
        wordIndex++;
        if (wordIndex == wordsPerRow) {
            wordIndex = 0;
            result += "</tr>"
        }
    }
    if (wordIndex != 0) {
        result += "</tr>";
    }
    this.updateElementContent("wordList", result);
};

// Should be called early on, before other init.
EditControlsModel.prototype.setSynphony = function(val) {
    this.synphony = val;
};

// This group of functions uses jquery (if loaded) to update the real model.
// Unit testing should spy or otherwise replace these functions, since $ will not be usefully defined.
EditControlsModel.prototype.updateElementContent = function(id, val) {
    $("#"+id).html(val);
};

EditControlsModel.prototype.getElementAttribute = function(id, attrName) {
    return $("#"+id).attr(attrName);
};

EditControlsModel.prototype.setElementAttribute = function(id, attrName, val) {
    $("#"+id).attr(attrName, val);
};

// Attach click handlers
var model = new EditControlsModel();
if (typeof($) == "function") {
    // Running for real, and jquery properly loaded first
    $("#incStage").click(function () {
        model.incrementStage();
    });
    $("#decStage").click(function () {
        model.decrementStage();
    });
    $("#incLevel").click(function () {
        model.incrementLevel();
    });
    $("#decLevel").click(function () {
        model.decrementLevel();
    });
    $("#sortAlphabetic").click(function () {
        model.sortAlphabetically();
    });
    $("#sortLength").click(function () {
        model.sortByLength();
    });
    $("#sortFrequency").click(function () {
        model.sortByFrequency();
    });
    $("#setUpStages").click(function () {
        alert("setup!");
    });
    // Todo PhilH: replace this fake synphony with something real.
    var synphony = new SynphonyApi();
    model.setSynphony(synphony);
    synphony.addStageWithWords("A", "the cat sat on the mat the rat sat on the cat");
    synphony.addStageWithWords("B", "cats and dogs eat rats rats eat lots");
    synphony.addStageWithWords("C", "this is a long sentence to give a better demonstration of how it handles a variety of words some of which are quite long which means if things are not confused it will make two columns");
    synphony.addLevel(jQuery.extend(new Level("1"), {maxWordsPerPage: 4, maxWordsPerSentence: 2, maxUniqueWordsPerBook: 15, maxWordsPerBook: 30}));
    synphony.addLevel(jQuery.extend(new Level("2"), {maxWordsPerPage: 6, maxWordsPerSentence: 4, maxUniqueWordsPerBook: 20,  maxWordsPerBook: 40}));
    synphony.addLevel(jQuery.extend(new Level("3"), {maxWordsPerPage: 8, maxWordsPerSentence: 5, maxUniqueWordsPerBook: 25}));
    synphony.addLevel(jQuery.extend(new Level("4"), {maxWordsPerPage: 10, maxWordsPerSentence: 6, maxUniqueWordsPerBook: 35}));
    model.postNavigationInit();
}
else {
    // running tests...or someone forgot to install jquery first
    $ = function() {
        alert("you should have loaded jquery first or blocked this call with spyOn");
    }
}