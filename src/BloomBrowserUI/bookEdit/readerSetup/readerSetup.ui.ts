/// <reference path="readerSetup.io.ts" />
/// <reference path="../js/libsynphony/jquery.text-markup.d.ts" />
/// <reference path="../js/libsynphony/bloom_lib.d.ts" />
/// <reference path="../../lib/localizationManager.ts" />
/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../lib/jquery-ui.d.ts" />
/// <reference path="../../lib/jquery.i18n.custom.ts" />
/// <reference path="../../lib/misc-types.d.ts" />
/// <reference path="../js/jquery.div-columns.ts" />

var desiredGPCs: string[];
var previousGPCs: string[];
var sightWords: string[];
var currentSightWords: string[];

window.addEventListener('message', process_UI_Message, false);

function process_UI_Message(event: MessageEvent): void {

  var params: string[] = event.data.split("\n");

  switch(params[0]) {

    case 'Files':
      var s: string = params[1];
      if (s.length > 0) {

        var files: string[] = s.split('\r');
        var extensions: string[] = getIframeChannel().readableFileExtensions;
                var needsTxtExtension: string = document.getElementById('needs_txt_extension').innerHTML;
        var notSupported: string = document.getElementById('format_not_supported').innerHTML;
        var foundNotSupported: boolean = false;
        files.forEach(function(element, index, array) {
                    var filenameComponents: string[] = element.split('.');
                    if (filenameComponents.length < 2) {
                        array[index] = element + ' ' + '<span class="format-not-supported">' + needsTxtExtension + '</span>';
            foundNotSupported = true;
                    } else {
                        var ext:string = filenameComponents.pop();
                        if (extensions.indexOf(ext) === -1) {
                            array[index] = element + ' ' + '<span class="format-not-supported">' + notSupported + '</span>';
                            foundNotSupported = true;
                        }
          }
        });
        s = files.join('\r');

        if (foundNotSupported)
          document.getElementById('how_to_export').style.display = '';
                else
                    document.getElementById('how_to_export').style.display = 'none';
      }

      var fileList: string = s || document.getElementById('please-add-texts').innerHTML;

      document.getElementById('dls_word_lists').innerHTML = fileList.replace(/\r/g, '<br>');
      return;

    case 'Words':
      displayWordsForSelectedStage(params[1]);
      return;

    case 'SetupType':
      //noinspection JSJQueryEfficiency
      var tabs: JQuery = $('#dlstabs');
      if (params[1] === 'stages') {
        tabs.tabs('option', 'disabled', [3]);
        tabs.tabs('option', 'active', 2);
        var firstStage = $('#stages-table').find('tbody tr:first');
        if (firstStage && (firstStage.length === 0))
          addNewStage();
        else
          firstStage.click(); // select the first stage
      }
      else {
        tabs.tabs('option', 'disabled', [0, 1, 2]);
        tabs.tabs('option', 'active', 3);
        var firstLevel = $('#levels-table').find('tbody tr:first');
        if (firstLevel && (firstLevel.length === 0))
          addNewLevel();
        else
          firstLevel.click(); // select the first level
      }

      // handle the beforeActivate event
      tabs.on('tabsbeforeactivate', function(event, ui) { tabBeforeActivate(ui); });

      return;

    case 'Font':
      var style: HTMLStyleElement = document.createElement('style');
      style.type = 'text/css';
      style.innerHTML = '.book-font { font-family: ' + params[1] + '; }';
      document.getElementsByTagName('head')[0].appendChild(style);
      return;

    case 'Help':
      var helpFile: string;
      //noinspection JSJQueryEfficiency
      switch($('#dlstabs').tabs('option', 'active')) {
        case 0:
          helpFile = '/Tasks/Edit_tasks/Decodable_Reader_Tool/Letters_tab.htm';
          break;
        case 1:
          helpFile = '/Tasks/Edit_tasks/Decodable_Reader_Tool/Words_tab.htm';
          break;
        case 2:
          helpFile = '/Tasks/Edit_tasks/Decodable_Reader_Tool/Decodable_Stages_tab.htm';
          break;
        case 3:
          helpFile = '/Tasks/Edit_tasks/Leveled_Reader_Tool/Reader_Levels_tab.htm';
          break;
        default:
      }
      if (helpFile)
        getIframeChannel().help(helpFile);
      return;

    default:
  }
}

