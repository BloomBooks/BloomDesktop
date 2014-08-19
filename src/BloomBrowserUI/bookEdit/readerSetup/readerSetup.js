// listen for messages sent to this iframe
window.addEventListener('message', processMessage, false);

var desiredGPCs;
var previousGPCs;
var sightWords;
var currentSightWords;

function accordionWindow() {
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
            document.getElementById('dls_word_lists').value = params[1].replace(/\r/g, '\n');
            return;

        case 'Words':
            displayWordsForSelectedStage(params[1]);
            return;

        case 'SetupType':
            var tabs = $('#dlstabs');
            if (params[1] === 'stages') {
                tabs.tabs('option', 'disabled', [2]);
                tabs.tabs('option', 'active', 1);
                var firstStage = $('#stages-table').find('tbody tr:first');
                if (firstStage && (firstStage.length === 0))
                    addNewStage();
                else
                    firstStage.click(); // select the first stage
            }
            else {
                tabs.tabs('option', 'disabled', [0, 1]);
                tabs.tabs('option', 'active', 2);
                var firstLevel = $('#levels-table').find('tbody tr:first');
                if (firstLevel && (firstLevel.length === 0))
                    addNewLevel();
                else
                    firstLevel.click(); // select the first level
            }

            return;

        case 'Font':
            var style = document.createElement('style');
            style.type = 'text/css';
            style.innerHTML = '.book-font { font-family: ' + params[1] + '; }';
            document.getElementsByTagName('head')[0].appendChild(style);
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

/**
 * Pass the settings to C# to be saved.
 */
function saveClicked() {

    // get the values
    var s = {};
    s.letters = document.getElementById('dls_letters').value.trim();
    s.letterCombinations = document.getElementById('dls_letter_combinations').value.trim();

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

    // send to parent
    var settingsStr = JSON.stringify(s);
    accordionWindow().postMessage('Refresh\n' + settingsStr, '*');

    // save now
    simpleAjaxPost('/bloom/readers/saveReaderToolSettings', parent.window.closeSetupDialog, settingsStr);
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

    if (!jsonData) return;

    // validate data
    var data = JSON.parse(jsonData);
    if (!data.letters) data.letters = '';
    if (!data.letterCombinations) data.letterCombinations = '';
    if (!data.moreWords) data.moreWords = '';
    if (!data.stages) data.stages = [];
    if (!data.levels) data.levels = [];


    // language tab
    document.getElementById('dls_letters').value = data.letters;
    document.getElementById('dls_letter_combinations').value = data.letterCombinations;
    document.getElementById('dls_more_words').value = data.moreWords.replace(/ /g, '\n');

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
    document.getElementById('remove-stage-number').innerHTML = currentStage;
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

    var letters = (document.getElementById('dls_letters').value.trim() + ' ' + document.getElementById('dls_letter_combinations').value.trim()).split(' ');
    letters = letters.filter(function(n){ return n !== ''; });

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
    updateNumbers('stages-table', 'setup-stage-number', 'remove-stage-number');
}

/**
 * Called to update the level numbers on the screen after rows are reordered.
 */
function updateLevelNumbers() {
    updateNumbers('levels-table', 'setup-level-number', 'remove-level-number');
}

function updateNumbers(tableId, setupNumberId, removeNumberId) {
    var tbody = $('#' + tableId).find('tbody');
    var rows = tbody.find('tr');
    renumberRows(rows);

    var currentStage = tbody.find('tr.selected td:nth-child(1)').html();
    $('#' + setupNumberId).innerHTML = currentStage;
    $('#' + removeNumberId).innerHTML = currentStage;
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

    var currentStage = tr.cells[0].innerHTML;
    document.getElementById('setup-level-number').innerHTML = currentStage;
    document.getElementById('remove-level-number').innerHTML = currentStage;

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
 * Retrieve data from localhost
 * @param {String} url The URL to request
 * @param {Function} callback Function to call when the ajax request returns
 * @param {String} [dataValue] Passed in the query string under the "data" key
 */
function simpleAjaxPost(url, callback, dataValue) {

    var ajaxSettings = {type: 'POST', url: url};
    if (dataValue) ajaxSettings.data = {data: dataValue};

    $.ajax(ajaxSettings)
        .done(function (data) {
            callback(data);
        });
}

$(document).ready(function () {
    accordionWindow().postMessage('Texts', '*');
    $('#stages-table').find('tbody').sortable({ stop: updateStageNumbers });
    $('#levels-table').find('tbody').sortable({ stop: updateLevelNumbers });
});