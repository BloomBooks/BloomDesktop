/// <reference path="readerToolsModel.ts" />
/// <reference path="directoryWatcher.ts" />
/// <reference path="../../../typings/jquery.qtip.d.ts" />
/// <reference path="../../../typings/jqueryui/jqueryui.d.ts" />
import {DirectoryWatcher} from "./directoryWatcher";
import {getTheOneReaderToolsModel} from "./readerToolsModel";
import theOneLocalizationManager from '../../../lib/localizationManager/localizationManager';
import {theOneLanguageDataInstance, LanguageData, theOneLibSynphony, ResetLanguageDataInstance}  from './libSynphony/synphony_lib';
import './libSynphony/synphony_lib.js';
import ReadersSynphonyWrapper from './ReadersSynphonyWrapper';
import {ReaderStage, ReaderLevel, ReaderSettings} from './ReaderSettings';
import {DataWord, clearWordCache} from './libSynphony/bloomSynphonyExtensions';
import "../../../lib/jquery.onSafe";
import axios = require('axios');
import * as _ from 'underscore';

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
      if (getTheOneReaderToolsModel().texts)
        getSetupDialogWindow().postMessage('Files\n' + getTheOneReaderToolsModel().texts.join("\r"), '*');
      return;

    case 'Words': // request from setup dialog for a list of words for a stage
      var words: any;
      if (getTheOneReaderToolsModel().synphony.source.useAllowedWords) {//reviewslog
        // params[1] is the stage number
        words = getTheOneReaderToolsModel().selectWordsFromAllowedLists(parseInt(params[1]));
      }
      else {
        // params[1] is a list of known graphemes
        words = getTheOneReaderToolsModel().selectWordsFromSynphony(false, params[1].split(' '), params[1].split(' '), true, true);
      }

      getSetupDialogWindow().postMessage('Words\n' + JSON.stringify(words), '*');
      return;

    case 'SetupType':
      getSetupDialogWindow().postMessage('SetupType\n' + getTheOneReaderToolsModel().setupType, '*');
      return;

    case 'SetMarkupType':
      getTheOneReaderToolsModel().setMarkupType(parseInt(params[1]));
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
    getTheOneReaderToolsModel().incrementStage();
  });

  $('#decStage').onSafe('click.readerTools', function() {
    getTheOneReaderToolsModel().decrementStage();
  });

  $('#sortAlphabetic').onSafe('click.readerTools', function() {
    getTheOneReaderToolsModel().sortAlphabetically();
  });

  $('#sortLength').onSafe('click.readerTools', function() {
    getTheOneReaderToolsModel().sortByLength();
  });

  $('#sortFrequency').onSafe('click.readerTools', function() {
    getTheOneReaderToolsModel().sortByFrequency();
  });

  getTheOneReaderToolsModel().updateControlContents();
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
    getTheOneReaderToolsModel().incrementLevel();
  });

  $('#decLevel').onSafe('click.readerTools', function() {
    getTheOneReaderToolsModel().decrementLevel();
  });

  getTheOneReaderToolsModel().updateControlContents();
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

    axios.get<string>('/bloom/api/collection/defaultFont').then(result => setDefaultFont(result.data));
    axios.get<string>('/bloom/api/readers/io/readerToolSettings').then(settingsFileContent => {
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
 * @global {getTheOneReaderToolsModel()) ReaderToolsModel
 */
function initializeSynphony(settingsFileContent: string): void {
  var synphony = new ReadersSynphonyWrapper();
  synphony.loadSettings(settingsFileContent);
  getTheOneReaderToolsModel().setSynphony(synphony);
  getTheOneReaderToolsModel().restoreState();

  getTheOneReaderToolsModel().updateControlContents();

  // set up a DirectoryWatcher on the Sample Texts directory
  getTheOneReaderToolsModel().directoryWatcher = new DirectoryWatcher('Sample Texts', 10);
  getTheOneReaderToolsModel().directoryWatcher.onChanged('SampleFilesChanged.ReaderTools', readerSampleFilesChanged);
  getTheOneReaderToolsModel().directoryWatcher.start();

  if (synphony.source.useAllowedWords) {
    // get the allowed words for each stage
    getTheOneReaderToolsModel().getAllowedWordsLists();
  }
  else {
    // get the list of sample texts
    axios.get<string>('/bloom/api/readers/ui/sampleTextsList').then(result =>beginSetTextsList(result.data));
  }
}

/**
 * Called in response to a request for the files in the sample texts directory
 * @param textsList List of file names delimited by \r
 */
function beginSetTextsList(textsList: string): Promise<void> {
  return getTheOneReaderToolsModel().beginSetTextsList(textsList.split(/\r/).filter(function(e){return e ? true : false;}));
}

function setDefaultFont(fontName: string): void {
  getTheOneReaderToolsModel().fontName = fontName;
}

/**
 * This method is called whenever a change is detected in the Sample Files directory
 */
export function readerSampleFilesChanged(): void {
    // We have to basically start over; no other way to get things in a consistent state
    // between the changed sample files and the sample words in the dialog itself.
    // We can however keep the current version of the settings saved in the model.
  beginRefreshEverything(getTheOneReaderToolsModel().synphony.source);
}

function refreshSettingsExceptSampleWords(newSettings) {
    var synphony = getTheOneReaderToolsModel().synphony;
    synphony.loadSettings(newSettings);
    if (synphony.source.useAllowedWords) {
        getTheOneReaderToolsModel().getAllowedWordsLists();
    } else {
        getTheOneReaderToolsModel().updateControlContents();
        getTheOneReaderToolsModel().doMarkup();
    }
}

/**
 * Re-creates the one instance of LanguageData and ReadersSynphonyWrapper, populates them from the supplied or current
 * settings and sample word files, and updates the UI to match. Because of the convoluted way we build
 * the indexes inside the LanguageData object, this is the only currently feasible way to get it in
 * a consistent state after changes to the sample words files or the panel in the settings dialog.
 * Returns a promise which is resolved when all the sample words files are loaded and the model is ready to use.
 */
function beginRefreshEverything(settings: ReaderSettings) : Promise<void> {
  // reset the file and word list
  ResetLanguageDataInstance();
  getTheOneReaderToolsModel().allWords = {};
  // This helps with updating the matching words panel in the setup dialog. If we switched to the
  // sample words tab, changed sample words, and switched back, or if the user just edited the sample
  // words files in the background, nothing will have changed that indicates the cache is invalid;
  // but in fact the words that should show for the current stage and state of things may need
  // updating.
  clearWordCache();

  var synphony = new ReadersSynphonyWrapper();
  synphony.loadSettings(settings);
  getTheOneReaderToolsModel().setSynphony(synphony);

  if (synphony.source.useAllowedWords) {
    // reload the allowed words for each stage
    getTheOneReaderToolsModel().getAllowedWordsLists();
  }
  else {
    // reload the sample texts
    return <any>axios.get<string>('/bloom/api/readers/io/sampleTextsList').then(result => beginSetTextsList(result.data));
  }
}

export function beginSaveChangedSettings(settings: ReaderSettings, previousMoreWords: string): Promise<void> {
  return <any>axios.post('/bloom/api/readers/io/readerToolSettings', settings)
    .then(result => {
      // reviewslog: following previous logic that we need to reload files if useAllowedWords
      // is true. Seems we should at least need to do it ALSO if it was PREVIOUSLY true.
      // But that is a very obscure case...we don't expect users to switch back and forth
      // in the basic mechanism by which they define stages.
      if (settings.moreWords !== previousMoreWords || settings.useAllowedWords) {
        return beginRefreshEverything(settings); // caller will resolve when everything is refreshed
      } else {
        refreshSettingsExceptSampleWords(settings);
        return null;
      }
    });
}

/**
 * Adds a function to the list of functions to call when the word list changes
 */
export function addWordListChangedListener(listenerNameAndContext: string, callback: () => {}) {
  getTheOneReaderToolsModel().wordListChangedListeners[listenerNameAndContext] = callback;
}

export function makeLetterWordList(): void {

  // get a copy of the current settings
  var settings: ReaderSettings = <ReaderSettings>jQuery.extend(true, {}, getTheOneReaderToolsModel().synphony.source);

  // remove levels
  if (typeof settings.levels !== null)
    settings.levels = null;

  // get the words for each stage
  var knownGPCS: string[] = [];
  for (var i = 0; i < settings.stages.length; i++) {

    var stageGPCS: string[] = settings.stages[i].letters.split(' ');
    knownGPCS = _.union(knownGPCS, stageGPCS);
    var stageWords: string[] = getTheOneReaderToolsModel().selectWordsFromSynphony(true, stageGPCS, knownGPCS, true, true);
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
  var ajaxSettings = {type: 'POST', url: '/bloom/api/readers/ui/makeLetterAndWordList'};
  ajaxSettings['data'] = {
    settings: JSON.stringify(settings),
    allWords: allWords.join('\t')
  };

  $.ajax(<JQueryAjaxSettings>ajaxSettings)
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
  if ((getTheOneReaderToolsModel().previousHeight !== currentHeight) || (getTheOneReaderToolsModel().previousWidth !== currentWidth)) {

    getTheOneReaderToolsModel().previousHeight = currentHeight;
    getTheOneReaderToolsModel().previousWidth = currentWidth;

    var top = wordList.parent().position().top;

    var synphony = getTheOneReaderToolsModel().synphony;
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
