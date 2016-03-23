/// <reference path="readerToolsModel.ts" />

interface qtipInterface extends JQuery {
  qtip(options: any): JQuery;
}

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
      if (model.texts)
        getSetupDialogWindow().postMessage('Files\n' + model.texts.join("\r"), '*');
      return;

    case 'Words': // request from setup dialog for a list of words for a stage
      var words: any;
      if (model.getSynphony().source.useAllowedWords) {
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
      var synphony = model.getSynphony();
      synphony.loadSettings(JSON.parse(params[1]));

      if (model.getSynphony().source.useAllowedWords) {
        model.getAllowedWordsLists();
      }
      else {
        model.updateControlContents();
        model.doMarkup();
      }

      return;

    case 'SetupType':
      getSetupDialogWindow().postMessage('SetupType\n' + model.setupType, '*');
      return;

    case 'SetMarkupType':
      model.setMarkupType(parseInt(params[1]));
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
  var sightWord = localizationManager.getText('EditTab.EditTab.Toolbox.DecodableReaderTool.SightWord', 'Sight Word');
  var notDecodable = localizationManager.getText('EditTab.EditTab.Toolbox.DecodableReaderTool.WordNotDecodable', 'This word is not decodable in this stage.');
  var editableElements = $(".bloom-content1");
  editableElements.find('span.' + (<textMarkup>$).cssSightWord()).each(function() {
    (<qtipInterface>$(this)).qtip({ content: sightWord });
  });

  editableElements.find('span.' + (<textMarkup>$).cssWordNotFound()).each(function() {
    (<qtipInterface>$(this)).qtip({ content: notDecodable });
  });

// we're considering dropping this entirely
// We are disabling the "Possible Word" feature at this time.
//editableElements.find('span.' + $.cssPossibleWord()).each(function() {
//    (<qtipInterface>$(this)).qtip({ content: 'This word is decodable in this stage, but is not part of the collected list of words.' });
//});
}

function markLeveledStatus(): void {
  // q-tips; mark sentences that are too long
  var tooLong = localizationManager.getText('EditTab.EditTab.Toolbox.LeveledReaderTool.SentenceTooLong',
      'This sentence is too long for this level.');
  var editableElements = $(".bloom-content1");
  editableElements.find('span.' + (<textMarkup>$).cssSentenceTooLong()).each(function() {
    (<qtipInterface>$(this)).qtip({ content: tooLong });
  });
}

function beginInitializeDecodableReaderTool(): JQueryPromise<void> {
    // load synphony settings and then finish init
    return beginLoadSynphonySettings().then(() => {

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
        $("#toolbox").accordion("refresh");

        $(window).resize(function() {
            resizeWordList(false);
        });

        setTimeout(function() { resizeWordList(); }, 200);
        setTimeout(function () { $.divsToColumns('letter'); }, 100);
    });
}

function beginInitializeLeveledReaderTool(): JQueryPromise <void> {
    // load synphony settings
    return beginLoadSynphonySettings().then(() => {

        $('#incLevel').onOnce('click.readerTools', function() {
            model.incrementLevel();
        });

        $('#decLevel').onOnce('click.readerTools', function() {
            model.decrementLevel();
        });

        model.updateControlContents();
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
    iframeChannel.simpleAjaxGet('/bloom/readers/getDefaultFont', setDefaultFont);
    iframeChannel.simpleAjaxGet('/bloom/readers/loadReaderToolSettings', (settingsFileContent) => {
        initializeSynphony(settingsFileContent);
        result.resolve();
    });
    return result;
}

/**
 * The function that is called to hook everything up.
 * Note: settingsFileContent may be empty.
 *
 * @param settingsFileContent The content of the standard JSON) file that stores the Synphony settings for the collection.
 * @global {ReaderToolsModel) model
 */
function initializeSynphony(settingsFileContent: string): void {
  var synphony = new SynphonyApi();
  synphony.loadSettings(settingsFileContent);
  model.setSynphony(synphony);
  model.restoreState();

  model.updateControlContents();

  // set up a DirectoryWatcher on the Sample Texts directory
  model.directoryWatcher = new DirectoryWatcher('Sample Texts', 10);
  model.directoryWatcher.onChanged('SampleFilesChanged.ReaderTools', readerSampleFilesChanged);
  model.directoryWatcher.start();

  if (synphony.source.useAllowedWords) {
    // get the allowed words for each stage
    model.getAllowedWordsLists();
  }
  else {
    // get the list of sample texts
    iframeChannel.simpleAjaxGet('/bloom/readers/getSampleTextsList', setTextsList);
  }
}

/**
 * Called in response to a request for the files in the sample texts directory
 * @param textsList List of file names delimited by \r
 */
function setTextsList(textsList: string): void {

  model.texts = textsList.split(/\r/).filter(function(e){return e ? true : false;});
  model.getNextSampleFile();
}

function setDefaultFont(fontName: string): void {
  model.fontName = fontName;
}

/**
 * This method is called whenever a change is detected in the Sample Files directory
 */
function readerSampleFilesChanged(): void {

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

/**
 * Adds a function to the list of functions to call when the word list changes
 */
function addWordListChangedListener(listenerNameAndContext: string, callback: () => {}) {
  model.wordListChangedListeners[listenerNameAndContext] = callback;
}

function makeLetterWordList(): void {

  // get a copy of the current settings
  var settings: ReaderSettings = <ReaderSettings>jQuery.extend(true, {}, model.getSynphony().source);

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
  for (var j = 1; j <= lang_data.VocabularyGroups; j++)
    allGroups.push('group' + j);
  allGroups = libsynphony.chooseVocabGroups(allGroups);

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
function resizeWordList(startTimeout: boolean = true): void {

  var div: JQuery = $('body').find('div[data-panelId="decodableReaderTool"]');
  if (div.length === 0) return; // if not found, the tool was closed

  var wordList: JQuery = div.find('#wordList');
  var currentHeight: number = div.height();
  var currentWidth: number = wordList.width();

  // resize the word list if the size of the pane changed
  if ((previousHeight !== currentHeight) || (previousWidth !== currentWidth)) {

    previousHeight = currentHeight;
    previousWidth = currentWidth;

    var top = wordList.parent().position().top;

    var synphony = model.getSynphony();
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
