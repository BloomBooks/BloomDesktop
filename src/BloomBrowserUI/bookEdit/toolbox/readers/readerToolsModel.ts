/// <reference path="./ReadersSynphonyWrapper.ts" />
/// <reference path="./libSynphony/jquery.text-markup.d.ts" />
/// <reference path="./jquery.div-columns.ts" />
/// <reference path="../../../typings/jqueryui/jqueryui.d.ts" />
/// <reference path="../../js/editableDivUtils.ts" />
/// <reference path="./directoryWatcher.ts" />
/// <reference path="../../../lib/localizationManager/localizationManager.ts" />
/// <reference path="readerTools.ts" />
/// <reference path="../toolbox.ts" />
/// <reference path="./libSynphony/synphony_lib.d.ts" />
import {DirectoryWatcher} from "./directoryWatcher";
import {resizeWordList} from "./readerTools";
import theOneLocalizationManager from '../../../lib/localizationManager/localizationManager';
import {ToolBox} from "../toolbox";
import "./libSynphony/jquery.text-markup.ts";
import './jquery.div-columns.ts';
import {ReaderStage, ReaderLevel, ReaderSettings} from './ReaderSettings';
import * as _ from 'underscore';
import {theOneLanguageDataInstance, theOneLibSynphony}  from './libSynphony/synphony_lib';
import ReadersSynphonyWrapper from './ReadersSynphonyWrapper';
import {DataWord,TextFragment} from './libSynphony/bloomSynphonyExtensions';
import axios = require('axios');
var SortType = {
  alphabetic: "alphabetic",
  byLength: "byLength",
  byFrequency: "byFrequency"
};

export var MarkupType = {
  None: 0,
  Leveled: 1,
  Decodable: 2
};

var sortIconSelectedClass = "sortIconSelected"; // The class we apply to the selected sort icon
var disabledIconClass = "disabledIcon"; // The class we apply to icons that are disabled.
var disabledLimitClass = "disabledLimit"; // The class we apply to max values that are disabled (0).

export class DRTState {
  stage: number = 1;
  level: number = 1;
  markupType: number = MarkupType.Decodable;
}

export interface ReaderToolsWindow extends Window {
  model: ReaderToolsModel;
  canUndo(): string;
  shouldHandleUndo(): string;
}

export class ReaderToolsModel {

  static model: ReaderToolsModel = new ReaderToolsModel(); //reviewslog: is that all it takes to make a singleton?
  static previousHeight : number = 0;
  static previousWidth : number = 0;
  
  stageNumber: number = 1;
  levelNumber: number = 1;
  synphony: ReadersSynphonyWrapper = null; // to ensure detection of async issues, don't init until we load its settings
  sort: string = SortType.alphabetic;
  currentMarkupType: number = MarkupType.None;
  allWords = {};
  texts = [];
  setupType: string = '';
  fontName: string = '';
  readableFileExtensions: string[] = [];
  directoryWatcher: DirectoryWatcher = null;
  maxAllowedWords: number = 10000;

  // remember words so we can update the counts real-time
  pageIDToText = [];

  // BL-599: Speed up the decodable reader tool
  stageGraphemes = [];

  activeElement: HTMLElement;
  undoStack: any[];
  redoStack: any[];

  wordListChangedListeners: any = {};

  // some things need to wait until the word list has finished loading
  wordListLoaded: boolean = false;
  ckEditorLoaded: boolean = false;
  allowedWordFilesRemaining: number = 0;
  public static getReadableFileExtensions() {
      return ['txt', 'js', 'json'];
  }

  readyToDoMarkup(): boolean { return this.wordListLoaded && this.ckEditorLoaded; }
  setCkEditorLoaded() : void { this.ckEditorLoaded = true; }

  incrementStage(): void {
    this.setStageNumber(this.stageNumber + 1);
  }

  decrementStage(): void {
    this.setStageNumber(this.stageNumber - 1);
  }

  setStageNumber(val: number): void {

    // this may result in a need to resize the word list
    ReaderToolsModel.previousHeight = 0;

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

    if (!this.readyToDoMarkup()) return;

    this.doMarkup();
    this.updateWordList();
  }

