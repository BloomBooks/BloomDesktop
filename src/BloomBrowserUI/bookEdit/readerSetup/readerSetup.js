// listen for messages sent to this iframe
window.addEventListener('message', processMessage, false);

var desiredGPCs;
var previousGPCs;
var sightWords;
var currentSightWords;
var previousMoreWords;

function accordionWindow() {
    if (window.parent)
        return window.parent.document.getElementById('accordion').contentWindow;
}

/**
 * Respond to messages from the parent document
 * @param {Event} event
 */
function processMessage(event) {

    var params = event.data.split("\n");

    switch(params[0]) {
        case 'OK':
            saveClicked();
            return;

        case 'Data':
            loadReaderSetupData(params[1]);
            accordionWindow().postMessage('SetupType', '*');
            return;

        case 'Files':
            var s = params[1];
            if (s.length > 0) {

                var files = s.split('\r');
                var extensions = getIframeChannel().readableFileExtensions;
                var notSupported = document.getElementById('format_not_supported').innerHTML;
                var foundNotSupported = false;
                files.forEach(function(element, index, array) {
                    var ext = element.split('.').pop();
                    if (extensions.indexOf(ext) === -1) {
                        array[index] = element + ' ' + '<span class="format-not-supported">' + notSupported + '</span>';
                        foundNotSupported = true;
                    }
                });
                s = files.join('\r');

                if (foundNotSupported)
                    document.getElementById('how_to_export').style.display = '';
            }

            var fileList = s || document.getElementById('please-add-texts').innerHTML;

            document.getElementById('dls_word_lists').innerHTML = fileList.replace(/\r/g, '<br>');
            return;

        case 'Words':
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
            tabs.on('tabsbeforeactivate', function(event, ui) { tabBeforeActivate(ui); });

            return;

        case 'Font':
            var style = document.createElement('style');
            style.type = 'text/css';
            style.innerHTML = '.book-font { font-family: ' + params[1] + '; }';
            document.getElementsByTagName('head')[0].appendChild(style);
            return;

        case 'Help':
            var helpFile;
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
            }
            if (helpFile)
                getIframeChannel().help(helpFile);
            return;
    }
}

/**
 * Fires an event for C# to handle.
 * The listeners in C# are set up in EditingModel.cs, in the function "DocumentCompleted()", and using this
 * syntax: _view.AddMessageEventListener("nameOfEvent", FunctionThatHandlesTheEvent);
 * @param {String} eventName
 * @param {String} eventData Note: use JSON.stringify if passing object data.
 */
function fireCSharpSetupEvent(eventName, eventData) {

    var event = new MessageEvent(eventName, {'view' : window, 'bubbles' : true, 'cancelable' : true, 'data' : eventData});
    document.dispatchEvent(event);
}


function saveClicked() {

    // update more words
    if (document.getElementById('dls_more_words').value !== previousMoreWords) {

        var accordion = accordionWindow();

        // save the changes and update lists
        saveChangedSettings(function() {
            if (typeof accordion['readerSampleFilesChanged'] === 'function')
                accordion['readerSampleFilesChanged']();
            parent.window.closeSetupDialog();
        });
    }
    else {
        saveChangedSettings(parent.window.closeSetupDialog);
    }
}

