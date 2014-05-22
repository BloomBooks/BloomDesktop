var SortType = {
    alphabetic: "alphabetic",
    byLength: "byLength",
    byFrequency: "byFrequency"
};

var MarkupType = {
    None: 0,
    Leveled: 1,
    Decodable: 2
};

var ReaderToolsModel = function() {
    this.stageNumber = 1;
    this.levelNumber = 1;
    this.synphony = new SynphonyApi(); // default state
    this.sort = SortType.alphabetic;
    this.currentMarkupType = MarkupType.Decodable;
};

ReaderToolsModel.prototype.incrementStage = function() {
    this.setStageNumber(this.stageNumber + 1);
};

ReaderToolsModel.prototype.decrementStage = function() {
    this.setStageNumber(this.stageNumber - 1);
};

ReaderToolsModel.prototype.setStageNumber = function(val) {
    var stages = this.synphony.getStages();
    if (val < 1 || val > stages.length) {
        return;
    }
    this.stageNumber = val;
    this.updateStageLabel();
    this.updateWordList();
    this.enableStageButtons();
    this.doMarkup();
};

ReaderToolsModel.prototype.updateStageLabel = function() {
    var stages = this.synphony.getStages();
    if (stages.length <= 0) {
        this.updateElementContent("stageNumber", "");
        return;
    }
    this.updateElementContent("stageNumber", stages[this.stageNumber - 1].getName());
};

ReaderToolsModel.prototype.incrementLevel = function() {
    this.setLevelNumber(this.levelNumber + 1);
};

ReaderToolsModel.prototype.decrementLevel = function() {
    this.setLevelNumber(this.levelNumber - 1);
};

ReaderToolsModel.prototype.setLevelNumber = function(val) {
    var levels = this.synphony.getLevels();
    if (val < 1 || val > levels.length) {
        return;
    }
    this.levelNumber = val;
    this.updateElementContent("levelNumber", levels[this.levelNumber - 1].getName());
    this.enableLevelButtons();
    this.updateLevelLimits();
    this.doMarkup();
};

ReaderToolsModel.prototype.sortByLength = function() {
    this.setSort(SortType.byLength);
};

ReaderToolsModel.prototype.sortByFrequency = function() {
    this.setSort(SortType.byFrequency);
};

ReaderToolsModel.prototype.sortAlphabetically = function() {
    this.setSort(SortType.alphabetic);
};

ReaderToolsModel.prototype.setSort = function(sortType) {
    this.sort = sortType;
    this.updateWordList();
    this.updateSortStatus();
};

ReaderToolsModel.prototype.updateSortStatus = function() {
    this.updateSelectedStatus("sortAlphabetic", this.sort === SortType.alphabetic);
    this.updateSelectedStatus("sortLength", this.sort === SortType.byLength);
    this.updateSelectedStatus("sortFrequency", this.sort === SortType.byFrequency);
};

var sortIconSelectedClass = "sortIconSelected"; // The class we apply to the selected sort icon
ReaderToolsModel.prototype.updateSelectedStatus = function(eltId, isSelected) {
    this.setPresenceOfClass(eltId, isSelected, sortIconSelectedClass);
};

// Should be called when the browser has loaded the page, and when the user has changed configuration.
// It updates various things in the UI to be consistent with the state of things in the model.
ReaderToolsModel.prototype.updateControlContents = function() {
    this.updateWordList();
    this.updateNumberOfStages();
    this.updateNumberOfLevels();
    this.updateStageLabel();
    this.enableStageButtons();
    this.enableLevelButtons();
    this.updateLevelLimits();
};

ReaderToolsModel.prototype.updateNumberOfStages = function() {
    this.updateElementContent("numberOfStages", this.synphony.getStages().length.toString());
};

ReaderToolsModel.prototype.updateNumberOfLevels = function() {
    this.updateElementContent("numberOfLevels", this.synphony.getLevels().length.toString());
};

ReaderToolsModel.prototype.enableStageButtons = function() {
    this.updateDisabledStatus("decStage", this.stageNumber <= 1);
    this.updateDisabledStatus("incStage", this.stageNumber >= this.synphony.getStages().length);
};

var disabledIconClass = "disabledIcon"; // The class we apply to icons that are disabled.
ReaderToolsModel.prototype.updateDisabledStatus = function(eltId, isDisabled) {
    this.setPresenceOfClass(eltId, isDisabled, disabledIconClass);
};

// Find the element with the indicated ID, and make sure that it has the className in its class attribute if isWanted is true, and not otherwise.
// (Tests currently assume it will be added last, but this is not required.)
// (class names used with this method should not occur as substrings within a longer class name)
ReaderToolsModel.prototype.setPresenceOfClass = function(eltId, isWanted, className) {
    var old = this.getElementAttribute(eltId, "class");

    // this can happen during testing
    if (!old) old = "";

    if (isWanted && old.indexOf(className) < 0) {
        this.setElementAttribute(eltId, "class", old + (old.length ? " " : "") + className);
    }
    else if (!isWanted && old.indexOf(className) >= 0) {
        this.setElementAttribute(eltId, "class", old.replace(className, "").replace("  ", " ").trim());
    }
};