  updateStageLabel(): void {
    var stages = this.synphony.getStages();
    if (stages.length <= 0) {
      ReaderToolsModel.updateElementContent("stageNumber", "0");
      return;
    }
    if (this.stageNumber > stages.length) {
       this.stageNumber = stages.length;
    }
    ReaderToolsModel.updateElementContent("stageNumber", stages[this.stageNumber - 1].getName());
  }

  incrementLevel(): void {
    this.setLevelNumber(this.levelNumber + 1);
  }

  decrementLevel(): void {
    this.setLevelNumber(this.levelNumber - 1);
  }

  setLevelNumber(val: number): void {

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
  }

  updateLevelLabel(): void {
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
  }

  sortByLength(): void {
    this.setSort(SortType.byLength);
  }

  sortByFrequency(): void {
    this.setSort(SortType.byFrequency);
  }

  sortAlphabetically(): void {
    this.setSort(SortType.alphabetic);
  }

  setSort(sortType: string): void {
    this.sort = sortType;
    this.updateSortStatus();
    this.updateWordList();
    this.saveState();
  }

  updateSortStatus(): void {
    ReaderToolsModel.updateSelectedStatus("sortAlphabetic", this.sort === SortType.alphabetic);
    ReaderToolsModel.updateSelectedStatus("sortLength", this.sort === SortType.byLength);
    ReaderToolsModel.updateSelectedStatus("sortFrequency", this.sort === SortType.byFrequency);
  }

  static updateSelectedStatus(eltId: string, isSelected: boolean): void {
    ReaderToolsModel.setPresenceOfClass(eltId, isSelected, sortIconSelectedClass);
  }

  /**
   * Should be called when the browser has loaded the page, and when the user has changed configuration.
   * It updates various things in the UI to be consistent with the state of things in the model.
   */
  updateControlContents(): void {
    this.updateLetterList();
    this.updateNumberOfStages();
    this.updateNumberOfLevels();
    this.updateStageLabel();
    this.enableStageButtons();
    this.enableLevelButtons();
    this.updateLevelLimits();
    this.updateLevelLabel();
    this.updateWordList();
  }

  updateNumberOfStages(): void {
    ReaderToolsModel.updateElementContent("numberOfStages", this.synphony.getStages().length.toString());
  }

  updateNumberOfLevels(): void {
    ReaderToolsModel.updateElementContent("numberOfLevels", this.synphony.getLevels().length.toString());
  }

  enableStageButtons(): void {
    ReaderToolsModel.updateDisabledStatus("decStage", this.stageNumber <= 1);
    ReaderToolsModel.updateDisabledStatus("incStage", this.stageNumber >= this.synphony.getStages().length);
  }

  static updateDisabledStatus(eltId: string, isDisabled: boolean): void {
    ReaderToolsModel.setPresenceOfClass(eltId, isDisabled, disabledIconClass);
  }

  /**
   * Find the element with the indicated ID, and make sure that it has the className in its class attribute
   * if isWanted is true, and not otherwise.
   * (Tests currently assume it will be added last, but this is not required.)
   * (class names used with this method should not occur as sub-strings within a longer class name)
   */
  static setPresenceOfClass(eltId: string, isWanted: boolean, className: string): void {
    var old = ReaderToolsModel.getElementAttribute(eltId, "class");

    // this can happen during testing
    if (!old) old = "";

    if (isWanted && old.indexOf(className) < 0) {
      ReaderToolsModel.setElementAttribute(eltId, "class", old + (old.length ? " " : "") + className);
    }
    else if (!isWanted && old.indexOf(className) >= 0) {
      ReaderToolsModel.setElementAttribute(eltId, "class", old.replace(className, "").replace("  ", " ").trim());
    }
  }

  enableLevelButtons(): void {
    ReaderToolsModel.updateDisabledStatus("decLevel", this.levelNumber <= 1);
    ReaderToolsModel.updateDisabledStatus("incLevel", this.levelNumber >= this.synphony.getLevels().length);
  }

  updateLevelLimits(): void {
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
  }

  static updateLevelLimit(id: string, limit: number): void {
    if (limit !== 0) {
      ReaderToolsModel.updateElementContent(id, limit.toString());
    }
    ReaderToolsModel.updateDisabledLimit(id, limit === 0);
  }

  static updateDisabledLimit(eltId: string, isDisabled: boolean): void {
    ReaderToolsModel.setPresenceOfClass(eltId, isDisabled, disabledLimitClass);
  }

