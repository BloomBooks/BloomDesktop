/// <reference path="readerToolsModel.ts" />
/// <reference path="directoryWatcher.ts" />
/// <reference path="../../../typings/jquery.qtip.d.ts" />
/// <reference path="../../../typings/jqueryui/jqueryui.d.ts" />
import {DirectoryWatcher} from "./directoryWatcher";
import {ReaderToolsModel} from "./readerToolsModel";
import theOneLocalizationManager from '../../../lib/localizationManager/localizationManager';
import {theOneLanguageDataInstance, LanguageData, theOneLibSynphony, ResetLanguageDataInstance}  from './libSynphony/synphony_lib';
import './libSynphony/synphony_lib.js';
import SynphonyApi from './synphonyApi';
import {ReaderStage, ReaderLevel, ReaderSettings} from './ReaderSettings';
import {DataWord} from './libSynphony/bloom_lib'; 
import "../../../lib/jquery.onSafe";
import axios = require('axios');

interface textMarkup extends JQueryStatic {
  cssSentenceTooLong(): JQuery;
  cssSightWord(): JQuery;
  cssWordNotFound(): JQuery;
  cssPossibleWord(): JQuery;
}

// listen for messages sent to this page
window.addEventListener('message', processDLRMessage, false);

var readerToolsInitialized: boolean = false;

function getSetupDialogWindow(): Window {
  return (<HTMLIFrameElement>parent.window.document.getElementById("settings_frame")).contentWindow;
}

/**
 * Respond to messages
 * @param {Event} event
 */
function processDLRMessage(event: MessageEvent): void {

  var params = event.data.split("\n");

  switch(params[0]) {
    case 'Texts': // request from setup dialog for the list of sample texts
      if (ReaderToolsModel.model.texts)
        getSetupDialogWindow().postMessage('Files\n' + ReaderToolsModel.model.texts.join("\r"), '*');
      return;

    case 'Words': // request from setup dialog for a list of words for a stage
      var words: any;
      if (ReaderToolsModel.model.synphony.source.useAllowedWords) {//reviewslog
        // params[1] is the stage number
        words = ReaderToolsModel.selectWordsFromAllowedLists(parseInt(params[1]));
      }
      else {
        // params[1] is a list of known graphemes
        words = ReaderToolsModel.selectWordsFromSynphony(false, params[1].split(' '), params[1].split(' '), true, true);
      }

      getSetupDialogWindow().postMessage('Words\n' + JSON.stringify(words), '*');
      return;

    case 'Refresh': // notification from setup dialog that settings have changed
      var synphony = ReaderToolsModel.model.synphony;//reviewslog
      synphony.loadSettings(JSON.parse(params[1]));

      if (synphony.source.useAllowedWords) {
        ReaderToolsModel.model.getAllowedWordsLists();
      }
      else {
        ReaderToolsModel.model.updateControlContents();
        ReaderToolsModel.model.doMarkup();
      }

      return;

    case 'SetupType':
      getSetupDialogWindow().postMessage('SetupType\n' + ReaderToolsModel.model.setupType, '*');
      return;

    case 'SetMarkupType':
      ReaderToolsModel.model.setMarkupType(parseInt(params[1]));
      return;

    case 'Qtips': // request from toolbox to add qtips to marked-up spans
      // We could make separate messages for these...
      markDecodableStatus();
      markLeveledStatus();

      return;

    default:
  }
}

function markDecodableStatus(): void {
  // q-tips; mark sight words and non-decodable words
  var sightWord = theOneLocalizationManager.getText('EditTab.EditTab.Toolbox.DecodableReaderTool.SightWord', 'Sight Word');
  var notDecodable = theOneLocalizationManager.getText('EditTab.EditTab.Toolbox.DecodableReaderTool.WordNotDecodable', 'This word is not decodable in this stage.');
  var editableElements = $(".bloom-content1");
  editableElements.find('span.' + (<textMarkup>$).cssSightWord()).each(function() {
    this.qtip({ content: sightWord });
  });

  editableElements.find('span.' + (<textMarkup>$).cssWordNotFound()).each(function() {
    this.qtip({ content: notDecodable });
  });

// we're considering dropping this entirely
// We are disabling the "Possible Word" feature at this time.
//editableElements.find('span.' + $.cssPossibleWord()).each(function() {
//    $(this.qtip({ content: 'This word is decodable in this stage, but is not part of the collected list of words.' });
//});
}