ReaderToolsModel.prototype.enableLevelButtons = function() {
    this.updateDisabledStatus("decLevel", this.levelNumber <= 1);
    this.updateDisabledStatus("incLevel", this.levelNumber >= this.synphony.getLevels().length);
};

ReaderToolsModel.prototype.updateLevelLimits = function() {
    var level = this.synphony.getLevels()[this.levelNumber - 1];
    if (!level) {
        level = new Level("");
    }
    this.updateLevelLimit("maxWordsPerPage", level.getMaxWordsPerPage());
    this.updateLevelLimit("maxWordsPerPageBook", level.getMaxWordsPerPage());
    this.updateLevelLimit("maxWordsPerSentence", level.getMaxWordsPerSentence());
    this.updateLevelLimit("maxWordsPerBook", level.getMaxWordsPerBook());
    this.updateLevelLimit("maxUniqueWordsPerBook", level.getMaxUniqueWordsPerBook());
};

ReaderToolsModel.prototype.updateLevelLimit = function(id, limit) {
    if (limit !== 0) {
        this.updateElementContent(id, limit.toString());
    }
    this.updateDisabledLimit(id, limit === 0);
};

var disabledLimitClass = "disabledLimit"; // The class we apply to max values that are disabled (0).
ReaderToolsModel.prototype.updateDisabledLimit = function(eltId, isDisabled) {
    this.setPresenceOfClass(eltId, isDisabled, disabledLimitClass);
};