  /**
   * Displays the list of words for the current Stage.
   */
  updateWordList(): void {

    // show the correct headings
    //reviewSLog
    var useAllowedWords = (ReaderToolsModel.model.synphony.source) ? ReaderToolsModel.model.synphony.source.useAllowedWords === 1 : false;

    // this happens during unit testing
    if (document.getElementById('make-letter-word-list-div')) {
      document.getElementById('make-letter-word-list-div').style.display = useAllowedWords ? 'none' : '';
      document.getElementById('letters-in-this-stage').style.display = useAllowedWords ? 'none' : '';
      document.getElementById('letterList').parentElement.style.display = useAllowedWords ? 'none' : '';
      document.getElementById('sample-words-this-stage').style.display = useAllowedWords ? 'none' : '';
      document.getElementById('sortFrequency').style.display = useAllowedWords ? 'none' : '';
      document.getElementById('allowed-words-this-stage').style.display = useAllowedWords ? '' : 'none';
      document.getElementById('allowed-word-list-truncated').style.display = useAllowedWords ? '' : 'none';
    }

    if (!this.readyToDoMarkup()) return;

    var wordList = document.getElementById('wordList');
    if (wordList) document.getElementById('wordList').innerHTML = '';

    var stages = this.synphony.getStages();
    if (stages.length === 0) return;

    var words: DataWord[];
    if (useAllowedWords)
      words = ReaderToolsModel.getAllowedWordsAsObjects(this.stageNumber);
    else
      words = this.getStageWordsAndSightWords(this.stageNumber);

    resizeWordList(false);

    // All cases use localeCompare for alphabetic sort. This is not ideal; it will use whatever
    // locale the browser thinks is current. When we implement ldml-dependent sorting we can improve this.
    switch (this.sort) {
      case SortType.alphabetic:
        words.sort(function (a: DataWord, b: DataWord) {
          return a.Name.localeCompare(b.Name);
        });
        break;
      case SortType.byLength:
        words.sort(function (a: DataWord, b: DataWord) {
          if (a.Name.length === b.Name.length) {
            return a.Name.localeCompare(b.Name);
          }
          return a.Name.length - b.Name.length;
        });
        break;
      case SortType.byFrequency:
        words.sort(function (a: DataWord, b: DataWord) {
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
      var w: DataWord = words[i];
      result += '<div class="word' + (w.isSightWord ? ' sight-word' : '') + '">' + w.Name + '</div>';
      if (w.Name.length > longestWord.length) longestWord = w.Name;
    }

    var div = $('div.wordList');
    div.css('font-family', this.fontName);

    ReaderToolsModel.updateElementContent("wordList", result);

    $.divsToColumnsBasedOnLongestWord('word', longestWord);
  }

  /**
   * Displays the list of letters for the current Stage.
   */
  updateLetterList(): void {
    var stages = this.synphony.getStages();
    if (stages.length === 0) {
      // In case the user deletes all stages, and something had been displayed before.
      ReaderToolsModel.updateElementContent("letterList", "");
      return;
    }

    if (this.stageNumber > 0) {
      this.stageGraphemes = this.getKnownGraphemes(this.stageNumber); //BL-838
    }

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
    var div = $('div.letterList');
    div.css('font-family', this.fontName);

    ReaderToolsModel.updateElementContent("letterList", result);

    $.divsToColumns('letter');
  }

  /**
   * Get the sight words for the current stage and all previous stages.
   * Note: The list returned may contain sight words from previous stages that are now decodable.
   * @param stageNumber
   * @returns An array of strings
   */
  getSightWords(stageNumber?: number): string[] {

    var stages: ReaderStage[] = this.synphony.getStages(stageNumber);
    var sightWords: string[] = [];
    if (stages.length > 0) {

      for (var i = 0; i < stages.length; i++) {
        if (stages[i].sightWords) sightWords = _.union(sightWords, stages[i].sightWords.split(' '));
      }
    }

    return sightWords;
  }

  /**
   * Get the sight words for the current stage and all previous stages as an array of DataWord objects
   * Note: The list returned may contain sight words from previous stages that are now decodable.
   * @param stageNumber
   * @returns An array of DataWord objects
   */
  getSightWordsAsObjects(stageNumber: number): DataWord[] {

    var words: string[] = this.getSightWords(stageNumber);
    var returnVal: DataWord[] = [];

    for (var i = 0; i < words.length; i++) {
      var dw = new DataWord(words[i]);
      dw.isSightWord = true;
      returnVal.push(dw);
    }

    return returnVal;
  }

  /**
   * Get the graphemes for the current stage and all previous stages
   * @param stageNumber
   * @returns An array of strings
   */
  getKnownGraphemes(stageNumber: number): string[] {

    var stages = this.synphony.getStages(stageNumber);

    // compact to remove empty items if no graphemes are selected
    return _.compact(_.pluck(stages, 'letters').join(' ').split(' '));
  }

  /**
   *
   * @returns An array of DataWord objects
   */
  getStageWords(): DataWord[] {

    if ((!this.stageGraphemes) || (this.stageGraphemes.length === 0)) return [];
    return ReaderToolsModel.selectWordsFromSynphony(false, this.stageGraphemes, this.stageGraphemes, true, true);
  }

  getStageWordsAndSightWords(stageNumber: number): DataWord[] {

    if (!this.readyToDoMarkup()) return;

    // first get the sight words
    var sightWords = this.getSightWordsAsObjects(stageNumber);
    var stageWords = this.getStageWords();

    return _.uniq(stageWords.concat(sightWords), false, function (w: DataWord) {
      return w.Name;
    });
  }

  /**
   * Change the markup type when the user selects a different Tool.
   * @param {int} markupType
   * returns true if doMarkup called
   */
  setMarkupType(markupType: number): boolean {

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
    if (newMarkupType === null) return false;
    var didMarkup = false;

    if (newMarkupType !== this.currentMarkupType) {
      var page: HTMLIFrameElement = <HTMLIFrameElement>parent.window.document.getElementById('page');
      if (page)
        $('.bloom-editable', page.contentWindow.document).removeSynphonyMarkup();
      this.currentMarkupType = newMarkupType;
      this.doMarkup();
      didMarkup = true;
    }

    this.saveState();
    return didMarkup;
  }

  static getElementsToCheck(): JQuery {

    var page: HTMLIFrameElement = <HTMLIFrameElement>parent.window.document.getElementById('page');

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
  }

  noteFocus(element: HTMLElement): void {
    this.activeElement = element;
    this.undoStack = [];
    this.redoStack = [];
    this.undoStack.push({
      html: element.innerHTML,
      text: element.textContent,
      offset: EditableDivUtils.getElementSelectionIndex(this.activeElement)
    });
  }

  shouldHandleUndo(): boolean {
    return this.currentMarkupType !== MarkupType.None;
  }

  undo(): void {
    if (!this.activeElement) return;
    if (this.activeElement.textContent == this.undoStack[this.undoStack.length - 1].text && this.undoStack.length > 1) {
      this.redoStack.push(this.undoStack.pop());
    }
    this.activeElement.innerHTML = this.undoStack[this.undoStack.length - 1].html;
    var restoreOffset = this.undoStack[this.undoStack.length - 1].offset;
    if (restoreOffset < 0) return;
    EditableDivUtils.makeSelectionIn(this.activeElement, restoreOffset, null, true);
  }

  canUndo(): boolean {
    if (!this.activeElement) return false;
    if (this.undoStack && (this.undoStack.length > 1 || this.activeElement.textContent !== this.undoStack[0].text)) {
      return true;
    }
    return false;
  }

  redo(): void {
    if (!this.activeElement) return;
    if (this.redoStack.length > 0) {
      this.undoStack.push(this.redoStack.pop());
    }
    this.activeElement.innerHTML = this.undoStack[this.undoStack.length - 1].html;
    var restoreOffset = this.undoStack[this.undoStack.length - 1].offset;
    if (restoreOffset < 0) return;
    EditableDivUtils.makeSelectionIn(this.activeElement, restoreOffset, null, true);
  }

  getPageWindow(): Window {
      return (<HTMLIFrameElement>top.document.getElementById('page')).contentWindow;
  }

  /**
   * Displays the correct markup for the current page.
   */
  doMarkup(): void {

    if (!this.readyToDoMarkup()) return;
    if (this.currentMarkupType === MarkupType.None) return;

    var oldSelectionPosition = -1;
    if (this.activeElement) oldSelectionPosition = EditableDivUtils.getElementSelectionIndex(this.activeElement);

    var editableElements = ReaderToolsModel.getElementsToCheck();

    // qtips can be orphaned if the element they belong to is deleted
    // (and so the mouse can't move off their owning element, and they never go away).
    // BL-2758 But if we're in a source collection we may have valid source bubbles,
    // don't delete them!
    if (editableElements.length > 0)
      $(editableElements[0]).closest('body').children('.qtip:not(".uibloomSourceTextsBubble")').remove();

    switch (this.currentMarkupType) {
      case MarkupType.Leveled:

        if (editableElements.length > 0) {
          var options = {
            maxWordsPerSentence: this.maxWordsPerSentenceOnThisPage(),
            maxWordsPerPage: this.maxWordsPerPage()
          };
          editableElements.checkLeveledReader(options);

          // update current page words
          var pageDiv = $('body', this.getPageWindow().document).find('div.bloom-page');
          if (pageDiv.length) {
            if (pageDiv[0].id)
              this.pageIDToText[pageDiv[0].id] = editableElements['allWords'];
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
        var cumulativeWords: DataWord[];
        var sightWords: string[];
        if (this.synphony.source.useAllowedWords === 1) {
          cumulativeWords = [];
          sightWords = ReaderToolsModel.selectWordsFromAllowedLists(this.stageNumber);
        }
        else {
          cumulativeWords = this.getStageWords();
          sightWords = this.getSightWords(this.stageNumber);
        }

        editableElements.checkDecodableReader({
          focusWords: cumulativeWords,
          previousWords: cumulativeWords,
          // theOneLibSynphony lowercases the text, so we must do the same with sight words.  (BL-2550)
          sightWords: sightWords.join(' ').toLowerCase().split(/\s/),
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
    var page: HTMLIFrameElement = <HTMLIFrameElement>parent.window.document.getElementById('page');
    if (page)
      page.contentWindow.postMessage('Qtips', "*");
  }

  maxWordsPerSentenceOnThisPage(): number {
    var levels: ReaderLevel[] = this.synphony.getLevels();
    if (levels.length <= 0) {
      return 9999;
    }
    return levels[this.levelNumber - 1].getMaxWordsPerSentence();
  }

  maxWordsPerBook(): number {
    var levels: ReaderLevel[] = this.synphony.getLevels();
    if (levels.length <= 0) {
      return 999999;
    }
    return levels[this.levelNumber - 1].getMaxWordsPerBook();
  }

  maxUniqueWordsPerBook(): number {
    var levels: ReaderLevel[] = this.synphony.getLevels();
    if (levels.length <= 0) {
      return 99999;
    }
    return levels[this.levelNumber - 1].getMaxUniqueWordsPerBook();
  }

  maxWordsPerPage(): number {
    var levels: ReaderLevel[] = this.synphony.getLevels();
    if (levels.length <= 0) {
      return 9999;
    }
    return levels[this.levelNumber - 1].getMaxWordsPerPage();
  }

// Though I'm not using this now, it was hard-won, and instructive. So I'm leaving it here
// as an example for now in case we need to do this transformResponse thing.
//   static getTextOfWholeBook(): void {
//       //review: on the server, this is actually a json string
//     axios.get<string>('/bloom/api/readers/textOfContentPages',
//     {
//       //when there are no content pages in the book, the server returns, properly, "{}"
//       //However the default transformResponse of axios eagerly does a JSON.Parse on everything,
//       //even if we say we only want text/plain and the server says it gave text/plain.  Sheesh.
//       //So we specify our own identity transformResponse
//         transformResponse:  (data: string) => <string>data }
//     ).then(result => {
//       //The return looks like {'12547c' : 'hello there', '898af87' : 'words of this page', etc.} 
//       ReaderToolsModel.model.pageIDToText = JSON.parse(result.data);
//       ReaderToolsModel.model.doMarkup();
//     });
//   }

  static getTextOfWholeBook(): void {
    axios.get<any[]>('/bloom/api/readers/textOfContentPages').then(result => {
      //The result looks like {'0bbf0bc5-4533-4c26-92d9-bea8fd064525:' : 'Jane saw spot', 'AAbf0bc5-4533-4c26-92d9-bea8fd064525:' : 'words of this page', etc.} 
      ReaderToolsModel.model.pageIDToText = result.data;
      ReaderToolsModel.model.doMarkup();
    });
  }
  
  displayBookTotals(): void {

    if (this.pageIDToText.length === 0) {
      ReaderToolsModel.getTextOfWholeBook();
      return;
    }

    var pageStrings = _.values(this.pageIDToText);

    ReaderToolsModel.updateActualCount(ReaderToolsModel.countWordsInBook(pageStrings), this.maxWordsPerBook(), 'actualWordCount');
    ReaderToolsModel.updateActualCount(ReaderToolsModel.maxWordsPerPageInBook(pageStrings), this.maxWordsPerPage(), 'actualWordsPerPageBook');
    ReaderToolsModel.updateActualCount(ReaderToolsModel.uniqueWordsInBook(pageStrings), this.maxUniqueWordsPerBook(), 'actualUniqueWords');
  }

  static countWordsInBook(pageStrings: string[]): number {
    var total = 0;
    for (var i = 0; i < pageStrings.length; i++) {
      var page = pageStrings[i];
      var fragments: TextFragment[] = theOneLibSynphony.stringToSentences(page);

      // remove inter-sentence space
      fragments = fragments.filter(function (frag) {
        return frag.isSentence;
      });

      for (var j = 0; j < fragments.length; j++) {
        total += fragments[j].wordCount();
      }
    }
    return total;
  }

  static uniqueWordsInBook(pageStrings: string[]): number {
    var wordMap = {};
    for (var i = 0; i < pageStrings.length; i++) {
      var page = pageStrings[i];
      var fragments: TextFragment[] = theOneLibSynphony.stringToSentences(page);

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
  }

  static maxWordsPerPageInBook(pageStrings: string[]): number {
    var maxWords = 0;

    for (var i = 0; i < pageStrings.length; i++) {
      var page = pageStrings[i];

      // split into sentences
      var fragments = theOneLibSynphony.stringToSentences(page);

      // remove inter-sentence space
      fragments = fragments.filter(function (frag) {
        return frag.isSentence;
      });

      var subMax = 0;
      for (var j = 0; j < fragments.length; j++) {
        subMax += fragments[j].wordCount();
      }

      if (subMax > maxWords) maxWords = subMax;
    }

    return maxWords;
  }

  static updateActualCount(actual: number, max: number, id: string): void {
    $('#' + id).html(actual.toString());
    var acceptable = (actual <= max) || (max === 0);
    // The two styles here must match ones defined in ReaderTools.htm or its stylesheet.
    // It's important NOT to use two names where one is a substring of the other (e.g., unacceptable
    // instead of tooLarge). That will mess things up going from the longer to the shorter.
    ReaderToolsModel.setPresenceOfClass(id, acceptable, "acceptable");
    ReaderToolsModel.setPresenceOfClass(id, !acceptable, "tooLarge");
  }

  updateMaxWordsPerSentenceOnPage(): void {
    ReaderToolsModel.updateActualCount(ReaderToolsModel.getElementsToCheck().getMaxSentenceLength(), this.maxWordsPerSentenceOnThisPage(), 'actualWordsPerSentence');
  }

  updateTotalWordsOnPage(): void {
    ReaderToolsModel.updateActualCount(ReaderToolsModel.getElementsToCheck().getTotalWordCount(), this.maxWordsPerPage(), 'actualWordsPerPage');
  }

  /** Should be called early on, before other init. */
  setSynphony(val: ReadersSynphonyWrapper): void {
    this.synphony = val;
  }

//   getSynphony(): ReadersSynphonyWrapper {
//     return this.synphony;
//   }

  /**
   * This group of functions uses jquery (if loaded) to update the real model.
   * Unit testing should spy or otherwise replace these functions, since $ will not be usefully defined.
   */
  static updateElementContent(id: string, val: string): void {
    $("#" + id).html(val);
  }

  static getElementAttribute(id: string, attrName: string): string {
    return $("#" + id).attr(attrName);
  }

  static setElementAttribute(id: string, attrName: string, val: string): void {
    $("#" + id).attr(attrName, val);
  }

  /**
   * Add words from a file to the list of all words. Does not produce duplicates.
   * @param fileContents
   */
  addWordsFromFile(fileContents: string): void {

//reviewslog: at the moment, thes first two clauses just do the same things

    // is this a Synphony data file?
    if (fileContents.substr(0, 12) === '{"LangName":' ||
        //TODO remove this is bizarre artifact of the original synphony, where the data file was actually some javascript. Still used in a unit test.
        fileContents.substr(0, 12) === 'setLangData(') {
      theOneLibSynphony.langDataFromString(fileContents);
      ReaderToolsModel.model.synphony.loadFromLangData(theOneLanguageDataInstance);
   }
    // handle sample texts files that are just a set of space-delimeted words
    else {
      var words = theOneLibSynphony.getWordsFromHtmlString(fileContents);
      // Limit the number of words processed from files.  The program hangs on very long lists.
      var lim = words.length;
      var wordNames = Object.keys(this.allWords);
      if (wordNames.length + words.length > this.maxAllowedWords) {
        lim = this.maxAllowedWords - wordNames.length;
      }
      for (var i = 0; i < lim; i++) {
        this.allWords[words[i]] = 1 + (this.allWords[words[i]] || 0);
      }
    }
  }

  static beginSetTextsList(textsArg: string[] ): Promise<void> {
    // only save the file types we can read
    ReaderToolsModel.model.texts = textsArg.filter(t => {
      var ext = t.split('.').pop();
      return ReaderToolsModel.getReadableFileExtensions().indexOf(ext) > -1;
    });
    return ReaderToolsModel.model.beginGetAllSampleFiles().then(() => {
      ReaderToolsModel.model.addWordsToSynphony();

      // The word list has been received. Now we are using setTimeout() to delay the remainder of the word
      // list processing so the UI doesn't appear frozen as long.
      setTimeout(function () {

        ReaderToolsModel.model.wordListLoaded = true;
        ReaderToolsModel.model.updateControlContents(); // needed if user deletes all of the stages.
        ReaderToolsModel.model.doMarkup();
        ReaderToolsModel.model.updateWordList();
        ReaderToolsModel.model.processWordListChangedListeners();

        // write out the ReaderToolsWords-xyz.json file
        axios.post('/bloom/api/readers/saveReaderToolsWords', theOneLanguageDataInstance);
      }, 200);
    });
  }


  /**
   * Called to process sample data files.
   * When all of them are read and processed, the promise is resolved.
   */
  beginGetAllSampleFiles(): Promise<void> {
    // The <any> works around a flaw in the declaration of axios.all in axios.d.ts.
    return (<any>axios).all(this.texts.map(fileName => {
      return axios.get<string>('/bloom/api/readers/sampleFileContents', { params: { fileName: fileName } })
        .then(result => {
          //axios get here is giving us an object even though the c# sends a text/plain.
          //and that would normally be great, but unfortunately the downstream code was written to take a raw
          //string (which happpens to be JSON). So for now, we just make it a string.
          var resultAsString = JSON.stringify(result.data);
          ReaderToolsModel.setSampleFileContents(resultAsString);
        });
    }));
  }

  /**
   * Called in response to a request for the contents of a sample text file
   * @param fileContents
   */
  static setSampleFileContents(fileContents: string): void {
    ReaderToolsModel.model.addWordsFromFile(fileContents);
  }

  /**
   * Notify anyone who wants to know that the word list changed
   */
  processWordListChangedListeners(): void {

    var handlers = Object.keys(this.wordListChangedListeners);
    for (var j = 0; j < handlers.length; j++)
      this.wordListChangedListeners[handlers[j]]();
  }

  /**
   * Take the list of words collected from the sample files, add it to SynPhony, and update the Stages.
   */
  addWordsToSynphony() {

    // add words to the word list
    ReadersSynphonyWrapper.addWords(this.allWords);
    theOneLibSynphony.processVocabularyGroups();
  }

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
  static selectWordsFromSynphony(justWordName: boolean, desiredGPCs: string[], knownGPCs: string[],
                          restrictToKnownGPCs: boolean, allowUpperCase?: boolean, syllableLengths?: number[],
                          selectedGroups?: string[], partsOfSpeech?: string[]): any[] {

    if (!selectedGroups) {
      selectedGroups = [];
      for (var i = 1; i <= theOneLanguageDataInstance.VocabularyGroups; i++)
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
      return theOneLibSynphony.selectGPCWordNamesWithArrayCompare(desiredGPCs, knownGPCs, restrictToKnownGPCs, allowUpperCase, syllableLengths, selectedGroups, partsOfSpeech);
    else
      return theOneLibSynphony.selectGPCWordsFromCache(desiredGPCs, knownGPCs, restrictToKnownGPCs, allowUpperCase, syllableLengths, selectedGroups, partsOfSpeech);
  }

  static selectWordsFromAllowedLists(stageNumber: number): string[] {

    var stages: ReaderStage[] = ReaderToolsModel.model.synphony.getStages(stageNumber);

    var words: string[] = [];
    for (var i=0; i < stages.length; i++) {
      if (stages[i].allowedWords)
        words = words.concat(stages[i].allowedWords);
    }

    // we are limiting the number of words to maxAllowedWords for performance reasons
    if (words.length > ReaderToolsModel.model.maxAllowedWords) {
      words = words.slice(0, ReaderToolsModel.model.maxAllowedWords);
    }

    return words;
  }

  static getToolboxWindow(): Window {
      return (<HTMLIFrameElement>document.getElementById('toolbox')).contentWindow;
  }

  /**
   * Get the allowed words for the current stage and all previous stages as an array of DataWord objects
   * @param stageNumber
   * @returns An array of DataWord objects
   */
  static getAllowedWordsAsObjects(stageNumber: number): DataWord[] {

    var words: string[] = ReaderToolsModel.selectWordsFromAllowedLists(stageNumber);
    var returnVal: DataWord[] = [];

    for (var i = 0; i < words.length; i++) {
      returnVal.push(new DataWord(words[i]));
    }

    // inform the user if the list was truncated
    //var toolbox: Document = ReaderToolsModel.getToolboxWindow().document;
     var toolbox = $('#toolbox');
    var msgDiv: JQuery = $(toolbox).find('#allowed-word-list-truncated');

    // if the list was truncated, show the message
    if (words.length < ReaderToolsModel.model.maxAllowedWords) {
      msgDiv.html('');
    }
    else {
      msgDiv.html(theOneLocalizationManager.simpleDotNetFormat($(toolbox).find('#allowed_word_list_truncated_text').html(), [ReaderToolsModel.model.maxAllowedWords.toLocaleString()]));
    }

    return returnVal;
  }

  saveState(): void {

    // this is needed for unit testing
    var toolbox = $('#toolbox');
    if (typeof toolbox.accordion !== 'function') return;

    // this is also needed for unit testing
    var active = toolbox.accordion('option', 'active');
    if (isNaN(active)) return;

    var state = new DRTState();
    state.stage = this.stageNumber;
    state.level = this.levelNumber;
    state.markupType = this.currentMarkupType;
    ToolBox.fireCSharpToolboxEvent('saveToolboxSettingsEvent', "state\tdecodableReader\t" + "stage:" + this.stageNumber + ";sort:" + this.sort);
    ToolBox.fireCSharpToolboxEvent('saveToolboxSettingsEvent', "state\tleveledReader\t" + this.levelNumber);
    theOneLibSynphony.dbSet('drt_state', state);
  }

  restoreState(): void {

    // this is needed for unit testing
    var toolbox = $('#toolbox');
    if (typeof toolbox.accordion !== 'function') return;

    var state = theOneLibSynphony.dbGet('drt_state');
    if (!state) state = new DRTState();

    if (!this.currentMarkupType) this.currentMarkupType = state.markupType;
    this.setStageNumber(state.stage);
    this.setLevelNumber(state.level);
  }

  getAllowedWordsLists(): void {

    var stages = this.synphony.getStages();

    // remember how many we are loading so we know when we're finished
    ReaderToolsModel.model.allowedWordFilesRemaining = stages.length;

    stages.forEach(function(stage, index) {
      if (stage.allowedWordsFile) {
          //axios.get<string>('/bloom/api/readers/allowedWordsList?fileName=' + encodeURIComponent(stage.allowedWordsFile))
          axios.get<string>('/bloom/api/readers/allowedWordsList', { params: { 'fileName': stage.allowedWordsFile } })
              .then(result => ReaderToolsModel.setAllowedWordsListList(result.data, index));
      }
    });
  }

  static setAllowedWordsListList(fileContents: string, stageIndex: number): void {

    // remove this one from the count of files remaining
    ReaderToolsModel.model.allowedWordFilesRemaining--;

    ReaderToolsModel.model.synphony.getStages()[stageIndex].setAllowedWordsString(fileContents);

    // if all loaded...
    if (ReaderToolsModel.model.allowedWordFilesRemaining < 1) {
      ReaderToolsModel.model.wordListLoaded = true;
      ReaderToolsModel.model.updateControlContents();
      ReaderToolsModel.model.doMarkup();
    }
  }
}
