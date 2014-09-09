/// <reference path="getIframeChannel.ts"/>

// listen for messages sent to this page
window.addEventListener('message', processDLRMessage, false);

var iframeChannel = getIframeChannel();

function getSetupDialogWindow() {
    return parent.window.document.getElementById("settings_frame").contentWindow;
}

/**
 * Respond to messages
 * @param {Event} event
 */
function processDLRMessage(event) {

    var params = event.data.split("\n");

    switch(params[0]) {
        case 'Texts': // request from setup dialog for the list of sample texts
            if (model.texts)
                getSetupDialogWindow().postMessage('Files\n' + model.texts.join("\r"), '*');
            return;

        case 'Refresh': // notification from setup dialog that settings have changed
            var synphony = model.getSynphony();
            synphony.loadSettings(JSON.parse(params[1]));
            model.updateControlContents();
            model.doMarkup();
            return;

        case 'SetupType':
            getSetupDialogWindow().postMessage('SetupType\n' + model.setupType, '*');
            return;
    }
}

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
    this.allWords = {};
    this.texts = [];
    this.textCounter = 0;
    this.setupType = '';
    this.fontName = '';
    this.readableFileExtensions = [];

    // this happens during testing
    if (iframeChannel)
        this.readableFileExtensions = iframeChannel.readableFileExtensions;

    /** @type DirectoryWatcher directoryWatcher */
    this.directoryWatcher = null;
};

ReaderToolsModel.prototype.incrementStage = function() {
    this.setStageNumber(this.stageNumber + 1);
};

ReaderToolsModel.prototype.decrementStage = function() {
    this.setStageNumber(this.stageNumber - 1);
};

ReaderToolsModel.prototype.setStageNumber = function(val) {

    val = parseInt(val);

    var stages = this.synphony.getStages();
    if (val < 1 || val > stages.length) {
        return;
    }
    this.stageNumber = val;
    this.updateStageLabel();
    this.updateWordList();
    this.enableStageButtons();
    this.saveState();
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

    val = parseInt(val);

    var levels = this.synphony.getLevels();
    if (val < 1 || val > levels.length) {
        return;
    }
    this.levelNumber = val;
    this.updateLevelLabel();
    this.enableLevelButtons();
    this.updateLevelLimits();
    this.saveState();
    this.doMarkup();
};