/**
 * Creates the grid of available graphemes
 */
function displayLetters(): void {

  var letters: string[] = ((<HTMLInputElement>document.getElementById('dls_letters')).value.trim()).split(' ');
  letters = letters.filter(function(n){ return n !== ''; });

  // If there are no letters, skip updating the contents of #setup-selected-letters. This leaves it showing the
  // message in the original file, which encourages users to set up an alphabet.
  if (letters.length === 0) return;

  /**
   * If there are more than 42 letters the parent div containing the letter divs will scroll vertically, so the
   * letter divs need to be a different width to accommodate the scroll bar.
   *
   * The suffix 's' stands for 'short', and 'l' stands for 'long.'
   *
   * parent div class rs-letter-container-s does not scroll
   * parent div class rs-letter-container-l scrolls vertically
   *
   * letter div class rs-letters-s fit 7 on a row
   * letter div class rs-letters-l fit 6 on a row (because of the scroll bar)
   */
  var suffix: string = 's';
  if (letters.length > 42) suffix = 'l';

  var div: JQuery = $('#setup-selected-letters');
  div.html('');
  div.removeClass('rs-letter-container-s').removeClass('rs-letter-container-l').addClass('rs-letter-container-' + suffix);

  for (var i = 0; i < letters.length; i++) {
    div.append($('<div class="book-font unselected-letter rs-letters rs-letters-' + suffix + '">' + letters[i] + '</div>'));
  }

  $('div.rs-letters').onOnce('click', function() {
    selectLetter(this);
  });
}

function setLevelValue(value: string): string {

  if ((value === '') || (parseInt(value) === 0)) return '-';
  return value;
}

/**
 * Update the fields when a different stage is selected
 * @param tr The selected table row element
 */
function selectStage(tr: HTMLTableRowElement): void {

  if (tr.classList.contains('selected')) return;

  var currentStage = (<HTMLTableCellElement>tr.cells[0]).innerHTML;
  document.getElementById('setup-stage-number').innerHTML = currentStage;
  document.getElementById('setup-remove-stage').innerHTML = localizationManager.getText('ReaderSetup.RemoveStage', 'Remove Stage {0}', currentStage);
  (<HTMLInputElement>document.getElementById('setup-stage-sight-words')).value = (<HTMLTableCellElement>tr.cells[2]).innerHTML;

  $('#stages-table').find('tbody tr.selected').removeClass('selected').addClass('linked');
  $(tr).removeClass('linked').addClass('selected');

  // get the words
  requestWordsForSelectedStage();
}

function requestWordsForSelectedStage():void {

  var tr = $('#stages-table').find('tbody tr.selected').get(0);


  desiredGPCs = (tr.cells[1].innerHTML).split(' ');
  previousGPCs = $.makeArray($(tr).prevAll().map(function() {
    return this.cells[1].innerHTML.split(' ');
  }));

  var knownGPCS = previousGPCs.join(' ') + ' ' + desiredGPCs.join(' ');
  currentSightWords = (tr.cells[2].innerHTML).split(' ');
  sightWords = $.makeArray($(tr).prevAll().map(function() {
    return this.cells[2].innerHTML.split(' ');
  }));

  sightWords = _.union(sightWords, currentSightWords);

  // remove empty items
  sightWords = _.compact(sightWords);

  accordionWindow().postMessage('Words\n' + knownGPCS, '*');
}

/**
 * Update the stage when a letter is selected or unselected.
 * @param  div
 */
