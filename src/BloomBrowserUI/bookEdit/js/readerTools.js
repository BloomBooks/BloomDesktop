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

        case 'Words': // request from setup dialog for a list of words for a stage
            var words = model.selectWordsFromSynphony(false, params[1].split(' '), params[1].split(' '), true, true);
            getSetupDialogWindow().postMessage('Words\n' + JSON.stringify(words), '*');
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

        case 'SetMarkupType':
            model.setMarkupType(parseInt(params[1]));
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
    this.currentMarkupType = MarkupType.None;
    this.allWords = {};
    this.texts = [];
    this.textCounter = 0;
    this.setupType = '';
    this.fontName = '';
    this.readableFileExtensions = [];
    this.keypressTimer = null;

    // this happens during testing
    if (iframeChannel)
        this.readableFileExtensions = iframeChannel.readableFileExtensions;

    /** @type DirectoryWatcher directoryWatcher */
    this.directoryWatcher = null;

    // remember words so we can update the counts real-time
    this.bookPageWords = [];
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
    this.updateLetterList();
    this.enableStageButtons();
    this.saveState();
    this.doMarkup();
    this.updateWordList();
};

ReaderToolsModel.prototype.updateStageLabel = function() {
    var stages = this.synphony.getStages();
    if (stages.length <= 0) {
        this.updateElementContent("stageNumber", "0");
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
        this.updateElementContent("levelNumber", "0");
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
    this.updateSortStatus();
    this.updateWordList();
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
    this.updateLetterList();
    this.updateNumberOfStages();
    this.updateNumberOfLevels();
    this.updateStageLabel();
    this.enableStageButtons();
    this.enableLevelButtons();
    this.updateLevelLimits();
    this.updateLevelLabel();
    this.updateWordList();
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
        level = new ReaderLevel("");

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

    document.getElementById('wordList').innerHTML = '';

    // using setTimeout to jump to another thread so the page refreshes before the word list is displayed
    setTimeout(function() {

        var stages = model.synphony.getStages();
        if (stages.length === 0) return;

        var words = model.getStageWordsAndSightWords(model.stageNumber);

        // All cases use localeCompare for alphabetic sort. This is not ideal; it will use whatever
        // locale the browser thinks is current. When we implement ldml-dependent sorting we can improve this.
        switch(model.sort) {
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
            default:
        }

        // add the words
        var result = '';
        var longestWord = '';
        for (var i = 0; i < words.length; i++) {
            var w = words[i];
            result += '<div class="word' + (w.isSightWord ? ' sight-word' : '') + '">' + w.Name + '</div>';
            if (w.Name.length > longestWord.length) longestWord = w.Name;
        }

        model.updateElementContent("wordList", result);

        $.divsToColumnsFaster('word', longestWord);
    }, 10);
};

/**
 * Displays the list of letters for the current Stage.
 */
ReaderToolsModel.prototype.updateLetterList = function() {
    var stages = this.synphony.getStages();
    if (stages.length === 0) return;

    // Letters up through current stage
    var letters = this.getKnownGraphemes(this.stageNumber);

    // All the letters in the order they were entered on the Letters tab in the set up dialog
    var allLetters = this.synphony.source.letters.split(' ');

    // Sort our letters based on the order they were entered
    letters.sort(function(a, b) {
        return allLetters.indexOf(a) - allLetters.indexOf(b);
    });

    var result = "";
    for (var i = 0; i < letters.length; i++) {
        var letter = letters[i];
        result += '<div class="letter">' + letter + '</div>';
    }

    this.updateElementContent("letterList", result);

    $.divsToColumns('letter');
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
    if (!page) {
        return $('.bloom-page')
            .not('.bloom-frontMatter, .bloom-backMatter')
            .find('.bloom-content1.bloom-editable');
    }

    // if this is a cover page, return an empty set
    var cover = $('body', page.contentWindow.document).find('div.cover');
    if (cover['length'] > 0) return $();

    // not a cover page, return elements to check
    return $('.bloom-page', page.contentWindow.document)
        .not('.bloom-frontMatter, .bloom-backMatter')
        .find('.bloom-content1.bloom-editable');
};

// Make a selection in the specified node at the specified offset
// if divBrCount is >=0, we expect to make the selection offset characters into node itself
// (typically the root div). After traversing offset characters, we will try to additionally
// traverse divBrCount <br> elements.
ReaderToolsModel.prototype.selectAtOffset = function(node, offset) {
    var range = parent.window.document.getElementById('page').contentWindow.document.createRange();
    range.setStart(node, offset);
    range.setEnd(node, offset);
    var selection1 = parent.window.document.getElementById('page').contentWindow.getSelection();
    selection1.removeAllRanges();
    selection1.addRange(range);
//    console.log("Selected at " + offset + " in node of type " + node.localName + " with text '" + node.textContent + "' (of length) " + node.textContent.length);
//    if (node.localName === null && node.textContent.length > 0 && node.textContent.charCodeAt(0) == 10) {
//        console.log("first character " + node.textContent.charCodeAt(0));
//    }
};

ReaderToolsModel.prototype.makeSelectionIn = function(node, offset, divBrCount, atStart) {
    if (node.nodeType === 3) {
        // drilled down to a text node. Make the selection.
        this.selectAtOffset(node, offset);
        return true;
    }
    var i = 0;
    var childNode;
    var len;
    for (; i < node.childNodes.length && offset >= 0; i++) {
        childNode = node.childNodes[i];
        len = childNode.textContent.length;
        if (divBrCount >= 0 && len == offset) {
            // We want the selection after childNode itself, plus if possible an additional divBrCount <br> elements
            for(i++;
                i < node.childNodes.length && divBrCount > 0 && node.childNodes[i].textContent.length == 0;
                i++) {
                if (node.childNodes[i].localName === 'br') divBrCount--;
            }
            // We want the selection in node itself, before childNode[i].
            this.selectAtOffset(node, i);
            return true;
        }
        // If it's at the end of a child (that is not the last child) we have a choice whether to put it at the
        // end of that node or the start of the following one. For some reason the IP is invisible if
        // placed at the end of the preceding one, so prefer the start of the following one, which is why
        // we generally call this routine with atStart true.
        // (But, of course, if it is the last node we must be able to put the IP at the very end.)
        // When trying to do a precise restore, we pass atStart carefully, as it may control
        // whether we end up before or after some <br>s
        if (offset < len || (offset == len && (i == node.childNodes.length - 1 || !atStart))) {
            if (this.makeSelectionIn(childNode, offset, -1, atStart)) {
                return true;
            }
        }
        offset -= len;
    }
    // Somehow we failed. Maybe the node it should go in has no text?
    // See if we can put it at the right position (or as close as possible) in an earlier node.
    // Not sure exactly what case required this...possibly markup included some empty spans?
    for (i--; i >= 0; i--)
    {
        childNode = node.childNodes[i];
        len = childNode.textContent.length;
        if (this.makeSelectionIn(childNode, len, -1, atStart)) {return true;}
    }
    // can't select anywhere (maybe this has no text-node children? Hopefully the caller can find
    // an equivalent place in an adjacent node).
    return false;
};

ReaderToolsModel.prototype.doKeypressMarkup = function() {
    if (this.keypressTimer && $.isFunction(this.keypressTimer.clearTimeout)) {
        this.keypressTimer.clearTimeout();
    }
    var self = this;
    this.keypressTimer = setTimeout(function() {
        // This happens 500ms after the user stops typing.
        var page = parent.window.document.getElementById('page');
        if (!page) return; // unit testing?
        var selection = page.contentWindow.getSelection();
        var current = selection.anchorNode;
        var active = $(selection.anchorNode).closest('div').get(0);
        if (!active || selection.rangeCount > 1 || (selection.rangeCount == 1 && !selection.getRangeAt(0).collapsed)) {
            return; // don't even try to adjust markup while there is some complex selection
        }
        var myRange = selection.getRangeAt(0).cloneRange();
        myRange.setStart(active, 0);
        var offset = myRange.toString().length;
        // In case the IP is somewhere like after the last <br> or between <br>s,
        // its anchorNode is the div itself, or perhaps one of its spans, and we want to try to put it back
        // in a comparable position. -1 marks a selection that is at a text level.
        // other values count the <br> elements immediately before the selection.
        // I am hoping it doesn't happen that there are <br>s at multiple levels.
        // Note that the newly marked up version will have any <br>s at the top level only (children of the div).
        var divBrCount = -1;
        if (current.nodeType !== 3)
        {
            divBrCount = 0;
            // endoffset counts the number of childNodes that the selection is after.
            // We want to know how many <br> nodes are between it and the previous non-empty node.
            for (k = myRange.endOffset - 1; k >= 0; k-- ) {
                if (current.childNodes[k].localName === 'br') divBrCount++;
                else if (current.childNodes[k].textContent.length > 0) break;
            }
        }
        //console.log("-----------------");
        //console.log('before: ' + active.innerHTML);
        var atStart = myRange.endOffset === 0;

        self.doMarkup();

        //console.log('after: ' + active.innerHTML);
        //console.log('restoring selection using ' + offset + " with brs " + divBrCount + " and atStart " + atStart);
        // Now we try to restore the selection at the specified position.
        self.makeSelectionIn(active, offset, divBrCount, atStart);

    }, 500);
};

ReaderToolsModel.prototype.getActiveElementSelectionIndex = function() {
    var page = parent.window.document.getElementById('page');
    if (!page) return -1; // unit testing?
    var selection = page.contentWindow.getSelection();
    var current = selection.anchorNode;
    var active = $(selection.anchorNode).closest('div').get(0);
    if (active != this.activeElement) return -1; // huh??
    if (!active || selection.rangeCount == 0 ) {
        return -1;
    }
    var myRange = selection.getRangeAt(0).cloneRange();
    myRange.setStart(active, 0);
    return myRange.toString().length;
};


ReaderToolsModel.prototype.noteFocus = function(element) {
    this.activeElement = element;
    this.undoStack = [];
    this.redoStack = [];
    this.undoStack.push({html: element.innerHTML, text: element.textContent, offset: this.getActiveElementSelectionIndex()});
    //alert(undoStack.last);
};

ReaderToolsModel.prototype.shouldHandleUndo = function() {
    return this.currentMarkupType !== MarkupType.None;
};

ReaderToolsModel.prototype.undo = function() {
    if (!this.activeElement) return;
    if (this.activeElement.textContent == this.undoStack[this.undoStack.length - 1].text && this.undoStack.length > 1) {
        this.redoStack.push(this.undoStack.pop());
    }
    this.activeElement.innerHTML = this.undoStack[this.undoStack.length - 1].html;
    var restoreOffset = this.undoStack[this.undoStack.length - 1].offset;
    if (restoreOffset < 0) return;
    this.makeSelectionIn(this.activeElement, restoreOffset, null, true);
};

ReaderToolsModel.prototype.canUndo = function() {
    if (!this.activeElement) return 'no';
    if (this.undoStack && (this.undoStack.length > 1 || this.activeElement.textContent !== this.undoStack[0].text)) {
        return 'yes';
    }
    return 'no';
};


ReaderToolsModel.prototype.redo = function() {
    if (!this.activeElement) return;
    if (this.redoStack.length > 0) {
        this.undoStack.push(this.redoStack.pop());
    }
    this.activeElement.innerHTML = this.undoStack[this.undoStack.length - 1].html;
    var restoreOffset = this.undoStack[this.undoStack.length - 1].offset;
    if (restoreOffset < 0) return;
    this.makeSelectionIn(this.activeElement, restoreOffset, null, true);
};

/**
 * Displays the correct markup for the current page.
 */
ReaderToolsModel.prototype.doMarkup = function() {

    if (this.currentMarkupType === MarkupType.None) return;

    var oldSelectionPosition = -1;
    if (this.activeElement) oldSelectionPosition = this.getActiveElementSelectionIndex();

    var editableElements = this.getElementsToCheck();

    // qtips can be orphaned if the element they belong to is deleted
    // (and so the mouse can't move off their owning element, and they never go away).
    if (editableElements.length > 0)
        $(editableElements[0]).closest('body').children('.qtip').remove();

    switch(this.currentMarkupType) {
        case MarkupType.Leveled:

            if (editableElements.length > 0) {
                var options = {maxWordsPerSentence: this.maxWordsPerSentenceOnThisPage(), maxWordsPerPage: this.maxWordsPerPage()};
                editableElements.checkLeveledReader(options);

                // update current page words
                var pageDiv = $('body', iframeChannel.getPageWindow().document).find('div.bloom-page');
                if (pageDiv.length) {
                    if (pageDiv[0].id) {

                        this.bookPageWords[pageDiv[0].id] = editableElements['allWords'];
                        console.log(this.bookPageWords);
                    }
                }
            }

            this.updateMaxWordsPerSentenceOnPage();
            this.updateTotalWordsOnPage();
            this.displayBookTotals();

            break;

        case MarkupType.Decodable:

            if (editableElements.length == 0) return;

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

    if (this.activeElement && this.activeElement.textContent != this.undoStack[this.undoStack.length - 1].text) {
        this.undoStack.push({html: this.activeElement.innerHTML, text: this.activeElement.textContent, offset: oldSelectionPosition});
        this.redoStack = []; // ok because only referred to by this variable.
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

    this.bookPageWords = JSON.parse(pageSource);
    this.displayBookTotals();
};

ReaderToolsModel.prototype.displayBookTotals = function () {

    if (this.bookPageWords.length === 0) {
        this.getTextOfWholeBook();
        return;
    }

    var pageStrings = _.values(this.bookPageWords);

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
            for (var k = 0; k < words.length; k++) {
                wordMap[words[k]] = 1;
            }
        }
    }
    return Object.keys(wordMap).length;
};

ReaderToolsModel.prototype.maxWordsPerPageInBook = function(pageStrings) {
    var maxWords = 0;

    for (var i = 0; i < pageStrings.length; i++) {
        var page = pageStrings[i];

        // split into sentences
        var fragments = libsynphony.stringToSentences(page);

        // remove inter-sentence space
        fragments = fragments.filter(function(frag) {
            return frag.isSentence;
        });

        var subMax = 0;
        for (var j = 0; j < fragments.length; j++) {
            subMax += fragments[j].wordCount();
        }

        if (subMax > maxWords) maxWords = subMax;
    }

    return maxWords;
};

ReaderToolsModel.prototype.updateActualCount = function(actual, max, id) {
    $('#' + id).html(actual.toString());
    var acceptable = (actual <= max) || (max === 0);
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
    if (fileContents.substr(0, 12) === '{"LangName":') {
        libsynphony.langDataFromString(fileContents);
        this.getSynphony().loadFromLangData(lang_data);
    }
    else if (fileContents.substr(0, 12) === 'setLangData(') {
        libsynphony.langDataFromString(fileContents);
        this.getSynphony().loadFromLangData(lang_data);
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
        this.doMarkup();
        this.updateWordList();
        processWordListChangedListeners();

        // write out the ReaderToolsWords-xyz.json file
        iframeChannel.simpleAjaxNoCallback('/bloom/readers/saveReaderToolsWords', JSON.stringify(lang_data));

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

    if (!this.currentMarkupType) this.currentMarkupType = state.markupType;
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

    setTimeout(function() { resizeWordList(); }, 100);
    setTimeout(function() { $.divsToColumns('letter'); }, 100);
}

function initializeLeveledRT() {

    // make sure synphony is initialized
    if (!model.getSynphony().source) {
        iframeChannel.simpleAjaxGet('/bloom/readers/getDefaultFont', setDefaultFont);
        iframeChannel.simpleAjaxGet('/bloom/readers/loadReaderToolSettings', initializeSynphony);
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

function makeLetterWordList() {

    // get a copy of the current settings
    var settings = jQuery.extend(true, {}, model.getSynphony().source);

    // remove levels
    if (typeof settings.levels !== 'undefined')
        delete settings.levels;

    // get the words for each stage
    var knownGPCS = [];
    for (var i = 0; i < settings.stages.length; i++) {

        var stageGPCS = settings.stages[i].letters.split(' ');
        knownGPCS = _.union(knownGPCS, stageGPCS);
        var stageWords = model.selectWordsFromSynphony(true, stageGPCS, knownGPCS, true, true);
        settings.stages[i].words = _.toArray(stageWords);
    }

    // get list of all words
    var allGroups = [];
    for (var j = 1; j <= lang_data.VocabularyGroups; j++)
        allGroups.push('group' + j);
    allGroups = libsynphony.chooseVocabGroups(allGroups);

    var allWords = [];
    for (var g = 0; g < allGroups.length; g++) {
        allWords = allWords.concat(allGroups[g]);
    }
    allWords = _.compact(_.pluck(allWords, 'Name'));

    // export the word list
    var ajaxSettings = {type: 'POST', url: '/bloom/readers/makeLetterAndWordList'};
    ajaxSettings['data'] = {
        settings: JSON.stringify(settings),
        allWords: allWords.join('\t')
    };

    $.ajax(ajaxSettings)
}

function loadExternalLink(url) {
    $.get(url, function() {
        // ignore response
        // in this case, we just want to open an external browser with a link, so we don't want to process the response
    });
}

function resizeWordList() {
    
}