function getChangedSettings() {

    var s = {};
    s.letters = document.getElementById('dls_letters').value.trim();

    // remove duplicates from the more words list
    var moreWords = _.uniq((document.getElementById('dls_more_words').value).split("\n"));

    // remove empty lines from the more words list
    moreWords = _.filter(moreWords, function(a) { return a.trim() !== ''; });
    s.moreWords = moreWords.join(' ');

    // stages
    s.stages = [];
    var stages = $('#stages-table').find('tbody tr');
    for (var i = 0; i < stages.length; i++) {
        var stage = {};
        stage.letters = stages[i].cells[1].innerHTML;
        stage.sightWords = stages[i].cells[2].innerHTML;

        // do not save stage with no data
        if (stage.letters || stage.sightWords)
            s.stages.push(stage);
    }

    // levels
    s.levels = [];
    var levels = $('#levels-table').find('tbody tr');
    for (var j = 0; j < levels.length; j++) {
        var level = {};
        level.maxWordsPerSentence = getLevelValue(levels[j].cells[1].innerHTML);
        level.maxWordsPerPage = getLevelValue(levels[j].cells[2].innerHTML);
        level.maxWordsPerBook = getLevelValue(levels[j].cells[3].innerHTML);
        level.maxUniqueWordsPerBook = getLevelValue(levels[j].cells[4].innerHTML);
        level.thingsToRemember = levels[j].cells[5].innerHTML.split('\n');
        s.levels.push(level);
    }
    return s;
}

/**
 * Pass the settings to C# to be saved.
 * @param [callback]
 */
function saveChangedSettings(callback) {

    // get the values
    var s = getChangedSettings();

    // send to parent
    var settingsStr = JSON.stringify(s);
    accordionWindow().postMessage('Refresh\n' + settingsStr, '*');

    // save now
    if (callback)
        getIframeChannel().simpleAjaxPost('/bloom/readers/saveReaderToolSettings', callback, settingsStr);
    else
        getIframeChannel().simpleAjaxNoCallback('/bloom/readers/saveReaderToolSettings', settingsStr);
}

function getLevelValue(innerHTML) {

    innerHTML = innerHTML.trim();
    if (innerHTML === '-') return '';
    return innerHTML;
}

function setLevelValue(value) {

    if (value === '') return '-';
    return value;
}

/**
 * Initializes the dialog with the current settings.
 * @param {String} jsonData The contents of the settings file
 */