function selectLetter(div: HTMLDivElement): void {

  var tr: JQuery = $('#stages-table').find('tbody tr.selected');

  // do not do anything if there is no selected stage
  if (tr.length === 0) return;

  // update the css classes
  if (div.classList.contains('unselected-letter'))
    $(div).removeClass('unselected-letter').addClass('current-letter');
  else if (div.classList.contains('current-letter'))
    $(div).removeClass('current-letter').addClass('unselected-letter');
  else
    return;

  // update the stages table
  var letters = $('.current-letter').map(function() {
    return this.innerHTML;
  });
  tr.find('td:nth-child(2)').html($.makeArray(letters).join(' '));

  requestWordsForSelectedStage();
}

/**
 * Highlights the graphemes for the current stage
 * @param tr Table row
 */
function selectLetters(tr: HTMLTableRowElement) {

  // remove current formatting
  var letters: JQuery = $('.rs-letters').removeClass('current-letter').removeClass('previous-letter').addClass('unselected-letter');

  // letters in the current stage
  var stage_letters: string[] = (<HTMLTableCellElement>tr.cells[1]).innerHTML.split(' ');
  var current: JQuery = letters.filter(function(index, element) {
    return stage_letters.indexOf(element.innerHTML) > -1;
  });

  // letters in previous stages
  stage_letters = $.makeArray($(tr).prevAll().map(function() {
    return this.cells[1].innerHTML.split(' ');
  }));
  var previous = letters.filter(function(index, element) {
    return stage_letters.indexOf(element.innerHTML) > -1;
  });

  // show current and previous letters
  if (current.length > 0) current.removeClass('unselected-letter').addClass('current-letter');
  if (previous.length > 0) previous.removeClass('unselected-letter').addClass('previous-letter');
}

/**
 * Update display when a different level is selected
 * @param tr
 */
function selectLevel(tr: HTMLTableRowElement) {

  if (tr.classList.contains('selected')) return;

  var currentLevel = getCellInnerHTML(tr, 0);
  document.getElementById('setup-level-number').innerHTML = currentLevel;
  document.getElementById('setup-remove-level').innerHTML = localizationManager.getText('ReaderSetup.RemoveLevel', 'Remove Level {0}', currentLevel);

  $('#levels-table').find('tbody tr.selected').removeClass('selected').addClass('linked');
  $(tr).removeClass('linked').addClass('selected');

  // check boxes and text boxes
  setLevelCheckBoxValue('words-per-sentence', getCellInnerHTML(tr, 1));
  setLevelCheckBoxValue('words-per-page', getCellInnerHTML(tr, 2));
  setLevelCheckBoxValue('words-per-book', getCellInnerHTML(tr, 3));
  setLevelCheckBoxValue('unique-words-per-book', getCellInnerHTML(tr, 4));

  // things to remember
  var vals = getCellInnerHTML(tr, 5).split('\n');
  var val = vals.join('</li><li contenteditable="true">');
  document.getElementById('things-to-remember').innerHTML = '<li contenteditable="true">' + val + '</li>';
}

function getCellInnerHTML(tr: HTMLTableRowElement, cellIndex: number): string {
  return (<HTMLTableCellElement>tr.cells[cellIndex]).innerHTML;
}

function setLevelCheckBoxValue(id: string, value: string): void {

  var checked: boolean = value !== '-';
  (<HTMLInputElement>document.getElementById('use-' + id)).checked = checked;

  var txt: HTMLInputElement = <HTMLInputElement>document.getElementById('max-' + id);
  txt.value = value === '-' ? '' : value;
  txt.disabled = !checked;
}

