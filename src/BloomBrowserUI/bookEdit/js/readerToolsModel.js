/// <reference path="interIframeChannel.ts" />
/// <reference path="getIframeChannel.ts" />
/// <reference path="synphonyApi.ts" />
/// <reference path="libsynphony/bloom_lib.d.ts" />
/// <reference path="libsynphony/synphony.d.ts" />
/// <reference path="libsynphony/jquery.text-markup.d.ts" />
/// <reference path="jquery.div-columns.ts" />
/// <reference path="../../lib/jquery-ui.d.ts" />
/// <reference path="editableDivUtils.ts" />
var iframeChannel = getIframeChannel();

var model;

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

var previousHeight = 0;

var sortIconSelectedClass = "sortIconSelected";
var disabledIconClass = "disabledIcon";
var disabledLimitClass = "disabledLimit";

var DRTState = (function () {
    function DRTState() {
        this.stage = 1;
        this.level = 1;
        this.markupType = MarkupType.Decodable;
    }
    return DRTState;
})();

var ReaderToolsModel = (function () {
    function ReaderToolsModel() {
        this.stageNumber = 1;
        this.levelNumber = 1;
        this.synphony = new SynphonyApi();
        this.sort = SortType.alphabetic;
        this.currentMarkupType = MarkupType.None;
        this.allWords = {};
        this.texts = [];
        this.textCounter = 0;
        this.setupType = '';
        this.fontName = '';
        this.readableFileExtensions = [];
        this.keypressTimer = null;
        /** @type DirectoryWatcher directoryWatcher */
        this.directoryWatcher = null;
        // remember words so we can update the counts real-time
        this.bookPageWords = [];
        // BL-599: Speed up the decodable reader tool
        this.stageGraphemes = [];
        this.wordListChangedListeners = {};
        // some things need to wait until the word list has finished loading
        this.wordListLoaded = false;
        // this happens during testing
        if (iframeChannel)
            this.readableFileExtensions = iframeChannel.readableFileExtensions;
    }
    ReaderToolsModel.prototype.incrementStage = function () {
        this.setStageNumber(this.stageNumber + 1);
    };

    ReaderToolsModel.prototype.decrementStage = function () {
        this.setStageNumber(this.stageNumber - 1);
    };

    ReaderToolsModel.prototype.setStageNumber = function (val) {
        // this may result in a need to resize the word list
        previousHeight = 0;

        var stages = this.synphony.getStages();
        if (val < 1 || val > stages.length) {
            return;
        }
        this.stageNumber = val;

        // BL-599: Speed up the decodable reader tool
        this.stageGraphemes = this.getKnownGraphemes(val);

        this.updateStageLabel();
        this.updateLetterList();
        this.enableStageButtons();
        this.saveState();

        if (!this.wordListLoaded)
            return;

        this.doMarkup();
        this.updateWordList();
    };

    ReaderToolsModel.prototype.updateStageLabel = function () {
        var stages = this.synphony.getStages();
        if (stages.length <= 0) {
            ReaderToolsModel.updateElementContent("stageNumber", "0");
            return;
        }
        ReaderToolsModel.updateElementContent("stageNumber", stages[this.stageNumber - 1].getName());
    };

    ReaderToolsModel.prototype.incrementLevel = function () {
        this.setLevelNumber(this.levelNumber + 1);
    };

    ReaderToolsModel.prototype.decrementLevel = function () {
        this.setLevelNumber(this.levelNumber - 1);
    };

    ReaderToolsModel.prototype.setLevelNumber = function (val) {
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

    ReaderToolsModel.prototype.updateLevelLabel = function () {
        var levels = this.synphony.getLevels();
        if (levels.length <= 0) {
            ReaderToolsModel.updateElementContent("levelNumber", "0");
            return;
        }

        if (levels.length < this.levelNumber) {
            this.setLevelNumber(levels.length);
            return;
        }

        ReaderToolsModel.updateElementContent("levelNumber", levels[this.levelNumber - 1].getName());
    };

    ReaderToolsModel.prototype.sortByLength = function () {
        this.setSort(SortType.byLength);
    };

    ReaderToolsModel.prototype.sortByFrequency = function () {
        this.setSort(SortType.byFrequency);
    };

    ReaderToolsModel.prototype.sortAlphabetically = function () {
        this.setSort(SortType.alphabetic);
    };

    ReaderToolsModel.prototype.setSort = function (sortType) {
        this.sort = sortType;
        this.updateSortStatus();
        this.updateWordList();
        this.saveState();
    };

    ReaderToolsModel.prototype.updateSortStatus = function () {
        ReaderToolsModel.updateSelectedStatus("sortAlphabetic", this.sort === SortType.alphabetic);
        ReaderToolsModel.updateSelectedStatus("sortLength", this.sort === SortType.byLength);
        ReaderToolsModel.updateSelectedStatus("sortFrequency", this.sort === SortType.byFrequency);
    };

    ReaderToolsModel.updateSelectedStatus = function (eltId, isSelected) {
        ReaderToolsModel.setPresenceOfClass(eltId, isSelected, sortIconSelectedClass);
    };

    /**
    * Should be called when the browser has loaded the page, and when the user has changed configuration.
    * It updates various things in the UI to be consistent with the state of things in the model.
    */
    ReaderToolsModel.prototype.updateControlContents = function () {
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

    ReaderToolsModel.prototype.updateNumberOfStages = function () {
        ReaderToolsModel.updateElementContent("numberOfStages", this.synphony.getStages().length.toString());
    };

    ReaderToolsModel.prototype.updateNumberOfLevels = function () {
        ReaderToolsModel.updateElementContent("numberOfLevels", this.synphony.getLevels().length.toString());
    };

    ReaderToolsModel.prototype.enableStageButtons = function () {
        ReaderToolsModel.updateDisabledStatus("decStage", this.stageNumber <= 1);
        ReaderToolsModel.updateDisabledStatus("incStage", this.stageNumber >= this.synphony.getStages().length);
    };

    ReaderToolsModel.updateDisabledStatus = function (eltId, isDisabled) {
        ReaderToolsModel.setPresenceOfClass(eltId, isDisabled, disabledIconClass);
    };

    /**
    * Find the element with the indicated ID, and make sure that it has the className in its class attribute
    * if isWanted is true, and not otherwise.
    * (Tests currently assume it will be added last, but this is not required.)
    * (class names used with this method should not occur as sub-strings within a longer class name)
    */
    ReaderToolsModel.setPresenceOfClass = function (eltId, isWanted, className) {
        var old = ReaderToolsModel.getElementAttribute(eltId, "class");

        // this can happen during testing
        if (!old)
            old = "";

        if (isWanted && old.indexOf(className) < 0) {
            ReaderToolsModel.setElementAttribute(eltId, "class", old + (old.length ? " " : "") + className);
        } else if (!isWanted && old.indexOf(className) >= 0) {
            ReaderToolsModel.setElementAttribute(eltId, "class", old.replace(className, "").replace("  ", " ").trim());
        }
    };

    ReaderToolsModel.prototype.enableLevelButtons = function () {
        ReaderToolsModel.updateDisabledStatus("decLevel", this.levelNumber <= 1);
        ReaderToolsModel.updateDisabledStatus("incLevel", this.levelNumber >= this.synphony.getLevels().length);
    };

    ReaderToolsModel.prototype.updateLevelLimits = function () {
        var level = this.synphony.getLevels()[this.levelNumber - 1];
        if (!level)
            level = new ReaderLevel("");

        ReaderToolsModel.updateLevelLimit("maxWordsPerPage", level.getMaxWordsPerPage());
        ReaderToolsModel.updateLevelLimit("maxWordsPerPageBook", level.getMaxWordsPerPage());
        ReaderToolsModel.updateLevelLimit("maxWordsPerSentence", level.getMaxWordsPerSentence());
        ReaderToolsModel.updateLevelLimit("maxWordsPerBook", level.getMaxWordsPerBook());
        ReaderToolsModel.updateLevelLimit("maxUniqueWordsPerBook", level.getMaxUniqueWordsPerBook());

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

    ReaderToolsModel.updateLevelLimit = function (id, limit) {
        if (limit !== 0) {
            ReaderToolsModel.updateElementContent(id, limit.toString());
        }
        ReaderToolsModel.updateDisabledLimit(id, limit === 0);
    };

    ReaderToolsModel.updateDisabledLimit = function (eltId, isDisabled) {
        ReaderToolsModel.setPresenceOfClass(eltId, isDisabled, disabledLimitClass);
    };

    /**
    * Displays the list of words for the current Stage.
    */
    ReaderToolsModel.prototype.updateWordList = function () {
        if (!this.wordListLoaded)
            return;

        var wordList = document.getElementById('wordList');
        if (wordList)
            document.getElementById('wordList').innerHTML = '';

        var stages = this.synphony.getStages();
        if (stages.length === 0)
            return;

        var words = this.getStageWordsAndSightWords(this.stageNumber);

        switch (this.sort) {
            case SortType.alphabetic:
                words.sort(function (a, b) {
                    return a.Name.localeCompare(b.Name);
                });
                break;
            case SortType.byLength:
                words.sort(function (a, b) {
                    if (a.Name.length === b.Name.length) {
                        return a.Name.localeCompare(b.Name);
                    }
                    return a.Name.length - b.Name.length;
                });
                break;
            case SortType.byFrequency:
                words.sort(function (a, b) {
                    var aFreq = a.Count;
                    var bFreq = b.Count;
                    if (aFreq === bFreq) {
                        return a.Name.localeCompare(b.Name);
                    }
                    return bFreq - aFreq;
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
            if (w.Name.length > longestWord.length)
                longestWord = w.Name;
        }

        ReaderToolsModel.updateElementContent("wordList", result);

        $.divsToColumnsBasedOnLongestWord('word', longestWord);
    };

    /**
    * Displays the list of letters for the current Stage.
    */
    ReaderToolsModel.prototype.updateLetterList = function () {
        var stages = this.synphony.getStages();
        if (stages.length === 0)
            return;

        // Letters up through current stage
        var letters = this.stageGraphemes;

        // All the letters in the order they were entered on the Letters tab in the set up dialog
        var allLetters = this.synphony.source.letters.split(' ');

        // Sort our letters based on the order they were entered
        letters.sort(function (a, b) {
            return allLetters.indexOf(a) - allLetters.indexOf(b);
        });

        var result = "";
        for (var i = 0; i < letters.length; i++) {
            var letter = letters[i];
            result += '<div class="letter">' + letter + '</div>';
        }

        ReaderToolsModel.updateElementContent("letterList", result);

        $.divsToColumns('letter');
    };

    /**
    * Get the sight words for the current stage and all previous stages.
    * Note: The list returned may contain sight words from previous stages that are now decodable.
    * @param stageNumber
    * @returns An array of strings
    */
    ReaderToolsModel.prototype.getSightWords = function (stageNumber) {
        var stages = this.synphony.getStages(stageNumber);
        var sightWords = [];
        if (stages.length > 0) {
            for (var i = 0; i < stages.length; i++) {
                if (stages[i].sightWords)
                    sightWords = _.union(sightWords, stages[i].sightWords.split(' '));
            }
        }

        return sightWords;
    };

    /**
    * Get the sight words for the current stage and all previous stages as an array of DataWord objects
    * Note: The list returned may contain sight words from previous stages that are now decodable.
    * @param stageNumber
    * @returns An array of DataWord objects
    */
    ReaderToolsModel.prototype.getSightWordsAsObjects = function (stageNumber) {
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
    * @param stageNumber
    * @returns An array of strings
    */
    ReaderToolsModel.prototype.getKnownGraphemes = function (stageNumber) {
        var stages = this.synphony.getStages(stageNumber);

        // compact to remove empty items if no graphemes are selected
        return _.compact(_.pluck(stages, 'letters').join(' ').split(' '));
    };

    /**
    *
    * @returns An array of DataWord objects
    */
    ReaderToolsModel.prototype.getStageWords = function () {
        if ((!this.stageGraphemes) || (this.stageGraphemes.length === 0))
            return [];
        return ReaderToolsModel.selectWordsFromSynphony(false, this.stageGraphemes, this.stageGraphemes, true, true);
    };

    ReaderToolsModel.prototype.getStageWordsAndSightWords = function (stageNumber) {
        if (!this.wordListLoaded)
            return;

        // first get the sight words
        var sightWords = this.getSightWordsAsObjects(stageNumber);
        var stageWords = this.getStageWords();

        return _.uniq(stageWords.concat(sightWords), false, function (w) {
            return w.Name;
        });
    };

    /**
    * Change the markup type when the user selects a different Tool.
    * @param {int} markupType
    */
    ReaderToolsModel.prototype.setMarkupType = function (markupType) {
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
        if (newMarkupType === null)
            return;

        if (newMarkupType !== this.currentMarkupType) {
            var page = parent.window.document.getElementById('page');
            if (page)
                $('.bloom-editable', page.contentWindow.document).removeSynphonyMarkup();
            this.currentMarkupType = newMarkupType;
            this.doMarkup();
        }

        this.saveState();
    };

    ReaderToolsModel.getElementsToCheck = function () {
        var page = parent.window.document.getElementById('page');

        // this happens during unit testing
        if (!page) {
            return $('.bloom-page').not('.bloom-frontMatter, .bloom-backMatter').find('.bloom-content1.bloom-editable');
        }

        // if this is a cover page, return an empty set
        var cover = $('body', page.contentWindow.document).find('div.cover');
        if (cover['length'] > 0)
            return $();

        // not a cover page, return elements to check
        return $('.bloom-page', page.contentWindow.document).not('.bloom-frontMatter, .bloom-backMatter').find('.bloom-content1.bloom-editable');
    };

    ReaderToolsModel.prototype.doKeypressMarkup = function () {
        // BL-599: "Unresponsive script" while typing in text.
        // The function setTimeout() returns an integer, not a timer object, and therefore it does not have a member
        // function called "clearTimeout." Because of this, the jQuery method $.isFunction(this.keypressTimer.clearTimeout)
        // will always return false (since "this.keypressTimer.clearTimeout" is undefined) and the result is a new 500
        // millisecond timer being created every time the doKeypress method is called, but none of the pre-existing timers
        // being cleared. The correct way to clear a timeout is to call clearTimeout(), passing it the integer returned by
        // the function setTimeout().
        //if (this.keypressTimer && $.isFunction(this.keypressTimer.clearTimeout)) {
        //  this.keypressTimer.clearTimeout();
        //}
        if (model.keypressTimer)
            clearTimeout(model.keypressTimer);

        model.keypressTimer = setTimeout(function () {
            // This happens 500ms after the user stops typing.
            var page = parent.window.document.getElementById('page');
            if (!page)
                return;

            var selection = page.contentWindow.getSelection();
            var current = selection.anchorNode;
            var active = $(selection.anchorNode).closest('div').get(0);
            if (!active || selection.rangeCount > 1 || (selection.rangeCount == 1 && !selection.getRangeAt(0).collapsed)) {
                return;
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
            if (current.nodeType !== 3) {
                divBrCount = 0;

                for (var k = myRange.endOffset - 1; k >= 0; k--) {
                    if (current.childNodes[k].localName === 'br')
                        divBrCount++;
                    else if (current.childNodes[k].textContent.length > 0)
                        break;
                }
            }

            var atStart = myRange.endOffset === 0;

            model.doMarkup();

            // Now we try to restore the selection at the specified position.
            EditableDivUtils.makeSelectionIn(active, offset, divBrCount, atStart);

            // clear this value to prevent unnecessary calls to clearTimeout() for timeouts that have already expired.
            model.keypressTimer = null;
        }, 500);
    };

    ReaderToolsModel.prototype.noteFocus = function (element) {
        this.activeElement = element;
        this.undoStack = [];
        this.redoStack = [];
        this.undoStack.push({
            html: element.innerHTML,
            text: element.textContent,
            offset: EditableDivUtils.getElementSelectionIndex(this.activeElement)
        });
    };

    ReaderToolsModel.prototype.shouldHandleUndo = function () {
        return this.currentMarkupType !== MarkupType.None;
    };

    ReaderToolsModel.prototype.undo = function () {
        if (!this.activeElement)
            return;
        if (this.activeElement.textContent == this.undoStack[this.undoStack.length - 1].text && this.undoStack.length > 1) {
            this.redoStack.push(this.undoStack.pop());
        }
        this.activeElement.innerHTML = this.undoStack[this.undoStack.length - 1].html;
        var restoreOffset = this.undoStack[this.undoStack.length - 1].offset;
        if (restoreOffset < 0)
            return;
        EditableDivUtils.makeSelectionIn(this.activeElement, restoreOffset, null, true);
    };

    ReaderToolsModel.prototype.canUndo = function () {
        if (!this.activeElement)
            return 'no';
        if (this.undoStack && (this.undoStack.length > 1 || this.activeElement.textContent !== this.undoStack[0].text)) {
            return 'yes';
        }
        return 'no';
    };

    ReaderToolsModel.prototype.redo = function () {
        if (!this.activeElement)
            return;
        if (this.redoStack.length > 0) {
            this.undoStack.push(this.redoStack.pop());
        }
        this.activeElement.innerHTML = this.undoStack[this.undoStack.length - 1].html;
        var restoreOffset = this.undoStack[this.undoStack.length - 1].offset;
        if (restoreOffset < 0)
            return;
        EditableDivUtils.makeSelectionIn(this.activeElement, restoreOffset, null, true);
    };

    /**
    * Displays the correct markup for the current page.
    */
    ReaderToolsModel.prototype.doMarkup = function () {
        if (!this.wordListLoaded)
            return;
        if (this.currentMarkupType === MarkupType.None)
            return;

        var oldSelectionPosition = -1;
        if (this.activeElement)
            oldSelectionPosition = EditableDivUtils.getElementSelectionIndex(this.activeElement);

        var editableElements = ReaderToolsModel.getElementsToCheck();

        // qtips can be orphaned if the element they belong to is deleted
        // (and so the mouse can't move off their owning element, and they never go away).
        if (editableElements.length > 0)
            $(editableElements[0]).closest('body').children('.qtip').remove();

        switch (this.currentMarkupType) {
            case MarkupType.Leveled:
                if (editableElements.length > 0) {
                    var options = {
                        maxWordsPerSentence: this.maxWordsPerSentenceOnThisPage(),
                        maxWordsPerPage: this.maxWordsPerPage()
                    };
                    editableElements.checkLeveledReader(options);

                    // update current page words
                    var pageDiv = $('body', iframeChannel.getPageWindow().document).find('div.bloom-page');
                    if (pageDiv.length) {
                        if (pageDiv[0].id)
                            this.bookPageWords[pageDiv[0].id] = editableElements['allWords'];
                    }
                }

                this.updateMaxWordsPerSentenceOnPage();
                this.updateTotalWordsOnPage();
                this.displayBookTotals();

                break;

            case MarkupType.Decodable:
                if (editableElements.length == 0)
                    return;

                // get current stage and all previous stages
                var stages = this.synphony.getStages(this.stageNumber);
                if (stages.length === 0)
                    return;

                // get word lists
                var cumulativeWords = this.getStageWords();
                var sightWords = this.getSightWords(this.stageNumber);

                editableElements.checkDecodableReader({
                    focusWords: cumulativeWords,
                    previousWords: cumulativeWords,
                    sightWords: sightWords,
                    knownGraphemes: this.stageGraphemes
                });

                break;

            default:
        }

        if (this.activeElement && this.activeElement.textContent != this.undoStack[this.undoStack.length - 1].text) {
            this.undoStack.push({
                html: this.activeElement.innerHTML,
                text: this.activeElement.textContent,
                offset: oldSelectionPosition
            });
            this.redoStack = []; // ok because only referred to by this variable.
        }

        // the contentWindow is not available during unit testing
        var page = parent.window.document.getElementById('page');
        if (page)
            page.contentWindow.postMessage('Qtips', "*");
    };

    ReaderToolsModel.prototype.maxWordsPerSentenceOnThisPage = function () {
        var levels = this.synphony.getLevels();
        if (levels.length <= 0) {
            return 9999;
        }
        return levels[this.levelNumber - 1].getMaxWordsPerSentence();
    };

    ReaderToolsModel.prototype.maxWordsPerBook = function () {
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

    ReaderToolsModel.prototype.maxWordsPerPage = function () {
        var levels = this.synphony.getLevels();
        if (levels.length <= 0) {
            return 9999;
        }
        return levels[this.levelNumber - 1].getMaxWordsPerPage();
    };

    ReaderToolsModel.getTextOfWholeBook = function () {
        iframeChannel.simpleAjaxGet('/bloom/readers/getTextOfPages', ReaderToolsModel.updateWholeBookCounts);
    };

    ReaderToolsModel.updateWholeBookCounts = function (pageSource) {
        model.bookPageWords = JSON.parse(pageSource);
        model.displayBookTotals();
    };

    ReaderToolsModel.prototype.displayBookTotals = function () {
        if (this.bookPageWords.length === 0) {
            ReaderToolsModel.getTextOfWholeBook();
            return;
        }

        var pageStrings = _.values(this.bookPageWords);

        ReaderToolsModel.updateActualCount(ReaderToolsModel.countWordsInBook(pageStrings), this.maxWordsPerBook(), 'actualWordCount');
        ReaderToolsModel.updateActualCount(ReaderToolsModel.maxWordsPerPageInBook(pageStrings), this.maxWordsPerPage(), 'actualWordsPerPageBook');
        ReaderToolsModel.updateActualCount(ReaderToolsModel.uniqueWordsInBook(pageStrings), this.maxUniqueWordsPerBook(), 'actualUniqueWords');
    };

    ReaderToolsModel.countWordsInBook = function (pageStrings) {
        var total = 0;
        for (var i = 0; i < pageStrings.length; i++) {
            var page = pageStrings[i];
            var fragments = libsynphony.stringToSentences(page);

            // remove inter-sentence space
            fragments = fragments.filter(function (frag) {
                return frag.isSentence;
            });

            for (var j = 0; j < fragments.length; j++) {
                total += fragments[j].wordCount();
            }
        }
        return total;
    };

    ReaderToolsModel.uniqueWordsInBook = function (pageStrings) {
        var wordMap = {};
        for (var i = 0; i < pageStrings.length; i++) {
            var page = pageStrings[i];
            var fragments = libsynphony.stringToSentences(page);

            // remove inter-sentence space
            fragments = fragments.filter(function (frag) {
                return frag.isSentence;
            });

            for (var j = 0; j < fragments.length; j++) {
                var words = fragments[j].words;
                for (var k = 0; k < words.length; k++) {
                    wordMap[words[k]] = 1;
                }
            }
        }
        return Object.keys(wordMap).length;
    };

    ReaderToolsModel.maxWordsPerPageInBook = function (pageStrings) {
        var maxWords = 0;

        for (var i = 0; i < pageStrings.length; i++) {
            var page = pageStrings[i];

            // split into sentences
            var fragments = libsynphony.stringToSentences(page);

            // remove inter-sentence space
            fragments = fragments.filter(function (frag) {
                return frag.isSentence;
            });

            var subMax = 0;
            for (var j = 0; j < fragments.length; j++) {
                subMax += fragments[j].wordCount();
            }

            if (subMax > maxWords)
                maxWords = subMax;
        }

        return maxWords;
    };

    ReaderToolsModel.updateActualCount = function (actual, max, id) {
        $('#' + id).html(actual.toString());
        var acceptable = (actual <= max) || (max === 0);

        // The two styles here must match ones defined in ReaderTools.htm or its stylesheet.
        // It's important NOT to use two names where one is a substring of the other (e.g., unacceptable
        // instead of tooLarge). That will mess things up going from the longer to the shorter.
        ReaderToolsModel.setPresenceOfClass(id, acceptable, "acceptable");
        ReaderToolsModel.setPresenceOfClass(id, !acceptable, "tooLarge");
    };

    ReaderToolsModel.prototype.updateMaxWordsPerSentenceOnPage = function () {
        ReaderToolsModel.updateActualCount(ReaderToolsModel.getElementsToCheck().getMaxSentenceLength(), this.maxWordsPerSentenceOnThisPage(), 'actualWordsPerSentence');
    };

    ReaderToolsModel.prototype.updateTotalWordsOnPage = function () {
        ReaderToolsModel.updateActualCount(ReaderToolsModel.getElementsToCheck().getTotalWordCount(), this.maxWordsPerPage(), 'actualWordsPerPage');
    };

    /** Should be called early on, before other init. */
    ReaderToolsModel.prototype.setSynphony = function (val) {
        this.synphony = val;
    };

    ReaderToolsModel.prototype.getSynphony = function () {
        return this.synphony;
    };

    /**
    * This group of functions uses jquery (if loaded) to update the real model.
    * Unit testing should spy or otherwise replace these functions, since $ will not be usefully defined.
    */
    ReaderToolsModel.updateElementContent = function (id, val) {
        $("#" + id).html(val);
    };

    ReaderToolsModel.getElementAttribute = function (id, attrName) {
        return $("#" + id).attr(attrName);
    };

    ReaderToolsModel.setElementAttribute = function (id, attrName, val) {
        $("#" + id).attr(attrName, val);
    };

    /**
    * Add words from a file to the list of all words. Does not produce duplicates.
    * @param fileContents
    */
    ReaderToolsModel.prototype.addWordsFromFile = function (fileContents) {
        // is this a Synphony data file?
        if (fileContents.substr(0, 12) === '{"LangName":') {
            libsynphony.langDataFromString(fileContents);
            this.getSynphony().loadFromLangData(lang_data);
        } else if (fileContents.substr(0, 12) === 'setLangData(') {
            libsynphony.langDataFromString(fileContents);
            this.getSynphony().loadFromLangData(lang_data);
        } else {
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
    ReaderToolsModel.prototype.getNextSampleFile = function () {
        // if there are no more files, process the word lists now
        if (this.textCounter >= this.texts.length) {
            this.addWordsToSynphony();

            // The word list has been received. Now we are using setTimeout() to move the remainder of the word
            // list processing to another thread so the UI doesn't appear frozen as long. This is essentially
            // the JavaScript version of Application.DoEvents().
            setTimeout(function () {
                model.wordListLoaded = true;
                model.doMarkup();
                model.updateWordList();
                model.processWordListChangedListeners();

                // write out the ReaderToolsWords-xyz.json file
                iframeChannel.simpleAjaxNoCallback('/bloom/readers/saveReaderToolsWords', JSON.stringify(lang_data));
            }, 200);

            return;
        }

        // only get the contents of the file types we can read
        var fileName;
        do {
            var ext = this.texts[this.textCounter].split('.').pop();
            if (this.readableFileExtensions.indexOf(ext) > -1)
                fileName = this.texts[this.textCounter];
            this.textCounter++;
        } while(!fileName && (this.textCounter < this.texts.length));

        if (fileName)
            iframeChannel.simpleAjaxGet('/bloom/readers/getSampleFileContents', ReaderToolsModel.setSampleFileContents, fileName);
        else
            this.getNextSampleFile();
    };

    /**
    * Called in response to a request for the contents of a sample text file
    * @param fileContents
    */
    ReaderToolsModel.setSampleFileContents = function (fileContents) {
        model.addWordsFromFile(fileContents);
        model.getNextSampleFile();
    };

    /**
    * Notify anyone who wants to know that the word list changed
    */
    ReaderToolsModel.prototype.processWordListChangedListeners = function () {
        var handlers = Object.keys(this.wordListChangedListeners);
        for (var j = 0; j < handlers.length; j++)
            this.wordListChangedListeners[handlers[j]]();
    };

    /**
    * Take the list of words collected from the sample files, add it to SynPhony, and update the Stages.
    */
    ReaderToolsModel.prototype.addWordsToSynphony = function () {
        // add words to the word list
        var syn = this.getSynphony();
        syn.addWords(this.allWords);
        libsynphony.processVocabularyGroups();
    };

    /**
    * Gets words from SynPhony that match the input criteria
    * @param justWordName Return just the word names, not DataWord objects
    * @param desiredGPCs An array of strings
    * @param knownGPCs An array of strings
    * @param restrictToKnownGPCs
    * @param [allowUpperCase]
    * @param [syllableLengths] An array of integers, uses 1-24 if empty
    * @param [selectedGroups] An array of strings, uses all groups if empty
    * @param [partsOfSpeech] An array of strings, uses all parts of speech if empty
    * @returns An array of strings or DataWord objects
    */
    ReaderToolsModel.selectWordsFromSynphony = function (justWordName, desiredGPCs, knownGPCs, restrictToKnownGPCs, allowUpperCase, syllableLengths, selectedGroups, partsOfSpeech) {
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
            return libsynphony.selectGPCWordsFromCache(desiredGPCs, knownGPCs, restrictToKnownGPCs, allowUpperCase, syllableLengths, selectedGroups, partsOfSpeech);
    };

    ReaderToolsModel.prototype.saveState = function () {
        // this is needed for unit testing
        var accordion = $('#accordion');
        if (typeof accordion.accordion !== 'function')
            return;

        // this is also needed for unit testing
        var active = accordion.accordion('option', 'active');
        if (isNaN(active))
            return;

        var state = new DRTState();
        state.stage = this.stageNumber;
        state.level = this.levelNumber;
        state.markupType = this.currentMarkupType;
        fireCSharpAccordionEvent('saveAccordionSettingsEvent', "state\tdecodableReader\t" + "stage:" + this.stageNumber + ";sort:" + this.sort);
        fireCSharpAccordionEvent('saveAccordionSettingsEvent', "state\tleveledReader\t" + this.levelNumber);
        libsynphony.dbSet('drt_state', state);
    };

    ReaderToolsModel.prototype.restoreState = function () {
        // this is needed for unit testing
        var accordion = $('#accordion');
        if (typeof accordion.accordion !== 'function')
            return;

        var state = libsynphony.dbGet('drt_state');
        if (!state)
            state = new DRTState();

        if (!this.currentMarkupType)
            this.currentMarkupType = state.markupType;
        this.setStageNumber(state.stage);
        this.setLevelNumber(state.level);
    };
    return ReaderToolsModel;
})();
//# sourceMappingURL=readerToolsModel.js.map