function loadReaderSetupData(jsonData) {

    if ((!jsonData) || (jsonData === '""')) return;

    // validate data
    var data = JSON.parse(jsonData);
    if (!data.letters) data.letters = '';
    if (!data.moreWords) data.moreWords = '';
    if (!data.stages) data.stages = [];
    if (!data.levels) data.levels = [];

    // language tab
    document.getElementById('dls_letters').value = data.letters;
    previousMoreWords = data.moreWords.replace(/ /g, '\n');
    document.getElementById('dls_more_words').value = previousMoreWords;

    // stages tab
    displayLetters();
    var stages = data.stages;
    var tbody = $('#stages-table').find('tbody');
    tbody.html('');

    for (var i = 0; i < stages.length; i++) {
        if (!stages[i].letters) stages[i].letters = '';
        if (!stages[i].sightWords) stages[i].sightWords = '';
        tbody.append('<tr class="linked"><td>' + (i + 1) + '</td><td class="book-font">' + stages[i].letters + '</td><td class="book-font">' + stages[i].sightWords + '</td></tr>');
    }

    // click event for stage rows
    tbody.find('tr').onOnce('click', function() {
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
    tbodyLevels.find('tr').onOnce('click', function() {
        selectLevel(this);
    });
}

/**
 * Update the fields when a different stage is selected
 * @param {HtmlElement} tr The selected table row element
 */
function selectStage(tr) {

    if (tr.classList.contains('selected')) return;

    var currentStage = tr.cells[0].innerHTML;
    document.getElementById('setup-stage-number').innerHTML = currentStage;
    document.getElementById('setup-remove-stage').innerHTML = localizationManager.getText('ReaderSetup.RemoveStage', 'Remove Stage {0}', currentStage);
    document.getElementById('setup-stage-sight-words').value = tr.cells[2].innerHTML;

    $('#stages-table').find('tbody tr.selected').removeClass('selected').addClass('linked');
    $(tr).removeClass('linked').addClass('selected');

    // get the words
    requestWordsForSelectedStage();
}

function requestWordsForSelectedStage() {

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
 * @param {HtmlElement} div
 */
function selectLetter(div) {

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
    $('#stages-table').find('tbody tr.selected td:nth-child(2)').html($.makeArray(letters).join(' '));

    requestWordsForSelectedStage();
}

/**
 * Creates the grid of available graphemes
 */
function displayLetters() {

    var letters = (document.getElementById('dls_letters').value.trim()).split(' ');
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
    var suffix = 's';
    if (letters.length > 42) suffix = 'l';

    var div = $('#setup-selected-letters');
    div.html('');
    div.removeClass('rs-letter-container-s').removeClass('rs-letter-container-l').addClass('rs-letter-container-' + suffix);

    for (var i = 0; i < letters.length; i++) {
        div.append($('<div class="book-font unselected-letter rs-letters rs-letters-' + suffix + '">' + letters[i] + '</div>'));
    }

    $('div.rs-letters').onOnce('click', function() {
        selectLetter(this);
    });
}

/**
 * Highlights the graphemes for the current stage
 * @param {HtmlElement} tr Table row
 */
function selectLetters(tr) {

    // remove current formatting
    var letters = $('.rs-letters').removeClass('current-letter').removeClass('previous-letter').addClass('unselected-letter');

    // letters in the current stage
    var stage_letters = tr.cells[1].innerHTML.split(' ');
    var current = letters.filter(function(index, element) {
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
 * Update the stage when the list of sight words changes
 * @param {HtmlElement} ta Text area
 */
function updateSightWords(ta) {
    var words = ta.value.trim().replace(/ ( )+/g, ' '); // remove consecutive spaces
    $('#stages-table').find('tbody tr.selected td:nth-child(3)').html(words);
}

function addNewStage() {

    var tbody = $('#stages-table').find('tbody');
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

function removeStage() {

    var tbody = $('#stages-table').find('tbody');

    // remove the current stage
    var current_row = tbody.find('tr.selected');
    var current_stage = current_row.find("td").eq(0).html();
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
}

function renumberRows(rows) {

    var rowNum = 1;

    $.each(rows, function() {
        this.cells[0].innerHTML = rowNum++;
    });
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

function displayWordsForSelectedStage(wordsStr) {

    var words = JSON.parse(wordsStr);
    words = _.toArray(words);

    // add sight words
    _.each(sightWords, function(sw) {

        var word = _.find(words, function(w) {
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

    var result = '';
    _.each(words, function(w) {

        if (!w.html)
            w.html = $.markupGraphemes(w.GPCForm, desiredGPCs);
        result += '<div class="book-font word">' + w.html + '</div>';
    });

    // set the list
    $('#rs-matching-words').html(result);

    // make columns
    $.divsToColumns('word');

    // display the count
    document.getElementById('setup-words-count').innerHTML = words.length;
}

function addNewLevel() {

    var tbody = $('#levels-table').find('tbody');
    tbody.append('<tr class="linked"><td>' + (tbody.children().length + 1) + '</td><td class="words-per-sentence">-</td><td class="words-per-page">-</td><td class="words-per-book">-</td><td class="unique-words-per-book">-</td><td style="display: none"></td></tr>');

    // click event for stage rows
    tbody.find('tr:last').onOnce('click', function() {
        selectLevel(this);
    });

    // go to the new stage
    tbody.find('tr:last').click();
}

function removeLevel() {

    var tbody = $('#levels-table').find('tbody');

    // remove the current stage
    var current_row = tbody.find('tr.selected');
    var current_stage = current_row.find("td").eq(0).html();
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
}

/**
 * Update display when a different level is selected
 * @param tr
 */
function selectLevel(tr) {

    if (tr.classList.contains('selected')) return;

    var currentLevel = tr.cells[0].innerHTML;
    document.getElementById('setup-level-number').innerHTML = currentLevel;
    document.getElementById('setup-remove-level').innerHTML = localizationManager.getText('ReaderSetup.RemoveLevel', 'Remove Level {0}', currentLevel);

    $('#levels-table').find('tbody tr.selected').removeClass('selected').addClass('linked');
    $(tr).removeClass('linked').addClass('selected');

    var sentence = tr.cells[1].innerHTML;
    var page = tr.cells[2].innerHTML;
    var book = tr.cells[3].innerHTML;
    var unique = tr.cells[4].innerHTML;

    // check boxes and text boxes
    setLevelCheckBoxValue('words-per-sentence', tr.cells[1].innerHTML);
    setLevelCheckBoxValue('words-per-page', tr.cells[2].innerHTML);
    setLevelCheckBoxValue('words-per-book', tr.cells[3].innerHTML);
    setLevelCheckBoxValue('unique-words-per-book', tr.cells[4].innerHTML);

    // things to remember
    var vals = tr.cells[5].innerHTML.split('\n');
    var val = vals.join('</li><li contenteditable="true">');
    document.getElementById('things-to-remember').innerHTML = '<li contenteditable="true">' + val + '</li>';
}

function setLevelCheckBoxValue(id, value) {

    var checked = value !== '-';
    document.getElementById('use-' + id).checked = checked;

    var txt = document.getElementById('max-' + id);
    txt.value = value === '-' ? '' : value;
    txt.disabled = !checked;
}

/**
 * Handles special keys in the Things to Remember list, which is a "ul" element
 * @param jqueryEvent
 */
function handleThingsToRemember(jqueryEvent) {

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
    }

}

/**
 * Converts the items of the "ul" element to a string and stores it in the levels table
 */
function storeThingsToRemember() {

    var val = document.getElementById('things-to-remember').innerHTML.trim();

    // remove html and split into array
    var vals = val.replace(/<li contenteditable="true">/g, '').replace(/<br>/g, '').split('</li>');

    // remove blank lines
    vals = vals.filter(function(e){ var x = e.trim(); return (x.length > 0 && x !== '&nbsp;'); });

    // store
    $('#levels-table').find('tbody tr.selected td:nth-child(6)').html(vals.join('\n'));
}

//noinspection JSUnusedGlobalSymbols
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
if (typeof ($) === "function") {

    $("#open-text-folder").onOnce('click', function() {
        fireCSharpSetupEvent('openTextsFolderEvent', 'open');
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
        var txtBox = document.getElementById('max-' + id);
        txtBox.disabled = !this.checked;
        $('#levels-table').find('tbody tr.selected td.' + id).html(this.checked ? txtBox.value : '');
    });

    levelDetail.find('.level-textbox').onOnce('keyup', function() {
        var id = this.id.replace(/^max-/, '');
        $('#levels-table').find('tbody tr.selected td.' + id).html(this.value);
    });
}

/**
 * Called after localized strings are loaded.
 */
function finishInitializing() {
    $('#stages-table').find('tbody').sortable({ stop: updateStageNumbers });
    $('#levels-table').find('tbody').sortable({ stop: updateLevelNumbers });
    accordionWindow().postMessage('Texts', '*');
}

function tabBeforeActivate(ui) {

    var panelId = ui['newPanel'][0].id;

    if (panelId === 'dlstabs-2') { // Decodable Stages tab

        var allLetters = (document.getElementById('dls_letters').value.trim()).split(' ');
        var tbody = $('#stages-table').find('tbody');

        // update letters grid
        displayLetters();

        // update letters in stages
        var rows = tbody.find('tr');
        rows.each(function() {

            // get the letters for this stage
            var letters = this.cells[1].innerHTML.split(' ');

            // make sure each letter for this stage is all in the allLetters list
            letters = _.intersection(letters, allLetters);
            this.cells[1].innerHTML = Array.join(letters, ' ');
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
            saveChangedSettings(function() {
                if (typeof accordion['readerSampleFilesChanged'] === 'function')
                    accordion['readerSampleFilesChanged']();
            });
        }
    }
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
    $('body').find('*[data-i18n]').localize(finishInitializing);
    var accordion = accordionWindow();
    accordion.addWordListChangedListener('wordListChanged.ReaderSetup', wordListChangedCallback);
});