function displayWordsForSelectedStage(wordsStr: string): void {

  var wordList = document.getElementById('rs-matching-words');
  wordList.innerHTML = '';

  var wordsObj: Object = JSON.parse(wordsStr);
  var words: DataWord[] = <DataWord[]>_.toArray(wordsObj);

  // add sight words
  _.each(sightWords, function(sw: string) {

    var word: DataWord = _.find(words, function(w: DataWord) {
      return w.Name === sw;
    });

    if (typeof word === 'undefined') {
      word = new DataWord(sw);

      if (_.contains(currentSightWords, sw)) {
        word.html = '<span class="sight-word current-sight-word">' + sw + '</span>';
      }
      else {
        word.html = '<span class="sight-word">' + sw + '</span>';
      }
      words.push(word);
    }
  });

  // sort the list
  words = _.sortBy(words, function(w) { return w.Name; });

  var result: string = '';
  var longestWord: string = '';
  var longestWordLength: number = 0;

  _.each(words, function(w: DataWord) {

    if (!w.html)
      w.html = $.markupGraphemes(w.Name, w.GPCForm, desiredGPCs);
    result += '<div class="book-font word">' + w.html + '</div>';

    if (w.Name.length > longestWordLength) {
      longestWord = w.Name;
      longestWordLength = longestWord.length;
    }
  });

  // set the list
  wordList.innerHTML = result;

  // make columns
  $.divsToColumnsBasedOnLongestWord('word', longestWord);

  // display the count
  document.getElementById('setup-words-count').innerHTML = words.length.toString();
}

function addNewStage(): void {

  var tbody: JQuery = $('#stages-table').find('tbody');
  tbody.append('<tr class="linked"><td>' + (tbody.children().length + 1) + '</td><td class="book-font"></td><td class="book-font"></td></tr>');

  // click event for stage rows
  tbody.find('tr:last').onOnce('click', function() {
    selectStage(this);
    displayLetters();
    selectLetters(this);
  });

  // go to the new stage
  tbody.find('tr:last').click();
}

function addNewLevel(): void {

  var tbody: JQuery = $('#levels-table').find('tbody');
  tbody.append('<tr class="linked"><td>' + (tbody.children().length + 1) + '</td><td class="words-per-sentence">-</td><td class="words-per-page">-</td><td class="words-per-book">-</td><td class="unique-words-per-book">-</td><td style="display: none"></td></tr>');

  // click event for stage rows
  tbody.find('tr:last').onOnce('click', function() {
    selectLevel(this);
  });

  // go to the new stage
  tbody.find('tr:last').click();
}

function tabBeforeActivate(ui): void {

  var panelId: string = ui['newPanel'][0].id;

  if (panelId === 'dlstabs-2') { // Decodable Stages tab

    var allLetters: string[] = ((<HTMLInputElement>document.getElementById('dls_letters')).value.trim()).split(' ');
    var tbody: JQuery = $('#stages-table').find('tbody');

    // update letters grid
    displayLetters();

    // update letters in stages
    var rows: JQuery = tbody.find('tr');
    rows.each(function() {

      // get the letters for this stage
      var letters = this.cells[1].innerHTML.split(' ');

      // make sure each letter for this stage is all in the allLetters list
      letters = _.intersection(letters, allLetters);
      this.cells[1].innerHTML = letters.join(' ');
    });

    // select letters for current stage
    var tr = tbody.find('tr.selected');
    if (tr.length === 1) {
      selectLetters(<HTMLTableRowElement>tr[0]);
    }

    // update more words
    if ((<HTMLInputElement>document.getElementById('dls_more_words')).value !== previousMoreWords) {

      // remember the new list of more words
      previousMoreWords = (<HTMLInputElement>document.getElementById('dls_more_words')).value;

      // save the changes and update lists
      var accordion = accordionWindow();
      saveChangedSettings(function() {
        if (typeof accordion['readerSampleFilesChanged'] === 'function')
          accordion['readerSampleFilesChanged']();
      });
    }
  }
}

/**
 * Handles special keys in the Things to Remember list, which is a "ul" element
 * @param jqueryEvent
 */
function handleThingsToRemember(jqueryEvent: JQueryEventObject): void {

  switch(jqueryEvent.which) {
    case 13: // add new li
      var x = $('<li contenteditable="true"></li>').insertAfter(jqueryEvent.target);
      jqueryEvent.preventDefault();
      x.focus();
      break;

    case 38: // up arrow
      var prev = $(jqueryEvent.target).prev();
      if (prev.length) prev.focus();
      break;

    case 40: // down arrow
      var next = $(jqueryEvent.target).next();
      if (next.length) next.focus();
      break;

    case 8: // backspace
      var thisItem = $(jqueryEvent.target);

      // if the item is not blank, return
      if (thisItem.text().length > 0) return;

      // cannot remove the last item
      var otherItem = thisItem.prev();
      if (!otherItem.length) otherItem = thisItem.next();
      if (!otherItem.length) return;

      // OK to remove the item
      thisItem.remove();
      otherItem.focus();
      break;

    default:
  }

}

