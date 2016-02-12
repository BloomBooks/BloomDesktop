/// <reference path="../../../toolbox/decodableReader/readerSettings.ts" />
/// <reference path="readerSetup.ui.ts" />
import {enableSampleWords, displayLetters, selectLetters, selectLevel, selectStage, setLevelValue} from './readerSetup.ui';
import {ReaderStage, ReaderLevel, ReaderSettings, ReaderSettingsReplacer} from '../ReaderSettings';
import "../../../../lib/jquery.onSafe.js";
import axios = require('axios');

var previousMoreWords: string;

window.addEventListener('message', process_IO_Message, false);

export interface ToolboxWindow extends Window{
    FrameExports:any;
}
export function toolboxWindow(): ToolboxWindow {
  if (window.parent)
    return (<HTMLIFrameElement>window.parent.document.getElementById('toolbox')).contentWindow as ToolboxWindow;
}

function process_IO_Message(event: MessageEvent): void {

  var params = event.data.split("\n");

  switch(params[0]) {
    case 'OK':
      saveClicked();
      return;

    case 'Data':
      loadReaderSetupData(params[1]);
      toolboxWindow().postMessage('SetupType', '*');
      return;

    default:
  }
}
export function setPreviousMoreWords(words: string){
  previousMoreWords = words;
}
export function getPreviousMoreWords():string{
  return previousMoreWords;
}

/**
 * Initializes the dialog with the current settings.
 * @param {String} jsonData The contents of the settings file
 */
function loadReaderSetupData(jsonData: string): void {

  if ((!jsonData) || (jsonData === '""')) return;

  // validate data
  var data = JSON.parse(jsonData);
  if (!data.letters) data.letters = '';
  if (!data.moreWords) data.moreWords = '';
  if (!data.stages) data.stages = [];
  if (!data.levels) data.levels = [];
  if (data.stages.length === 0) data.stages.push(new ReaderStage('1'));
  if (data.levels.length === 0) data.levels.push(new ReaderLevel('1'));
  if (!data.useAllowedWords) data.useAllowedWords = 0;

  // language tab
  (<HTMLInputElement>document.getElementById('dls_letters')).value = data.letters;
  setPreviousMoreWords(data.moreWords.replace(/ /g, '\n'));
  (<HTMLInputElement>document.getElementById('dls_more_words')).value = previousMoreWords;
  $('input[name="words-or-letters"][value="' + data.useAllowedWords + '"]').prop('checked', true);
  enableSampleWords();

  // stages tab
  displayLetters();
  var stages = data.stages;
  var tbody = $('#stages-table').find('tbody');
  tbody.html('');

  for (var i = 0; i < stages.length; i++) {
    if (!stages[i].letters) stages[i].letters = '';
    if (!stages[i].sightWords) stages[i].sightWords = '';
    if (!stages[i].allowedWordsFile) stages[i].allowedWordsFile = '';
    tbody.append('<tr class="linked"><td>' + (i + 1) + '</td><td class="book-font">' + stages[i].letters + '</td><td class="book-font">' + stages[i].sightWords + '</td><td class="book-font">' + stages[i].allowedWordsFile + '</td></tr>');
  }

  // click event for stage rows
  tbody.find('tr').onSafe('click', function() {
    selectStage(this);
    displayLetters();
    selectLetters(this);
  });

  // levels tab
  var levels = data.levels;
  var tbodyLevels = $('#levels-table').find('tbody');
  tbodyLevels.html('');
  for (var j = 0; j < levels.length; j++) {
    var level = levels[j];
    tbodyLevels.append('<tr class="linked"><td>' + (j + 1) + '</td><td class="words-per-sentence">' +  setLevelValue(level.maxWordsPerSentence) + '</td><td class="words-per-page">' +  setLevelValue(level.maxWordsPerPage) + '</td><td class="words-per-book">' +  setLevelValue(level.maxWordsPerBook) + '</td><td class="unique-words-per-book">' +  setLevelValue(level.maxUniqueWordsPerBook) + '</td><td style="display: none">' + level.thingsToRemember.join('\n') + '</td></tr>');
  }

  // click event for level rows
  tbodyLevels.find('tr').onSafe('click', function() {
    selectLevel(this);
  });
}