function markLeveledStatus(): void {
  // q-tips; mark sentences that are too long
  var tooLong = theOneLocalizationManager.getText('EditTab.EditTab.Toolbox.LeveledReaderTool.SentenceTooLong',
      'This sentence is too long for this level.');
  var editableElements = $(".bloom-content1");
  editableElements.find('span.' + (<textMarkup>$).cssSentenceTooLong()).each(function() {
    $(this).qtip({ content: tooLong });
  });
}

export function beginInitializeDecodableReaderTool(): JQueryPromise<void> {
    // load synphony settings and then finish init
    return beginLoadSynphonySettings().then(() => {

  // use the off/on pattern so the event is not added twice if the tool is closed and then reopened
  $('#incStage').onSafe('click.readerTools', function() {
    ReaderToolsModel.model.incrementStage();
  });

  $('#decStage').onSafe('click.readerTools', function() {
    ReaderToolsModel.model.decrementStage();
  });

  $('#sortAlphabetic').onSafe('click.readerTools', function() {
    ReaderToolsModel.model.sortAlphabetically();
  });

  $('#sortLength').onSafe('click.readerTools', function() {
    ReaderToolsModel.model.sortByLength();
  });

  $('#sortFrequency').onSafe('click.readerTools', function() {
    ReaderToolsModel.model.sortByFrequency();
  });

  ReaderToolsModel.model.updateControlContents();
  $("#toolbox").accordion("refresh");

  $(window).resize(function() {
    resizeWordList(false);
  });

  setTimeout(function() { resizeWordList(); }, 200);
        setTimeout(function () { $.divsToColumns('letter'); }, 100);
    });
}

export function beginInitializeLeveledReaderTool(): JQueryPromise <void> {
  // load synphony settings
    return beginLoadSynphonySettings().then(() => {

  $('#incLevel').onSafe('click.readerTools', function() {
    ReaderToolsModel.model.incrementLevel();
  });

  $('#decLevel').onSafe('click.readerTools', function() {
    ReaderToolsModel.model.decrementLevel();
  });

  ReaderToolsModel.model.updateControlContents();
  $("#toolbox").accordion("refresh");
    });
}

function beginLoadSynphonySettings(): JQueryPromise<void> {
  // make sure synphony is initialized
    var result = $.Deferred<void>();
    if (readerToolsInitialized) {
        result.resolve();
        return result;
    }
    readerToolsInitialized = true;

    axios.get<string>('/bloom/readers/getDefaultFont').then(result => setDefaultFont(result.data));
    axios.get<string>('/bloom/readers/loadReaderToolSettings').then(settingsFileContent => {
        initializeSynphony(settingsFileContent.data);
        result.resolve();
    });
    return result;
}

/**
 * The function that is called to hook everything up.
 * Note: settingsFileContent may be empty.
 *
 * @param settingsFileContent The content of the standard JSON) file that stores the Synphony settings for the collection.
 * @global {ReaderToolsModel) ReaderToolsModel.model
 */
function initializeSynphony(settingsFileContent: string): void {
  var synphony = new SynphonyApi();
  synphony.loadSettings(settingsFileContent);
  ReaderToolsModel.model.setSynphony(synphony);
  ReaderToolsModel.model.restoreState();

  ReaderToolsModel.model.updateControlContents();

  // set up a DirectoryWatcher on the Sample Texts directory
  ReaderToolsModel.model.directoryWatcher = new DirectoryWatcher('Sample Texts', 10);
  ReaderToolsModel.model.directoryWatcher.onChanged('SampleFilesChanged.ReaderTools', readerSampleFilesChanged);
  ReaderToolsModel.model.directoryWatcher.start();

  if (synphony.source.useAllowedWords) {
    // get the allowed words for each stage
    ReaderToolsModel.model.getAllowedWordsLists();
  }
  else {
    // get the list of sample texts
    axios.get<string>('/bloom/readers/getSampleTextsList').then(result => setTextsList(result.data));
  }
}