/**
 * Update the stage when the list of sight words changes
 * @param ta Text area
 */
function updateSightWords(ta: HTMLInputElement): void {
  var words: string = ta.value.trim().replace(/ ( )+/g, ' '); // remove consecutive spaces
  $('#stages-table').find('tbody tr.selected td:nth-child(3)').html(words);
}

function removeStage(): void {

  var tbody: JQuery = $('#stages-table').find('tbody');

  // remove the current stage
  var current_row: JQuery = tbody.find('tr.selected');
  var current_stage: number = parseInt(current_row.find("td").eq(0).html());
  current_row.remove();

  var rows: JQuery = tbody.find('tr');

  if (rows.length > 0) {

    // renumber remaining stages
    renumberRows(rows);

    // select a different stage
    if (rows.length >= current_stage)
      tbody.find('tr:nth-child(' + current_stage + ')').click();
    else
      tbody.find('tr:nth-child(' + rows.length + ')').click();
  }
  else {
    resetStageDetail();
  }
}

function resetStageDetail(): void {

  document.getElementById('setup-words-count').innerHTML = '0';
  document.getElementById('rs-matching-words').innerHTML = '';
  (<HTMLInputElement>document.getElementById('setup-stage-sight-words')).value = '';
  $('.rs-letters').removeClass('current-letter').removeClass('previous-letter').addClass('unselected-letter');
}

function renumberRows(rows: JQuery): void {

  var rowNum = 1;

  $.each(rows, function() {
    this.cells[0].innerHTML = rowNum++;
  });
}

function removeLevel(): void {

  var tbody: JQuery = $('#levels-table').find('tbody');

  // remove the current level
  var current_row: JQuery = tbody.find('tr.selected');
  var current_stage: number = parseInt(current_row.find("td").eq(0).html());
  current_row.remove();

  var rows = tbody.find('tr');

  if (rows.length > 0) {

    // renumber remaining levels
    renumberRows(rows);

    // select a different stage
    if (rows.length >= current_stage)
      tbody.find('tr:nth-child(' + current_stage + ')').click();
    else
      tbody.find('tr:nth-child(' + rows.length + ')').click();
  }
  else {
    resetLevelDetail();
  }
}

function resetLevelDetail(): void {

  document.getElementById('setup-level-number').innerHTML = '0';

  setLevelCheckBoxValue('words-per-sentence', '-');
  setLevelCheckBoxValue('words-per-page', '-');
  setLevelCheckBoxValue('words-per-book', '-');
  setLevelCheckBoxValue('unique-words-per-book', '-');

  document.getElementById('things-to-remember').innerHTML = '<li contenteditable="true"></li>';
}

/**
 * Converts the items of the "ul" element to a string and stores it in the levels table
 */
function storeThingsToRemember(): void {

  var val: string = document.getElementById('things-to-remember').innerHTML.trim();

  // remove html and split into array
  var vals: string[] = val.replace(/<li contenteditable="true">/g, '').replace(/<br>/g, '').split('</li>');

  // remove blank lines
  vals = vals.filter(function(e){ var x = e.trim(); return (x.length > 0 && x !== '&nbsp;'); });

  // store
  $('#levels-table').find('tbody tr.selected td:nth-child(6)').html(vals.join('\n'));
}