ReaderToolsModel.prototype.updateLevelLabel = function() {
    var levels = this.synphony.getLevels();
    if (levels.length <= 0) {
        this.updateElementContent("levelNumber", "");
        return;
    }

    if (levels.length < this.levelNumber) {
        this.setLevelNumber(levels.length);
        return;
    }

    this.updateElementContent("levelNumber", levels[this.levelNumber - 1].getName());
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
    this.updateLevelLabel();
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

/**
 * Find the element with the indicated ID, and make sure that it has the className in its class attribute
 * if isWanted is true, and not otherwise.
 * (Tests currently assume it will be added last, but this is not required.)
 * (class names used with this method should not occur as substrings within a longer class name)
 * @param {String} eltId Element ID
 * @param {Boolean} isWanted
 * @param {String} className
 */
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
    if (!level)
        level = new Level("");

    this.updateLevelLimit("maxWordsPerPage", level.getMaxWordsPerPage());
    this.updateLevelLimit("maxWordsPerPageBook", level.getMaxWordsPerPage());
    this.updateLevelLimit("maxWordsPerSentence", level.getMaxWordsPerSentence());
    this.updateLevelLimit("maxWordsPerBook", level.getMaxWordsPerBook());
    this.updateLevelLimit("maxUniqueWordsPerBook", level.getMaxUniqueWordsPerBook());

    if (level.thingsToRemember.length) {

        var list = document.getElementById('thingsToRemember');
        if (list !== null) {
            list.innerHTML = '';

            for (var i = 0; i < level.thingsToRemember.length; i++) {
                var li = document.createElement('li');
                li.appendChild(document.createTextNode(level.thingsToRemember[i]));
                list.appendChild(li);
            }
        }
    }
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

/**
 * Displays the list of words for the current Stage.
 */
ReaderToolsModel.prototype.updateWordList = function() {
    var stages = this.synphony.getStages();
    if (stages.length === 0) return;

    var words = this.getStageWordsAndSightWords(this.stageNumber);

    // All cases use localeCompare for alphabetic sort. This is not ideal; it will use whatever
    // locale the browser thinks is current. When we implement ldml-dependent sorting we can improve this.
    switch(this.sort) {
        case SortType.alphabetic:
            words.sort(function(a, b) {
                return a.Name.localeCompare(b.Name);
            });
            break;
        case SortType.byLength:
            words.sort(function(a, b) {
                if (a.Name.length === b.Name.length) {
                    return a.Name.localeCompare(b.Name);
                }
                return a.Name.length - b.Name.length;
            });
            break;
        case SortType.byFrequency:
            words.sort(function(a, b) {
                var aFreq = a.Count;
                var bFreq = b.Count;
                if (aFreq === bFreq) {
                    return a.Name.localeCompare(b.Name);
                }
                return bFreq - aFreq; // MOST frequent first
            });
            break;
    }

    // Review JohnH (JohnT): should they be arranged across rows or down columns?
    var result = "";
    for (var i = 0; i < words.length; i++) {
        var w = words[i];
        result += '<div class="word' + (w.isSightWord ? ' sight-word' : '') + '">' + w.Name + '</div>';
    }

    this.updateElementContent("wordList", result);

    $.divsToColumns('word');
};

/**
 * Get the sight words for the current stage and all previous stages.
 * Note: The list returned may contain sight words from previous stages that are now decodable.
 * @param {int} [stageNumber]
 * @returns {Array} An array of strings
 */
ReaderToolsModel.prototype.getSightWords = function(stageNumber) {

    var stages = this.synphony.getStages(stageNumber);
    var sightWords = [];
    if (stages.length > 0) {

        for (var i = 0; i < stages.length; i++) {
            if (stages[i].sightWords) sightWords = _.union(sightWords, stages[i].sightWords.split(' '));
        }
    }

    return sightWords;
};

/**
 * Get the sight words for the current stage and all previous stages as an array of DataWord objects
 * Note: The list returned may contain sight words from previous stages that are now decodable.
 * @param {int} stageNumber
 * @returns {DataWord[]}
 */
ReaderToolsModel.prototype.getSightWordsAsObjects = function(stageNumber) {

    var words = this.getSightWords(stageNumber);
    var returnVal = [];

    for (var i = 0; i < words.length; i++) {
        var dw = new DataWord(words[i]);
        dw.isSightWord = true;
        returnVal.push(dw);
    }

    return returnVal;
};

/**
 * Get the graphemes for the current stage and all previous stages
 * @param {int} stageNumber
 * @returns {Array} An array of strings
 */
ReaderToolsModel.prototype.getKnownGraphemes = function(stageNumber) {

    var stages = this.synphony.getStages(stageNumber);

    // compact to remove empty items if no graphemes are selected
    return _.compact(_.pluck(stages, 'letters').join(' ').split(' '));
};

/**
 *
 * @param {int} stageNumber
 * @returns {Array}
 */
ReaderToolsModel.prototype.getStageWords = function(stageNumber) {

    var g = this.getKnownGraphemes(stageNumber);
    if (g.length === 0) return [];
    return this.selectWordsFromSynphony(false, g, g, true, true);
};

ReaderToolsModel.prototype.getStageWordsAndSightWords = function(stageNumber) {

    // first get the sight words
    var sightWords = this.getSightWordsAsObjects(stageNumber);
    var stageWords = this.getStageWords(stageNumber);

    return _.uniq(stageWords.concat(sightWords), false, function(w) { return w.Name; });
};

/**
 * Change the markup type when the user selects a different Tool.
 * @param {int} markupType
 */
ReaderToolsModel.prototype.setMarkupType = function(markupType) {

    var newMarkupType = null;
    switch (markupType) {
    case 1:
        if (this.currentMarkupType !== MarkupType.Decodable)
            newMarkupType = MarkupType.Decodable;
        break;

    case 2:
        if (this.currentMarkupType !== MarkupType.Leveled)
            newMarkupType = MarkupType.Leveled;
        break;

    default:
        if (this.currentMarkupType !== MarkupType.None)
            newMarkupType = MarkupType.None;
        break;
    }

    // if no change, return now
    if (newMarkupType === null) return;

    if (newMarkupType !== this.currentMarkupType) {
        var page = parent.window.document.getElementById('page');
        if (page)
            $('.bloom-editable', page.contentWindow.document).removeSynphonyMarkup();
        this.currentMarkupType = newMarkupType;
        this.doMarkup();
    }

    this.saveState();
};

ReaderToolsModel.prototype.getElementsToCheck = function() {

    var page = parent.window.document.getElementById('page');

    // this happens during unit testing
    if (!page) return $(".bloom-content1");

    // if this is a cover page, return an empty set
    var cover = $('body', page.contentWindow.document).find('div.cover');
    if (cover['length'] > 0) return $();

    // not a cover page, return elements to check
    return $(".bloom-content1", page.contentWindow.document);
};

/**
 * Displays the correct markup for the current page.
 */
ReaderToolsModel.prototype.doMarkup = function() {

    if (this.currentMarkupType === MarkupType.None) return;

    var editableElements = this.getElementsToCheck();

    switch(this.currentMarkupType) {
        case MarkupType.Leveled:
            var options = {maxWordsPerSentence: this.maxWordsPerSentenceOnThisPage(), maxWordsPerPage: this.maxWordsPerPage()};
            editableElements.checkLeveledReader(options);
            this.updateMaxWordsPerSentenceOnPage();
            this.updateTotalWordsOnPage();
            this.getTextOfWholeBook();

            break;

        case MarkupType.Decodable:

            // get current stage and all previous stages
            var stages = this.synphony.getStages(this.stageNumber);
            if (stages.length === 0) return;

            // get word lists
            var cumulativeWords = this.getStageWords(this.stageNumber);
            var sightWords = this.getSightWords(this.stageNumber);

            // get known grapheme list from stages
            var knownGraphemes = this.getKnownGraphemes(this.stageNumber);

            editableElements.checkDecodableReader({
                focusWords: cumulativeWords,
                previousWords: cumulativeWords,
                sightWords: sightWords,
                knownGraphemes: knownGraphemes
            });

            break;
    }

    // the contentWindow is not available during unit testing
    var page = parent.window.document.getElementById('page');
    if (page)
        page.contentWindow.postMessage('Qtips', "*");
};

ReaderToolsModel.prototype.maxWordsPerSentenceOnThisPage = function() {
    var levels = this.synphony.getLevels();
    if (levels.length <= 0) {
        return 9999;
    }
    return levels[this.levelNumber - 1].getMaxWordsPerSentence();
};

ReaderToolsModel.prototype.maxWordsPerBook = function() {
    var levels = this.synphony.getLevels();
    if (levels.length <= 0) {
        return 999999;
    }
    return levels[this.levelNumber - 1].getMaxWordsPerBook();
};

ReaderToolsModel.prototype.maxUniqueWordsPerBook = function () {
    var levels = this.synphony.getLevels();
    if (levels.length <= 0) {
        return 99999;
    }
    return levels[this.levelNumber - 1].getMaxUniqueWordsPerBook();
};

ReaderToolsModel.prototype.maxWordsPerPage = function() {
    var levels = this.synphony.getLevels();
    if (levels.length <= 0) {
        return 9999;
    }
    return levels[this.levelNumber - 1].getMaxWordsPerPage();
};

ReaderToolsModel.prototype.getTextOfWholeBook = function () {
    iframeChannel.simpleAjaxGet('/bloom/readers/getTextOfPages', updateWholeBookCounts);
};

ReaderToolsModel.prototype.updateWholeBookCounts = function (pageSource) {
    var pageStrings = pageSource.split('\r');
    this.updateActualCount(this.countWordsInBook(pageStrings), this.maxWordsPerBook(), 'actualWordCount');
    this.updateActualCount(this.maxWordsPerPageInBook(pageStrings), this.maxWordsPerPage(), 'actualWordsPerPageBook');
    this.updateActualCount(this.uniqueWordsInBook(pageStrings), this.maxUniqueWordsPerBook(), 'actualUniqueWords');
};

ReaderToolsModel.prototype.countWordsInBook = function(pageStrings) {
    var total = 0;
    for (i = 0; i < pageStrings.length; i++) {
        var page = pageStrings[i];
        var fragments = libsynphony.stringToSentences(page);

        // remove inter-sentence space
        fragments = fragments.filter(function(frag) {
            return frag.isSentence;
        });

        for (j = 0; j < fragments.length; j++) {
            total += fragments[j].wordCount();
        }
    }
    return total;
};

ReaderToolsModel.prototype.uniqueWordsInBook = function (pageStrings) {
    var wordMap = {};
    for (i = 0; i < pageStrings.length; i++) {
        var page = pageStrings[i];
        var fragments = libsynphony.stringToSentences(page);

        // remove inter-sentence space
        fragments = fragments.filter(function (frag) {
            return frag.isSentence;
        });

        for (j = 0; j < fragments.length; j++) {
            var words = fragments[j].words;
            for (k = 0; k < words.length; k++) {
                wordMap[words[k]] = 1;
            }
        }
    }
    return Object.keys(wordMap).length;
};

ReaderToolsModel.prototype.maxWordsPerPageInBook = function(pageStrings) {
    var maxWords = 0;

    for (i = 0; i < pageStrings.length; i++) {
        var page = pageStrings[i];

        // split into sentences
        var fragments = libsynphony.stringToSentences(page);

        // remove inter-sentence space
        fragments = fragments.filter(function(frag) {
            return frag.isSentence;
        });

        var subMax = 0;
        for (j = 0; j < fragments.length; j++) {
            subMax += fragments[j].wordCount();
        }

        if (subMax > maxWords) maxWords = subMax;
    }

    return maxWords;
};

ReaderToolsModel.prototype.updateActualCount = function(actual, max, id) {
    $('#' + id).html(actual.toString());
    var acceptable = actual <= max;
    // The two styles here must match ones defined in ReaderTools.htm or its stylesheet.
    // It's important NOT to use two names where one is a substring of the other (e.g., unacceptable
    // instead of tooLarge). That will mess things up going from the longer to the shorter.
    this.setPresenceOfClass(id, acceptable, "acceptable");
    this.setPresenceOfClass(id, !acceptable, "tooLarge");
};

ReaderToolsModel.prototype.updateMaxWordsPerSentenceOnPage = function () {
    this.updateActualCount(this.getElementsToCheck().getMaxSentenceLength(), this.maxWordsPerSentenceOnThisPage(), 'actualWordsPerSentence');
};

ReaderToolsModel.prototype.updateTotalWordsOnPage = function() {
    this.updateActualCount(this.getElementsToCheck().getTotalWordCount(), this.maxWordsPerPage(), 'actualWordsPerPage');
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
    $("#" + id).html(val);
};

ReaderToolsModel.prototype.getElementAttribute = function(id, attrName) {
    return $("#" + id).attr(attrName);
};

ReaderToolsModel.prototype.setElementAttribute = function(id, attrName, val) {
    $("#" + id).attr(attrName, val);
};

/**
 * Add words from a file to the list of all words. Does not produce duplicates.
 * @param {String} fileContents
 */
ReaderToolsModel.prototype.addWordsFromFile = function(fileContents) {

    // is this a Synphony data file?
    if (fileContents.substr(0, 12) === 'setLangData(') {
        libsynphony.langDataFromString(fileContents);
    }
    else {
        var words = libsynphony.getWordsFromHtmlString(fileContents);

        for (var i = 0; i < words.length; i++) {
            this.allWords[words[i]] = 1 + (this.allWords[words[i]] || 0);
        }
    }

};

/**
 * Called when we have finished processing a sample text file.
 * If there are more files to load, request the next one.
 * If there are no more files to load, process the word list.
 */
ReaderToolsModel.prototype.getNextSampleFile = function() {

    // if there are no more files, process the word lists now
    if (this.textCounter >= this.texts.length) {
        this.addWordsToSynphony();
        this.updateWordList();
        this.doMarkup();
        processWordListChangedListeners();
        return;
    }

    // only get the contents of the file types we can read
    var fileName;
    do {
        var ext = this.texts[this.textCounter].split('.').pop();
        if (this.readableFileExtensions.indexOf(ext) > -1)
            fileName = this.texts[this.textCounter];
        this.textCounter++;
    } while (!fileName && (this.textCounter < this.texts.length));

    if (fileName)
        iframeChannel.simpleAjaxGet('/bloom/readers/getSampleFileContents', setSampleFileContents, fileName);
    else
        this.getNextSampleFile();
};

/**
 * Take the list of words collected from the sample files, add it to SynPhony, and update the Stages.
 */
ReaderToolsModel.prototype.addWordsToSynphony = function() {

    // add words to the word list
    var syn = this.getSynphony();
    syn.addWords(this.allWords);
    libsynphony.processVocabularyGroups();
};

/**
 * Gets words from SynPhony that match the input criteria
 * @param {Boolean} justWordName Return just the word names, not DataWord objects
 * @param {String[]} desiredGPCs An array of strings
 * @param {String[]} knownGPCs An array of strings
 * @param {Boolean} restrictToKnownGPCs
 * @param {Boolean} [allowUpperCase]
 * @param {int[]} [syllableLengths] An array of integers, uses 1-24 if empty
 * @param {String[]} [selectedGroups] An array of strings, uses all groups if empty
 * @param {String[]} [partsOfSpeech] An array of strings, uses all parts of speech if empty
 * @returns {Array} An array of strings or DataWord objects
 */
ReaderToolsModel.prototype.selectWordsFromSynphony = function(justWordName, desiredGPCs, knownGPCs, restrictToKnownGPCs, allowUpperCase, syllableLengths, selectedGroups, partsOfSpeech) {

    if (!selectedGroups) {
        selectedGroups = [];
        for (var i = 1; i <= lang_data.VocabularyGroups; i++)
            selectedGroups.push('group' + i);
    }

    if (!syllableLengths) {
        //using 24 as an arbitrary max number of syllables
        syllableLengths = [];
        for (var j = 1; j < 25; j++)
            syllableLengths.push(j);
    }

    if (!partsOfSpeech)
        partsOfSpeech = [];

    if (justWordName)
        return libsynphony.selectGPCWordNamesWithArrayCompare(desiredGPCs, knownGPCs, restrictToKnownGPCs, allowUpperCase, syllableLengths, selectedGroups, partsOfSpeech);
    else
        return libsynphony.selectGPCWordsWithArrayCompare(desiredGPCs, knownGPCs, restrictToKnownGPCs, allowUpperCase, syllableLengths, selectedGroups, partsOfSpeech);
};

ReaderToolsModel.prototype.saveState = function() {

    // this is needed for unit testing
    var accordion = $('#accordion');
    if (typeof accordion.accordion !== 'function') return;

    // this is also needed for unit testing
    var active = accordion.accordion('option', 'active');
    if (isNaN(active)) return;

    var state = new DRTState();
    state.stage = this.stageNumber;
    state.level = this.levelNumber;
    state.markupType = this.currentMarkupType;
    fireCSharpAccordionEvent('saveAccordionSettingsEvent', "state\tdecodableReader\t" + this.stageNumber);
    fireCSharpAccordionEvent('saveAccordionSettingsEvent', "state\tleveledReader\t" + this.levelNumber);
    libsynphony.dbSet('drt_state', state);
};

ReaderToolsModel.prototype.restoreState = function() {

    // this is needed for unit testing
    var accordion = $('#accordion');
    if (typeof accordion.accordion !== 'function') return;

    var state = libsynphony.dbGet('drt_state');
    if (!state) state = new DRTState();

    this.currentMarkupType = state.markupType;
    this.setStageNumber(state.stage);
    this.setLevelNumber(state.level);
};

function initializeDecodableRT() {

    // make sure synphony is initialized
    if (!model.getSynphony().source) {
        iframeChannel.simpleAjaxGet('/bloom/readers/getDefaultFont', setDefaultFont);
        iframeChannel.simpleAjaxGet('/bloom/readers/loadReaderToolSettings', initializeSynphony);
    }

    // use the off/on pattern so the event is not added twice if the tool is closed and then reopened
    $('#incStage').onOnce('click.readerTools', function() {
        model.incrementStage();
    });

    $('#decStage').onOnce('click.readerTools', function() {
        model.decrementStage();
    });

    $('#sortAlphabetic').onOnce('click.readerTools', function() {
        model.sortAlphabetically();
    });

    $('#sortLength').onOnce('click.readerTools', function() {
        model.sortByLength();
    });

    $('#sortFrequency').onOnce('click.readerTools', function() {
        model.sortByFrequency();
    });

    model.updateControlContents();

    setTimeout(function() { $.divsToColumns('word'); }, 100);
}

function initializeLeveledRT() {

    // make sure synphony is initialized
    if (!model.getSynphony().source) {
        iframeChannel.simpleAjaxGet('/bloom/getDefaultFont', setDefaultFont);
        iframeChannel.simpleAjaxGet('/bloom/loadReaderToolSettings', initializeSynphony);
    }

    $('#incLevel').onOnce('click.readerTools', function() {
        model.incrementLevel();
    });

    $('#decLevel').onOnce('click.readerTools', function() {
        model.decrementLevel();
    });

    model.updateControlContents();
}

function DRTState() {
    this.stage = 1;
    this.level = 1;
    this.markupType = MarkupType.Decodable;
}

var model = new ReaderToolsModel();
if (typeof ($) === "function") {

    // Running for real, and jquery properly loaded first
    model.setSynphony(new SynphonyApi());
}
else {
    // running tests...or someone forgot to install jquery first
    $ = function() {
        alert("you should have loaded jquery first or blocked this call with spyOn");
    };
}

/**
 * The function that is called to hook everything up.
 * Note: settingsFileContent may be empty.
 *
 * @param {String} settingsFileContent The content of the standard JSON) file that stores the Synphony settings for the collection.
 * @global {ReaderToolsModel) model
 */
function initializeSynphony(settingsFileContent) {

    var synphony = model.getSynphony();
    synphony.loadSettings(settingsFileContent);
    model.restoreState();

    model.updateControlContents();

    // change markup based on visible options
    $('#accordion').onOnce('accordionactivate.readerTools', function(event, ui) {
        model.setMarkupType(ui.newHeader.data('markuptype'));
    } );

    // set up a DirectoryWatcher on the Sample Texts directory
    model.directoryWatcher = new DirectoryWatcher('Sample Texts', 10);
    model.directoryWatcher.onChanged('SampleFilesChanged.ReaderTools', readerSampleFilesChanged);
    model.directoryWatcher.start();

    // get the list of sample texts
    iframeChannel.simpleAjaxGet('/bloom/readers/getSampleTextsList', setTextsList);
}

/**
 * Called in response to a request for the files in the sample texts directory
 * @param {String} textsList List of file names delimited by \r
 */
function setTextsList(textsList) {

    model.texts = textsList.split(/\r/).filter(function(e){return e;});
    model.getNextSampleFile();
}

/**
 * Called in response to a request for the contents of a sample text file
 * @param {string} fileContents
 */
function setSampleFileContents(fileContents) {
    model.addWordsFromFile(fileContents);
    model.getNextSampleFile();
}

/**
 * Called in response to a request for the contents of the book's pages
 * @param {string} pageSource
 */
function updateWholeBookCounts(pageSource) {
    model.updateWholeBookCounts(pageSource);
}

function setDefaultFont(fontName) {
    model.fontName = fontName;
}

/**
 * This method is called whenever a change is detected in the Sample Files directory
 * @@param {String[]} newFiles Names of new files
 * @@param {String[]} deletedFiles Names of deleted files
 * @@param {String[]} changedFiles Names of changed files
 */
function readerSampleFilesChanged() {

    // reset the file and word list
    lang_data = new LanguageData();
    model.allWords = {};
    model.textCounter = 0;

    var settings = model.getSynphony().source;
    model.setSynphony(new SynphonyApi());

    var synphony = model.getSynphony();
    synphony.loadSettings(settings);

    // reload the sample texts
    iframeChannel.simpleAjaxGet('/bloom/readers/getSampleTextsList', setTextsList);
}

//noinspection JSUnusedGlobalSymbols
/**
 * Gets the list of texts in the Sample Texts directory
 * @returns {String[]}
 */
function getTexts() {
    if (model.texts)
        return model.texts;
    else
        return [];
}

/**
 * A list of the functions to call when the word list changes
 */
var wordListChangedListeners = {};

//noinspection JSUnusedGlobalSymbols
/**
 * Adds a function to the list of functions to call when the word list changes
 * @param {String} listenerNameAndContext
 * @param {Function} callback
 */
function addWordListChangedListener(listenerNameAndContext, callback) {
    wordListChangedListeners[listenerNameAndContext] = callback;
}

/**
 * Notify anyone who wants to know that the word list changed
 */
function processWordListChangedListeners() {

    var handlers = Object.keys(wordListChangedListeners);
    for (var j = 0; j < handlers.length; j++)
        wordListChangedListeners[handlers[j]]();
}