/**
 * Called in response to a request for the files in the sample texts directory
 * @param textsList List of file names delimited by \r
 */
function setTextsList(textsList: string): void {

  ReaderToolsModel.model.texts = textsList.split(/\r/).filter(function(e){return e ? true : false;});
  ReaderToolsModel.model.getNextSampleFile();
}

function setDefaultFont(fontName: string): void {
  ReaderToolsModel.model.fontName = fontName;
}

/**
 * This method is called whenever a change is detected in the Sample Files directory
 */
function readerSampleFilesChanged(): void {

  // reset the file and word list
  //theOneLanguageDataInstance = new LanguageData();
  ResetLanguageDataInstance();
  ReaderToolsModel.model.allWords = {};
  ReaderToolsModel.model.textCounter = 0;

  var settings = ReaderToolsModel.model.synphony.source;
  ReaderToolsModel.model.setSynphony(new SynphonyApi());

  var synphony = ReaderToolsModel.model.synphony;
  synphony.loadSettings(settings);

  // reload the sample texts
  axios.get<string>('/bloom/readers/getSampleTextsList').then(result => setTextsList(result.data));
}

/**
 * Adds a function to the list of functions to call when the word list changes
 */
export function addWordListChangedListener(listenerNameAndContext: string, callback: () => {}) {
  ReaderToolsModel.model.wordListChangedListeners[listenerNameAndContext] = callback;
}

function makeLetterWordList(): void {

  // get a copy of the current settings
  var settings: ReaderSettings = <ReaderSettings>jQuery.extend(true, {}, ReaderToolsModel.model.synphony.source);

  // remove levels
  if (typeof settings.levels !== null)
    settings.levels = null;

  // get the words for each stage
  var knownGPCS: string[] = [];
  for (var i = 0; i < settings.stages.length; i++) {

    var stageGPCS: string[] = settings.stages[i].letters.split(' ');
    knownGPCS = _.union(knownGPCS, stageGPCS);
    var stageWords: string[] = ReaderToolsModel.selectWordsFromSynphony(true, stageGPCS, knownGPCS, true, true);
    settings.stages[i].words = <string[]>_.toArray(stageWords);
  }

  // get list of all words
  var allGroups: string[] = [];
  for (var j = 1; j <= theOneLanguageDataInstance.VocabularyGroups; j++)
    allGroups.push('group' + j);
  allGroups = theOneLibSynphony.chooseVocabGroups(allGroups);

  var allWords: string[] = [];
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

  $.ajax(<JQueryAjaxSettings>ajaxSettings)
}

function loadExternalLink(url: string): void {
  $.get(url, function() {
    // ignore response
    // in this case, we just want to open an external browser with a link, so we don't want to process the response
  });
}

/**
 * We need to check the size of the decodable reader tool pane periodically so we can adjust the height of the word list
 * @global {number} previousHeight
 */
export function resizeWordList(startTimeout: boolean = true): void {

  var div: JQuery = $('body').find('div[data-panelId="decodableReaderTool"]');
  if (div.length === 0) return; // if not found, the tool was closed

  var wordList: JQuery = div.find('#wordList');
  var currentHeight: number = div.height();
  var currentWidth: number = wordList.width();

  // resize the word list if the size of the pane changed
  if ((ReaderToolsModel.previousHeight !== currentHeight) || (ReaderToolsModel.previousWidth !== currentWidth)) {

    ReaderToolsModel.previousHeight = currentHeight;
    ReaderToolsModel.previousWidth = currentWidth;

    var top = wordList.parent().position().top;

    var synphony = ReaderToolsModel.model.synphony;
    if (synphony.source) {

      var ht = currentHeight - top;
      if (synphony.source.useAllowedWords === 1) {
        ht -= div.find('#allowed-word-list-truncated').height();
      }
      else {
        ht -= div.find('#make-letter-word-list-div').height();
      }

      // for a reason I haven't discovered, the height calculation is always off by 6 pixels
      ht += 6;

      if (ht < 50) ht = 50;

      wordList.parent().css('height', Math.floor(ht) + 'px');
    }
  }

  if (startTimeout) setTimeout(function() { resizeWordList(); }, 500);
}