function updateNumbers(tableId: string): void {

  var tbody: JQuery = $('#' + tableId).find('tbody');
  var rows = tbody.find('tr');
  renumberRows(rows);

  var currentStage = tbody.find('tr.selected td:nth-child(1)').html();

  if (tableId === 'levels-table') {
    document.getElementById('setup-level-number').innerHTML = currentStage;
    document.getElementById('setup-remove-level').innerHTML = localizationManager.getText('ReaderSetup.RemoveLevel', 'Remove Level {0}', currentStage);
  }
  else {
    document.getElementById('setup-stage-number').innerHTML = currentStage;
    document.getElementById('setup-remove-stage').innerHTML = localizationManager.getText('ReaderSetup.RemoveStage', 'Remove Stage {0}', currentStage);
  }
}

/**
 * Called to update the stage numbers on the screen after rows are reordered.
 */
function updateStageNumbers() {
  updateNumbers('stages-table');
}

/**
 * Called to update the level numbers on the screen after rows are reordered.
 */
function updateLevelNumbers() {
  updateNumbers('levels-table');
}

function firstSetupLetters(): boolean {
  $('#dlstabs').tabs('option', 'active', 0);
  return false;
}

/**
 * Event handlers
 *
 * NOTE: Returning false from a click event handler cancels the default action of the element.
 *       e.g. If the element is an anchor with the href set, navigation is canceled.
 *       e.g. If the element is a submit button, form submission is canceled.
 */
function attachEventHandlers(): void {

  if (typeof ($) === "function") {

    $("#open-text-folder").onOnce('click', function() {
      getIframeChannel().simpleAjaxNoCallback('/bloom/readers/openTextsFolder');
      return false;
    });

    $("#setup-add-stage").onOnce('click', function() {
      addNewStage();
      return false;
    });

    $("#define-sight-words").onOnce('click', function() {
      alert('What are sight words?');
      return false;
    });

    $("#setup-stage-sight-words").onOnce('keyup', function() {
      updateSightWords(this);
      requestWordsForSelectedStage();
    });

    $('#setup-remove-stage').onOnce('click', function() {
      removeStage();
      return false;
    });

    $('#setup-add-level').onOnce('click', function() {
      addNewLevel();
      return false;
    });

    $('#setup-remove-level').onOnce('click', function() {
      removeLevel();
      return false;
    });

    var toRemember = $('#things-to-remember');
    toRemember.onOnce('keydown', handleThingsToRemember);
    toRemember.onOnce('keyup', storeThingsToRemember);

    var levelDetail = $('#level-detail');
    levelDetail.find('.level-checkbox').onOnce('change', function() {
      var id = this.id.replace(/^use-/, '');
      var txtBox: HTMLInputElement = <HTMLInputElement>document.getElementById('max-' + id);
      txtBox.disabled = !this.checked;
      $('#levels-table').find('tbody tr.selected td.' + id).html(this.checked ? txtBox.value : '');
    });

    levelDetail.find('.level-textbox').onOnce('keyup', function() {
      var id = this.id.replace(/^max-/, '');
      $('#levels-table').find('tbody tr.selected td.' + id).html(this.value);
    });
  }
}

function setWordContainerHeight() {

  // set height of word list
  var div: JQuery = $('#setup-stage-matching-words').find('div:first-child');
  var ht = $('setup-words-count').height();
  div.css('height', 'calc(100% - ' + ht + 'px)');
}

/**
 * Called after localized strings are loaded.
 */
function finishInitializing() {
  $('#stages-table').find('tbody').sortable({ stop: updateStageNumbers });
  $('#levels-table').find('tbody').sortable({ stop: updateLevelNumbers });
  accordionWindow().postMessage('Texts', '*');
  setWordContainerHeight();
}

/**
 * The ReaderTools calls this function to notify the dialog that the word list and/or the list of sample files
 * has changed.
 */
function wordListChangedCallback() {
  var accordion = accordionWindow();
  if (!accordion) return;
  accordion.postMessage('Texts', '*');
  requestWordsForSelectedStage();
}

$(document).ready(function () {
  attachEventHandlers();
  $('body').find('*[data-i18n]').localize(finishInitializing);
  var accordion = accordionWindow();
  accordion['addWordListChangedListener']('wordListChanged.ReaderSetup', wordListChangedCallback);
  (<longPressInterface>$('textarea')).longPress();
});