ReaderToolsModel.prototype.updateWordList = function() {
    var stages = this.synphony.getStages();
    var words = [];
    if (stages.length > 0) {
        var stage = stages[this.stageNumber - 1];
        words = stage.getWords();
    }
    // All cases use localeCompare for alphabetic sort. This is not ideal; it will use whatever
    // locale the browser thinks is current. When we implement ldml-dependent sorting we can improve this.
    switch(this.sort) {
        case SortType.alphabetic:
            words.sort(function(a,b) { return a.localeCompare(b);});
            break;
        case SortType.byLength:
            words.sort(function(a,b) {
                if (a.length === b.length) {
                    return a.localeCompare(b);
                }
                return a.length - b.length;
            });
            break;
        case SortType.byFrequency:
            words.sort(function(a,b) {
                var aFreq = stage.getFrequency(a);
                var bFreq = stage.getFrequency(b);
                if (aFreq === bFreq) {
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
        if (wordIndex === 0) {
            result += "<tr>";
        }
        result += "<td>" + words[i] + "</td>";
        wordIndex++;
        if (wordIndex === wordsPerRow) {
            wordIndex = 0;
            result += "</tr>";
        }
    }
    if (wordIndex !== 0) {
        result += "</tr>";
    }
    this.updateElementContent("wordList", result);
};

ReaderToolsModel.prototype.setMarkupType = function(markupType) {

    if ((typeof markupType === 'undefined') || markupType === null) return;

    var newMarkupType = null;
    switch (markupType) {
        case 0:
            if (this.currentMarkupType !== MarkupType.Decodable)
                newMarkupType = MarkupType.Decodable;
            break;

        case 1:
            if (this.currentMarkupType !== MarkupType.Leveled)
                newMarkupType = MarkupType.Leveled;
            break;

        case 2:
            if (this.currentMarkupType !== MarkupType.None)
                newMarkupType = MarkupType.None;
            break;
    }

    // if no change, return now
    if (newMarkupType === null) return;

    if (newMarkupType !== this.currentMarkupType) {
        $('.bloom-editable').removeSynphonyMarkup();
        this.currentMarkupType = newMarkupType;
        this.doMarkup();
    }
};

ReaderToolsModel.prototype.doMarkup = function() {
    switch (this.currentMarkupType) {
        case MarkupType.None:
            break;

        case MarkupType.Leveled:
            var options = {maxWordsPerSentence: this.maxWordsPerSentenceOnThisPage()};
            $(".bloom-editable").checkLeveledReader(options);
            this.updateMaxWordsPerSentenceOnPage();
            this.updateTotalWordsOnPage();
            break;

        case MarkupType.Decodable:
            var stages = this.synphony.getStages();
            if (stages.length === 0) return;

            // get word lists
            var cumulativeWords = [];
            for (var i = 0; i < (this.stageNumber - 1); i++)
                cumulativeWords = cumulativeWords.concat(stages[i].getWordObjects());

            var focusWords = stages[this.stageNumber - 1].getWords();
            var sightWords = stages[this.stageNumber - 1].sightWords;

            // for now, build known grapheme list from words
            var knownGraphemes = _.uniq(_.union(_.pluck(cumulativeWords, 'Name'), focusWords).join('').split(''));

            $(".bloom-editable").checkDecodableReader({
                focusWords: focusWords,
                previousWords: cumulativeWords,
                sightWords: sightWords,
                knownGraphemes: knownGraphemes
            });

            break;
    }
};

ReaderToolsModel.prototype.lostFocus = function(element) {
    this.doMarkup();
};

ReaderToolsModel.prototype.maxWordsPerSentenceOnThisPage = function() {
    var levels = this.synphony.getLevels();
    if (levels.length <= 0) {
        return 9999;
    }
    return levels[this.levelNumber - 1].getMaxWordsPerSentence();
};

ReaderToolsModel.prototype.maxWordsPerPage = function() {
    var levels = this.synphony.getLevels();
    if (levels.length <= 0) {
        return 9999;
    }
    return levels[this.levelNumber - 1].getMaxWordsPerPage();
};

ReaderToolsModel.prototype.updateMaxWordsPerSentenceOnPage = function() {
    var max = $(".bloom-editable").getMaxSentenceLength();
    $("#actualWordsPerSentence").html(max.toString());
    var acceptable = max <= this.maxWordsPerSentenceOnThisPage();
    // The two styles here must match ones defined in ReaderTools.htm or its stylesheet.
    // It's important NOT to use two names where one is a substring of the other (e.g., unacceptable
    // instead of tooLarge). That will mess things up going from the longer to the shorter.
    this.setPresenceOfClass("actualWordsPerSentence", acceptable, "acceptable");
    this.setPresenceOfClass("actualWordsPerSentence", !acceptable, "tooLarge");
};

ReaderToolsModel.prototype.updateTotalWordsOnPage = function() {
    var count = $(".bloom-editable").getTotalWordCount();
    $("#actualWordsPerPage").html(count.toString());
    var acceptable = count <= this.maxWordsPerPage();
    this.setPresenceOfClass("actualWordsPerPage", acceptable, "acceptable");
    this.setPresenceOfClass("actualWordsPerPage", !acceptable, "tooLarge");
};

// Should be called early on, before other init.
ReaderToolsModel.prototype.setSynphony = function(val) {
    this.synphony = val;
};

ReaderToolsModel.prototype.getSynphony = function() {
    return this.synphony;
};

// This group of functions uses jquery (if loaded) to update the real model.
// Unit testing should spy or otherwise replace these functions, since $ will not be usefully defined.
ReaderToolsModel.prototype.updateElementContent = function(id, val) {
    $("#"+id).html(val);
};

ReaderToolsModel.prototype.getElementAttribute = function(id, attrName) {
    return $("#"+id).attr(attrName);
};

ReaderToolsModel.prototype.setElementAttribute = function(id, attrName, val) {
    $("#"+id).attr(attrName, val);
};

// Attach click handlers
var model = new ReaderToolsModel();
if (typeof($) === "function") {
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
    $("#setUpStages").click(function (clickEvent) {
        clickEvent.preventDefault(); // don't try to follow nonexistent href
       model.getSynphony().showConfigDialog(function() {
           model.updateControlContents();
           // Todo: update the doc content also, if relevant limits changed
           // Todo: update model.levelNumber, if it is now out of range.
       });
    });
    // Todo PhilH: replace this fake synphony with something real.
    var synphony = new SynphonyApi();
    model.setSynphony(synphony);
    initializeSynphony("", true);
    // invoke function when a bloom-editable element loses focus
    $(".bloom-editable").focusout(function() {
        model.lostFocus(this); // This is the element that just lost focus.
    });
}
else {
    // running tests...or someone forgot to install jquery first
    $ = function() {
        alert("you should have loaded jquery first or blocked this call with spyOn");
    };
}

// The function that the C# code calls to hook everything up.
// settingsFileContent should be the content of the standard file that stores the Synphony settings for the collection.
// (Note that it may be empty.) For debugging and demo purposes we generate some fake data if fakeIt is true
// and the attempt to load the file does not produce anything.
function initializeSynphony(settingsFileContent, fakeIt) {
    var synphony = model.getSynphony();
    synphony.loadSettings(settingsFileContent);
    if (fakeIt && synphony.getStages().length === 0 && synphony.getLevels().length === 0) {
        synphony.addStageWithWords("1", "the cat sat on the mat the rat sat on the cat", "canine feline");
        synphony.addStageWithWords("2", "cats and dogs eat rats rats eat lots", "carnivore omnivore");
        synphony.addStageWithWords("3", "this is a long sentence to give a better demonstration of how it handles a variety of words some of which are quite long which means if things are not confused it will make two columns", "sentence paragraph");
        synphony.addLevel(jQuery.extend(new Level("1"), {maxWordsPerPage: 4, maxWordsPerSentence: 2, maxUniqueWordsPerBook: 15, maxWordsPerBook: 30}));
        synphony.addLevel(jQuery.extend(new Level("2"), {maxWordsPerPage: 6, maxWordsPerSentence: 4, maxUniqueWordsPerBook: 20,  maxWordsPerBook: 40}));
        synphony.addLevel(jQuery.extend(new Level("3"), {maxWordsPerPage: 8, maxWordsPerSentence: 5, maxUniqueWordsPerBook: 25}));
        synphony.addLevel(jQuery.extend(new Level("4"), {maxWordsPerPage: 10, maxWordsPerSentence: 6, maxUniqueWordsPerBook: 35}));
    }
    model.updateControlContents();
    model.doMarkup();

    // change markup based on visible options
    $('#accordion').children('h3').on('click', function() { model.setMarkupType($(this).data('markuptype')); });
};