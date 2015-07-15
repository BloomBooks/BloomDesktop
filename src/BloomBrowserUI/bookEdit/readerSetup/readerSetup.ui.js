/// <reference path="readerSetup.io.ts" />
/// <reference path="../js/libsynphony/jquery.text-markup.d.ts" />
/// <reference path="../js/libsynphony/bloom_lib.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="../../lib/jquery-ui.d.ts" />
/// <reference path="../../lib/jquery.i18n.custom.ts" />
/// <reference path="../../lib/misc-types.d.ts" />
/// <reference path="../js/jquery.div-columns.ts" />
var desiredGPCs;
var previousGPCs;
var sightWords;
var currentSightWords;
window.addEventListener('message', process_UI_Message, false);
function process_UI_Message(event) {
    var params = event.data.split("\n");
    switch (params[0]) {
        case 'Files':
            var s = params[1];
            if (s.length > 0) {
                var files = s.split('\r');
                var extensions = getIframeChannel().readableFileExtensions;
                var needsTxtExtension = document.getElementById('needs_txt_extension').innerHTML;
                var notSupported = document.getElementById('format_not_supported').innerHTML;
                var foundNotSupported = false;
                files.forEach(function (element, index, array) {
                    var filenameComponents = element.split('.');
                    if (filenameComponents.length < 2) {
                        array[index] = element + ' ' + '<span class="format-not-supported">' + needsTxtExtension + '</span>';
                        foundNotSupported = true;
                    }
                    else {
                        var ext = filenameComponents.pop();
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
            var fileList = s || document.getElementById('please-add-texts').innerHTML;
            document.getElementById('dls_word_lists').innerHTML = fileList.replace(/\r/g, '<br>');
            return;
        case 'Words':
            var useSampleWords = $('input[name="words-or-letters"]:checked').val() === '1';
            if (useSampleWords)
                displayAllowedWordsForSelectedStage(params[1]);
            else
                displayWordsForSelectedStage(params[1]);
            return;
        case 'SetupType':
            //noinspection JSJQueryEfficiency
            var tabs = $('#dlstabs');
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
            tabs.on('tabsbeforeactivate', function (event, ui) {
                tabBeforeActivate(ui);
            });
            return;
        case 'Font':
            var style = document.createElement('style');
            style.type = 'text/css';
            style.innerHTML = '.book-font { font-family: ' + params[1] + '; }';
            document.getElementsByTagName('head')[0].appendChild(style);
            return;
        case 'Help':
            var helpFile;
            switch ($('#dlstabs').tabs('option', 'active')) {
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
function displayLetters() {
    var letters = cleanSpaceDelimitedList(document.getElementById('dls_letters').value.trim()).split(' ');
    letters = letters.filter(function (n) {
        return n !== '';
    });
    // If there are no letters, skip updating the contents of #setup-selected-letters. This leaves it showing the
    // message in the original file, which encourages users to set up an alphabet.
    if (letters.length === 0)
        return;
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
    var suffix = 's';
    if (letters.length > 42)
        suffix = 'l';
    var div = $('#setup-selected-letters');
    div.html('');
    div.removeClass('rs-letter-container-s').removeClass('rs-letter-container-l').addClass('rs-letter-container-' + suffix);
    for (var i = 0; i < letters.length; i++) {
        div.append($('<div class="book-font unselected-letter rs-letters rs-letters-' + suffix + '">' + letters[i] + '</div>'));
    }
    $('div.rs-letters').onOnce('click', function () {
        selectLetter(this);
    });
}
function setLevelValue(value) {
    if (!value)
        return '-';
    var testVal = (typeof value === 'number') ? value : parseInt(value);
    if (testVal === 0)
        return '-';
    return testVal.toString();
}
/**
 * Update the fields when a different stage is selected
 * @param tr The selected table row element
 */
function selectStage(tr) {
    if (tr.classList.contains('selected'))
        return;
    var currentStage = tr.cells[0].innerHTML;
    document.getElementById('setup-stage-number').innerHTML = currentStage;
    document.getElementById('setup-remove-stage').innerHTML = localizationManager.getText('ReaderSetup.RemoveStage', 'Remove Stage {0}', currentStage);
    document.getElementById('setup-stage-sight-words').value = tr.cells[2].innerHTML;
    $('#stages-table').find('tbody tr.selected').removeClass('selected').addClass('linked');
    var currentTr = $(tr);
    currentTr.removeClass('linked').addClass('selected');
    setAllowedWordsFile(currentTr.find('td:nth-child(4)').html());
    // get the words
    requestWordsForSelectedStage();
}
function requestWordsForSelectedStage() {
    var tr = $('#stages-table').find('tbody tr.selected').get(0);
    desiredGPCs = (tr.cells[1].innerHTML).split(' ');
    previousGPCs = $.makeArray($(tr).prevAll().map(function () {
        return this.cells[1].innerHTML.split(' ');
    }));
    var knownGPCS = previousGPCs.join(' ') + ' ' + desiredGPCs.join(' ');
    currentSightWords = (tr.cells[2].innerHTML).split(' ');
    sightWords = $.makeArray($(tr).prevAll().map(function () {
        return this.cells[2].innerHTML.split(' ');
    }));
    sightWords = _.union(sightWords, currentSightWords);
    // remove empty items
    sightWords = _.compact(sightWords);
    var useSampleWords = $('input[name="words-or-letters"]:checked').val() === '1';
    if (useSampleWords)
        accordionWindow().postMessage('Words\n' + tr.cells[0].innerHTML, '*');
    else
        accordionWindow().postMessage('Words\n' + knownGPCS, '*');
}
/**
 * Update the stage when a letter is selected or unselected.
 * @param  div
 */
function selectLetter(div) {
    var tr = $('#stages-table').find('tbody tr.selected');
    // do not do anything if there is no selected stage
    if (tr.length === 0)
        return;
    // update the css classes
    if (div.classList.contains('unselected-letter'))
        $(div).removeClass('unselected-letter').addClass('current-letter');
    else if (div.classList.contains('current-letter'))
        $(div).removeClass('current-letter').addClass('unselected-letter');
    else
        return;
    // update the stages table
    var letters = $('.current-letter').map(function () {
        return this.innerHTML;
    });
    tr.find('td:nth-child(2)').html($.makeArray(letters).join(' '));
    requestWordsForSelectedStage();
}
/**
 * Highlights the graphemes for the current stage
 * @param tr Table row
 */
function selectLetters(tr) {
    // remove current formatting
    var letters = $('.rs-letters').removeClass('current-letter').removeClass('previous-letter').addClass('unselected-letter');
    // letters in the current stage
    var stage_letters = tr.cells[1].innerHTML.split(' ');
    var current = letters.filter(function (index, element) {
        return stage_letters.indexOf(element.innerHTML) > -1;
    });
    // letters in previous stages
    stage_letters = $.makeArray($(tr).prevAll().map(function () {
        return this.cells[1].innerHTML.split(' ');
    }));
    var previous = letters.filter(function (index, element) {
        return stage_letters.indexOf(element.innerHTML) > -1;
    });
    // show current and previous letters
    if (current.length > 0)
        current.removeClass('unselected-letter').addClass('current-letter');
    if (previous.length > 0)
        previous.removeClass('unselected-letter').addClass('previous-letter');
}
/**
 * Update display when a different level is selected
 * @param tr
 */
function selectLevel(tr) {
    if (tr.classList.contains('selected'))
        return;
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
function getCellInnerHTML(tr, cellIndex) {
    return tr.cells[cellIndex].innerHTML;
}
function setLevelCheckBoxValue(id, value) {
    var checked = value !== '-';
    document.getElementById('use-' + id).checked = checked;
    var txt = document.getElementById('max-' + id);
    txt.value = value === '-' ? '' : value;
    txt.disabled = !checked;
}
function displayAllowedWordsForSelectedStage(wordsStr) {
    var wordList = document.getElementById('rs-matching-words');
    wordList.innerHTML = '';
    var wordsObj = JSON.parse(wordsStr);
    var words = _.toArray(wordsObj);
    var result = '';
    var longestWord = '';
    var longestWordLength = 0;
    _.each(words, function (w) {
        result += '<div class="book-font word">' + w + '</div>';
        if (w.length > longestWordLength) {
            longestWord = w;
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
function displayWordsForSelectedStage(wordsStr) {
    var wordList = document.getElementById('rs-matching-words');
    wordList.innerHTML = '';
    var wordsObj = JSON.parse(wordsStr);
    var words = _.toArray(wordsObj);
    // add sight words
    _.each(sightWords, function (sw) {
        var word = _.find(words, function (w) {
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
    words = _.sortBy(words, function (w) {
        return w.Name;
    });
    var result = '';
    var longestWord = '';
    var longestWordLength = 0;
    _.each(words, function (w) {
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
function addNewStage() {
    var tbody = $('#stages-table').find('tbody');
    tbody.append('<tr class="linked"><td>' + (tbody.children().length + 1) + '</td><td class="book-font"></td><td class="book-font"></td><td class="book-font"></td></tr>');
    // click event for stage rows
    tbody.find('tr:last').onOnce('click', function () {
        selectStage(this);
        displayLetters();
        selectLetters(this);
    });
    // go to the new stage
    tbody.find('tr:last').click();
}
function addNewLevel() {
    var tbody = $('#levels-table').find('tbody');
    tbody.append('<tr class="linked"><td>' + (tbody.children().length + 1) + '</td><td class="words-per-sentence">-</td><td class="words-per-page">-</td><td class="words-per-book">-</td><td class="unique-words-per-book">-</td><td style="display: none"></td></tr>');
    // click event for stage rows
    tbody.find('tr:last').onOnce('click', function () {
        selectLevel(this);
    });
    // go to the new stage
    tbody.find('tr:last').click();
}
function tabBeforeActivate(ui) {
    var panelId = ui['newPanel'][0].id;
    if (panelId === 'dlstabs-2') {
        var allLetters = cleanSpaceDelimitedList(document.getElementById('dls_letters').value.trim()).split(' ');
        var tbody = $('#stages-table').find('tbody');
        // update letters grid
        displayLetters();
        // update letters in stages
        var rows = tbody.find('tr');
        rows.each(function () {
            // get the letters for this stage
            var letters = this.cells[1].innerHTML.split(' ');
            // make sure each letter for this stage is all in the allLetters list
            letters = _.intersection(letters, allLetters);
            this.cells[1].innerHTML = letters.join(' ');
        });
        // select letters for current stage
        var tr = tbody.find('tr.selected');
        if (tr.length === 1) {
            selectLetters(tr[0]);
        }
        // update more words
        if (document.getElementById('dls_more_words').value !== previousMoreWords) {
            // remember the new list of more words
            previousMoreWords = document.getElementById('dls_more_words').value;
            // save the changes and update lists
            var accordion = accordionWindow();
            saveChangedSettings(function () {
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
function handleThingsToRemember(jqueryEvent) {
    switch (jqueryEvent.which) {
        case 13:
            var x = $('<li contenteditable="true"></li>').insertAfter(jqueryEvent.target);
            jqueryEvent.preventDefault();
            x.focus();
            break;
        case 38:
            var prev = $(jqueryEvent.target).prev();
            if (prev.length)
                prev.focus();
            break;
        case 40:
            var next = $(jqueryEvent.target).next();
            if (next.length)
                next.focus();
            break;
        case 8:
            var thisItem = $(jqueryEvent.target);
            // if the item is not blank, return
            if (thisItem.text().length > 0)
                return;
            // cannot remove the last item
            var otherItem = thisItem.prev();
            if (!otherItem.length)
                otherItem = thisItem.next();
            if (!otherItem.length)
                return;
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
function updateSightWords(ta) {
    var words = cleanSpaceDelimitedList(ta.value);
    $('#stages-table').find('tbody tr.selected td:nth-child(3)').html(words);
}
function removeStage() {
    var tbody = $('#stages-table').find('tbody');
    // remove the current stage
    var current_row = tbody.find('tr.selected');
    var current_stage = parseInt(current_row.find("td").eq(0).html());
    current_row.remove();
    var rows = tbody.find('tr');
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
function resetStageDetail() {
    document.getElementById('setup-words-count').innerHTML = '0';
    document.getElementById('rs-matching-words').innerHTML = '';
    document.getElementById('setup-stage-sight-words').value = '';
    $('.rs-letters').removeClass('current-letter').removeClass('previous-letter').addClass('unselected-letter');
}
function renumberRows(rows) {
    var rowNum = 1;
    $.each(rows, function () {
        this.cells[0].innerHTML = (rowNum++).toString();
    });
}
function removeLevel() {
    var tbody = $('#levels-table').find('tbody');
    // remove the current level
    var current_row = tbody.find('tr.selected');
    var current_stage = parseInt(current_row.find("td").eq(0).html());
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
function resetLevelDetail() {
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
function storeThingsToRemember() {
    var val = document.getElementById('things-to-remember').innerHTML.trim();
    // remove html and split into array
    var vals = val.replace(/<li contenteditable="true">/g, '').replace(/<br>/g, '').split('</li>');
    // remove blank lines
    vals = vals.filter(function (e) {
        var x = e.trim();
        return (x.length > 0 && x !== '&nbsp;');
    });
    // store
    $('#levels-table').find('tbody tr.selected td:nth-child(6)').html(vals.join('\n'));
}
function updateNumbers(tableId) {
    var tbody = $('#' + tableId).find('tbody');
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
function firstSetupLetters() {
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
function attachEventHandlers() {
    if (typeof ($) === "function") {
        $("#open-text-folder").onOnce('click', function () {
            getIframeChannel().simpleAjaxNoCallback('/bloom/readers/openTextsFolder');
            return false;
        });
        $("#setup-add-stage").onOnce('click', function () {
            addNewStage();
            return false;
        });
        $("#define-sight-words").onOnce('click', function () {
            alert('What are sight words?');
            return false;
        });
        $("#setup-stage-sight-words").onOnce('keyup', function () {
            updateSightWords(this);
            requestWordsForSelectedStage();
        });
        $('#setup-remove-stage').onOnce('click', function () {
            removeStage();
            return false;
        });
        $('#setup-add-level').onOnce('click', function () {
            addNewLevel();
            return false;
        });
        $('#setup-remove-level').onOnce('click', function () {
            removeLevel();
            return false;
        });
        var toRemember = $('#things-to-remember');
        toRemember.onOnce('keydown', handleThingsToRemember);
        toRemember.onOnce('keyup', storeThingsToRemember);
        var levelDetail = $('#level-detail');
        levelDetail.find('.level-checkbox').onOnce('change', function () {
            var id = this.id.replace(/^use-/, '');
            var txtBox = document.getElementById('max-' + id);
            txtBox.disabled = !this.checked;
            $('#levels-table').find('tbody tr.selected td.' + id).html(this.checked ? txtBox.value : '');
        });
        levelDetail.find('.level-textbox').onOnce('keyup', function () {
            var id = this.id.replace(/^max-/, '');
            $('#levels-table').find('tbody tr.selected td.' + id).html(this.value);
        });
        $('input[name="words-or-letters"]').onOnce('change', function () {
            enableSampleWords();
        });
        $('#setup-choose-allowed-words-file').onOnce('click', function () {
            getIframeChannel().simpleAjaxPost('/bloom/readers/selectStageAllowedWordsFile', function (fileName) {
                if (fileName)
                    setAllowedWordsFile(fileName);
            });
            return false;
        });
        $('#remove-allowed-word-file').onOnce('click', function () {
            setAllowedWordsFile('');
            return false;
        });
        $('#allowed-words-file-div').onOnce('mouseenter', function () {
            var title = localizationManager.getText('ReaderSetup.RemoveWordList', 'Remove from this stage');
            var anchor = $(this).find('a');
            anchor.attr('title', title);
            anchor.show();
        });
        $('#allowed-words-file-div').onOnce('mouseleave', function () {
            $(this).find('a').hide();
        });
    }
}
function setAllowedWordsFile(fileName) {
    document.getElementById('allowed-words-file').innerHTML = fileName;
    if (fileName) {
        document.getElementById('setup-choose-allowed-words-file').style.display = 'none';
        document.getElementById('allowed-words-file-div').style.display = '';
    }
    else {
        document.getElementById('setup-choose-allowed-words-file').style.display = '';
        document.getElementById('allowed-words-file-div').style.display = 'none';
        fileName = ''; // to be sure it isn't undefined
    }
    $('#stages-table').find('tbody tr.selected td:nth-child(4)').html(fileName);
}
function enableSampleWords() {
    // get the selected option
    var useSampleWords = $('input[name="words-or-letters"]:checked').val() === '1';
    // initialize control state
    var controls = $('#dlstabs-1').find('.disableable');
    var stagesTable = $('#stages-table');
    controls.removeClass('disabled');
    stagesTable.removeClass('hide-second-column');
    stagesTable.removeClass('hide-third-column');
    stagesTable.removeClass('hide-fourth-column');
    // enable or disable
    if (useSampleWords) {
        controls.addClass('disabled');
        stagesTable.addClass('hide-second-column');
        stagesTable.addClass('hide-third-column');
    }
    else {
        stagesTable.addClass('hide-fourth-column');
    }
    // controls for letter-based stages
    document.getElementById('setup-stage-letters-and-words').style.display = useSampleWords ? 'none' : '';
    document.getElementById('matching-words-span').style.display = useSampleWords ? 'none' : '';
    // controls for word-list-based stages
    document.getElementById('setup-stage-words-file').style.display = useSampleWords ? '' : 'none';
    document.getElementById('allowed-words-span').style.display = useSampleWords ? '' : 'none';
    //
    //
}
function setWordContainerHeight() {
    // set height of word list
    var div = $('#setup-stage-matching-words').find('div:first-child');
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
    if (!accordion)
        return;
    accordion.postMessage('Texts', '*');
    requestWordsForSelectedStage();
}
$(document).ready(function () {
    attachEventHandlers();
    $('body').find('*[data-i18n]').localize(finishInitializing);
    var accordion = accordionWindow();
    accordion['addWordListChangedListener']('wordListChanged.ReaderSetup', wordListChangedCallback);
    $('textarea').longPress();
});
//# sourceMappingURL=readerSetup.ui.js.map