function saveClicked(): void {

  var toolbox:any = toolboxWindow();
  // update more words
  if (((<HTMLInputElement>document.getElementById('dls_more_words')).value !== previousMoreWords)
    || (parseInt($('input[name="words-or-letters"]:checked').val()) != 0)) {


    // save the changes and update lists
    saveChangedSettings(function() {
      if (typeof toolbox['readerSampleFilesChanged'] === 'function')
        toolbox['readerSampleFilesChanged']();
      toolbox.closeSetupDialog();
    });
  }
  else {
      saveChangedSettings(toolbox.closeSetupDialog);
  }
}

/**
 * Pass the settings to C# to be saved.
 * @param [callback]
 */
export function saveChangedSettings(callback?: Function): void {

  // get the values
  var s = getChangedSettings();

  // send to parent
  var settingsStr = JSON.stringify(s, ReaderSettingsReplacer);
  toolboxWindow().postMessage('Refresh\n' + settingsStr, '*');

  // save now
  if (callback)
    axios.post('/bloom/readers/saveReaderToolSettings', { params: { data: settingsStr } }).then(result => {callback(result.data)});
  else
    axios.post('/bloom/readers/saveReaderToolSettings', { params: { data: settingsStr } });
}

function getChangedSettings(): any {

  var s: ReaderSettings = new ReaderSettings();
  s.letters = cleanSpaceDelimitedList((<HTMLInputElement>document.getElementById('dls_letters')).value);

  // remove duplicates from the more words list
  var moreWords: string[] = _.uniq(((<HTMLInputElement>document.getElementById('dls_more_words')).value).split("\n"));

  // remove empty lines from the more words list
  moreWords = _.filter(moreWords, function(a: string) { return a.trim() !== ''; });
  s.moreWords = moreWords.join(' ');

  s.useAllowedWords = parseInt($('input[name="words-or-letters"]:checked').val());

  // stages
  var stages: JQuery = $('#stages-table').find('tbody tr');
  for (var i: number = 0; i < stages.length; i++) {
    var stage: ReaderStage = new ReaderStage((i + 1).toString());
    var row: HTMLTableRowElement = <HTMLTableRowElement>stages[i];
    stage.letters = (<HTMLTableCellElement>row.cells[1]).innerHTML;
    stage.sightWords = cleanSpaceDelimitedList((<HTMLTableCellElement>row.cells[2]).innerHTML);
    stage.allowedWordsFile = (<HTMLTableCellElement>row.cells[3]).innerHTML;

    // do not save stage with no data
    if (stage.letters || stage.sightWords || stage.allowedWordsFile)
      s.stages.push(stage);
  }

  // levels
  var levels: JQuery = $('#levels-table').find('tbody tr');
  for (var j: number = 0; j < levels.length; j++) {
    var level: ReaderLevel = new ReaderLevel((j + 1).toString());
    var row: HTMLTableRowElement = <HTMLTableRowElement>levels[j];
    level.maxWordsPerSentence = getLevelValue((<HTMLTableCellElement>row.cells[1]).innerHTML);
    level.maxWordsPerPage = getLevelValue((<HTMLTableCellElement>row.cells[2]).innerHTML);
    level.maxWordsPerBook = getLevelValue((<HTMLTableCellElement>row.cells[3]).innerHTML);
    level.maxUniqueWordsPerBook = getLevelValue((<HTMLTableCellElement>row.cells[4]).innerHTML);
    level.thingsToRemember = (<HTMLTableCellElement>row.cells[5]).innerHTML.split('\n');
    s.levels.push(level);
  }
  return s;
}

function getLevelValue(innerHTML: string): number {

  innerHTML = innerHTML.trim();
  if (innerHTML === '-') return 0;
  return parseInt(innerHTML);
}

/**
 * if the user enters a comma-separated list, remove the commas before saving (this is a space-delimited list)
 * @param original
 * @returns {string}
 */
export function cleanSpaceDelimitedList(original: string): string {

  var cleaned: string = original.replace(/,/g, ' '); // replace commas
  cleaned = cleaned.trim().replace(/ ( )+/g, ' ');   // remove consecutive spaces

  return cleaned;
}
