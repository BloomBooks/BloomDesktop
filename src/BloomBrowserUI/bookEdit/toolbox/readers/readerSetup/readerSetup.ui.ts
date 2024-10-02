/// <reference path="readerSetup.io.ts" />
/// <reference path="../../../../lib/jquery.onSafe.d.ts" />
import theOneLocalizationManager from "../../../../lib/localizationManager/localizationManager";
import "../../../../lib/jquery.onSafe.ts";
import {
    beginSaveChangedSettings,
    cleanSpaceDelimitedList,
    toolboxWindow,
    setPreviousMoreWords,
    getPreviousMoreWords,
    levelSettings,
    spanForSettingWithText
} from "./readerSetup.io";
import { DataWord } from "../libSynphony/bloomSynphonyExtensions";
import axios from "axios";
import { get, post } from "../../../../utils/bloomApi";
import * as _ from "underscore";

let desiredGPCs: string[];
let previousGPCs: string[];
let sightWords: string[];
let currentSightWords: string[];

window.addEventListener("message", process_UI_Message, false);

function process_UI_Message(event: MessageEvent): void {
    const params: string[] = event.data.split("\n");

    switch (params[0]) {
        case "Files": {
            let s: string = params[1];
            if (s.length > 0) {
                const files: string[] = s.split("\r");
                const extensions: string[] = ["txt", "js", "json"]; // reviewSlog ReaderToolsModel.getReadableFileExtensions(); but do NOT want to import that here; it should only be in toolbox iframe
                const needsTxtExtension: string = getInnerHtmlSafely(
                    "needs_txt_extension"
                );
                const notSupported: string = getInnerHtmlSafely(
                    "format_not_supported"
                );
                let foundNotSupported: boolean = false;
                files.forEach((element, index, array) => {
                    const filenameComponents: string[] = element.split(".");
                    if (filenameComponents.length < 2) {
                        array[index] =
                            element +
                            " " +
                            '<span class="format-not-supported">' +
                            needsTxtExtension +
                            "</span>";
                        foundNotSupported = true;
                    } else {
                        const ext:
                            | string
                            | undefined = filenameComponents.pop();
                        if (!ext || extensions.indexOf(ext) === -1) {
                            array[index] =
                                element +
                                " " +
                                '<span class="format-not-supported">' +
                                notSupported +
                                "</span>";
                            foundNotSupported = true;
                        }
                    }
                });
                s = files.join("\r");

                setElementDisplay("how_to_export", !foundNotSupported);
            }

            const fileList: string =
                s || getInnerHtmlSafely("please-add-texts");

            (<HTMLElement>(
                document.getElementById("dls_word_lists")
            )).innerHTML = fileList.replace(/\r/g, "<br>");
            return;
        }

        case "UpdateWordsDisplay": {
            const useSampleWords =
                $('input[name="words-or-letters"]:checked').val() === "1";
            if (useSampleWords) displayAllowedWordsForSelectedStage(params[1]);
            else displayWordsForSelectedStage(params[1]);
            return;
        }

        case "ConfigureActiveTab": {
            //noinspection JSJQueryEfficiency
            const tabs: JQuery = $("#dlstabs");
            if (params[1] === "stages") {
                tabs.tabs("option", "disabled", [3, 4]);
                tabs.tabs("option", "active", 2);
                const firstStage = $("#stages-table").find("tbody tr:first");
                if (firstStage && firstStage.length === 0) addNewStage();
                else firstStage.click(); // select the first stage
            } else {
                tabs.tabs("option", "disabled", [0, 1, 2]);
                tabs.tabs("option", "active", 4);
                const firstLevel = $("#levels-table").find("tbody tr:first");
                if (firstLevel && firstLevel.length === 0) addNewLevel();
                else firstLevel.click(); // select the first level
            }

            // handle the beforeActivate event
            tabs.on("tabsbeforeactivate", (event, ui) => {
                tabBeforeActivate(ui);
            });

            return;
        }

        case "Font": {
            const style: HTMLStyleElement = document.createElement("style");
            style.type = "text/css";
            style.innerHTML = ".book-font { font-family: " + params[1] + "; }";
            document.getElementsByTagName("head")[0].appendChild(style);
            return;
        }

        case "Help": {
            let helpFile: string = "";
            //noinspection JSJQueryEfficiency
            switch ($("#dlstabs").tabs("option", "active")) {
                case 0:
                    helpFile =
                        "Tasks/Edit_tasks/Decodable_Reader_Tool/Letters_tab.htm";
                    break;
                case 1:
                    helpFile =
                        "Tasks/Edit_tasks/Decodable_Reader_Tool/Words_tab.htm";
                    break;
                case 2:
                    helpFile =
                        "Tasks/Edit_tasks/Decodable_Reader_Tool/Decodable_Stages_tab.htm";
                    break;
                case 3:
                    helpFile =
                        "Tasks/Edit_tasks/Leveled_Reader_Tool/Punctuation_tab.htm";
                    break;
                case 4:
                    helpFile =
                        "Tasks/Edit_tasks/Leveled_Reader_Tool/Reader_Levels_tab.htm";
                    break;
                default:
            }
            if (helpFile) post("help?topic=" + helpFile);
            return;
        }

        default:
    }
}

function getInnerHtmlSafely(elementName: string): string {
    const element = document.getElementById(elementName);
    return element ? element.innerHTML : "";
}

/**
 * Creates the grid of available graphemes
 */
export function displayLetters(): void {
    let letters: string[] = cleanSpaceDelimitedList(
        (<HTMLInputElement>document.getElementById("dls_letters")).value.trim()
    ).split(" ");
    letters = letters.filter(n => {
        return n !== "";
    });

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
    let suffix: string = "s";
    if (letters.length > 42) suffix = "l";

    const div: JQuery = $("#setup-selected-letters");
    div.html("");
    div.removeClass("rs-letter-container-s")
        .removeClass("rs-letter-container-l")
        .addClass("rs-letter-container-" + suffix);

    for (let i = 0; i < letters.length; i++) {
        div.append(
            $(
                `<div class="lang1InATool book-font unselected-letter rs-letters rs-letters-${suffix}">${letters[i]}</div>`
            )
        );
    }

    $("div.rs-letters").onSafe("click", function() {
        selectLetter(this);
    });
}

export function setLevelValue(value: any): string {
    if (!value) return "-";

    const testVal: number = typeof value === "number" ? value : parseInt(value);

    if (testVal === 0) return "-";

    return testVal.toString();
}

/**
 * Update the fields when a different stage is selected
 * @param tr The selected table row element
 */
export function selectStage(tr: HTMLTableRowElement): void {
    if (tr.classList.contains("selected")) return;

    const currentStage = (<HTMLTableCellElement>tr.cells[0]).innerHTML;
    (<HTMLElement>(
        document.getElementById("setup-stage-number")
    )).innerHTML = currentStage;
    (<HTMLElement>(
        document.getElementById("setup-remove-stage")
    )).innerHTML = theOneLocalizationManager.getText(
        "ReaderSetup.RemoveStage",
        "Remove Stage {0}",
        currentStage
    );
    (<HTMLInputElement>(
        document.getElementById("setup-stage-sight-words")
    )).value = (<HTMLTableCellElement>tr.cells[2]).innerHTML;

    $("#stages-table")
        .find("tbody tr.selected")
        .removeClass("selected")
        .addClass("linked");

    const currentTr = $(tr);
    currentTr.removeClass("linked").addClass("selected");

    setAllowedWordsFile(currentTr.find("td:nth-child(4)").html());

    // get the words
    requestWordsForSelectedStage();
}

function requestWordsForSelectedStage(): void {
    const tr = <HTMLTableRowElement>$("#stages-table")
        .find("tbody tr.selected")
        .get(0);

    desiredGPCs = (<HTMLTableCellElement>tr.cells[1]).innerHTML.split(" ");
    previousGPCs = $.makeArray(
        $(tr)
            .prevAll()
            .map(function() {
                return (<HTMLTableCellElement>this.cells[1]).innerHTML.split(
                    " "
                );
            })
    );

    const knownGPCS = previousGPCs.join(" ") + " " + desiredGPCs.join(" ");
    currentSightWords = (<HTMLTableCellElement>tr.cells[2]).innerHTML.split(
        " "
    );
    sightWords = $.makeArray(
        $(tr)
            .prevAll()
            .map(function() {
                return (<HTMLTableCellElement>this.cells[2]).innerHTML.split(
                    " "
                );
            })
    );

    sightWords = _.union(sightWords, currentSightWords);

    // remove empty items
    sightWords = _.compact(sightWords);

    const useSampleWords =
        $('input[name="words-or-letters"]:checked').val() === "1";

    const win = toolboxWindow();
    if (!win) return;

    // FYI: This is supposed to invoke the Words handler in readerTools.ts
    if (useSampleWords)
        win.postMessage(
            "Words\n" + (<HTMLTableCellElement>tr.cells[0]).innerHTML,
            "*"
        );
    else win.postMessage("Words\n" + knownGPCS, "*");
}

/**
 * Update the stage when a letter is selected or unselected.
 * @param  div
 */
function selectLetter(div: HTMLDivElement): void {
    const tr: JQuery = $("#stages-table").find("tbody tr.selected");

    // do not do anything if there is no selected stage
    if (tr.length === 0) return;

    // update the css classes
    if (div.classList.contains("unselected-letter"))
        $(div)
            .removeClass("unselected-letter")
            .addClass("current-letter");
    else if (div.classList.contains("current-letter"))
        $(div)
            .removeClass("current-letter")
            .addClass("unselected-letter");
    else return;

    // update the stages table
    const letters = $(".current-letter").map(function() {
        return this.innerHTML;
    });
    tr.find("td:nth-child(2)").html($.makeArray(letters).join(" "));

    requestWordsForSelectedStage();
}

/**
 * Highlights the graphemes for the current stage
 * @param tr Table row
 */
export function selectLetters(tr: HTMLTableRowElement) {
    // remove current formatting
    const letters: JQuery = $(".rs-letters")
        .removeClass("current-letter")
        .removeClass("previous-letter")
        .addClass("unselected-letter");

    // letters in the current stage
    let stage_letters: string[] = (<HTMLTableCellElement>(
        tr.cells[1]
    )).innerHTML.split(" ");
    const current: JQuery = letters.filter((index, element) => {
        return stage_letters.indexOf((<HTMLElement>element).innerHTML) > -1;
    });

    // letters in previous stages
    stage_letters = $.makeArray(
        $(tr)
            .prevAll()
            .map(function() {
                return (<HTMLTableCellElement>this.cells[1]).innerHTML.split(
                    " "
                );
            })
    );
    const previous = letters.filter((index, element) => {
        return stage_letters.indexOf((<HTMLElement>element).innerHTML) > -1;
    });

    // show current and previous letters
    if (current.length > 0)
        current.removeClass("unselected-letter").addClass("current-letter");
    if (previous.length > 0)
        previous.removeClass("unselected-letter").addClass("previous-letter");
}

// Set the label at the top of the detail pane that shows the current level.
// Tricky because we may see the original string which is (or is a translation of)
// "Level {0} maximums", or we may see the result of a previous call to this
// method, which replaces {0} with a span. A regular expression matches one
// or the other, and it gets replaced with the current level.
function setLevelLabel(level: string) {
    const labelElt = <HTMLElement>document.getElementById("setup-level-label");
    let innerHtml = labelElt.innerHTML;
    innerHtml = innerHtml.replace(
        /\{0\}|<span[^>]*>\d*<\/span>/,
        `<span class="rs-subheading">${level}</span>`
    );
    labelElt.innerHTML = innerHtml;
}

/**
 * Update display when a different level is selected
 * @param tr
 */
export function selectLevel(tr: HTMLTableRowElement) {
    if (tr.classList.contains("selected")) return;

    const currentLevel = getCellInnerHTML(tr, 0);
    setLevelLabel(currentLevel);
    (<HTMLElement>(
        document.getElementById("setup-remove-level")
    )).innerHTML = theOneLocalizationManager.getText(
        "ReaderSetup.RemoveLevel",
        "Remove Level {0}",
        currentLevel
    );

    $("#levels-table")
        .find("tbody tr.selected")
        .removeClass("selected")
        .addClass("linked");
    $(tr)
        .removeClass("linked")
        .addClass("selected");

    // check boxes and text boxes
    for (let i = 0; i < levelSettings.length; i++) {
        setLevelCheckBoxValue(
            levelSettings[i].cellClass,
            getSpanContent(tr, levelSettings[i].cellClass)
        );
        if (levelSettings[i].subcell) {
            setLevelCheckBoxValue(
                levelSettings[i].subcell!.cellClass,
                getSpanContent(tr, levelSettings[i].subcell!.cellClass)
            );
        }
    }

    // things to remember
    const vals = getCellInnerHTML(tr, levelSettings.length + 1).split("\n");
    const val = vals.join('</li><li contenteditable="true">');
    const thingsToRemember = <HTMLElement>(
        document.getElementById("things-to-remember")
    );
    thingsToRemember.innerHTML = '<li contenteditable="true">' + val + "</li>";

    // Add event handlers to do plain text paste to all the children
    // present at this time (that is, when the level is selected)
    // If any children are added after this point (e.g. if the user hits carriage return a few times),
    // it is that code's responsibility to add the event handler too.
    forcePlainTextPaste(thingsToRemember);
}

// prevent pasting anything but plain text into the contenteditable children of the argument
export function forcePlainTextPaste(parent: ParentNode): void {
    [].forEach.call(parent.querySelectorAll('[contenteditable="true"]'), el => {
        addForcePlainTextPasteHandler(el);
    });
}

function addForcePlainTextPasteHandler(element: HTMLElement) {
    // Note: if you use an anonymous handler, you can easily get duplicate listeners.
    // We use a named function here instead.
    element.addEventListener("paste", forcePlainTextPasteHandler, false);
}

function forcePlainTextPasteHandler(e) {
    e.preventDefault();
    const text = e.clipboardData.getData("text/plain");
    document.execCommand("insertHTML", false, text);
}

function getCellInnerHTML(tr: HTMLTableRowElement, cellIndex: number): string {
    return (<HTMLTableCellElement>tr.cells[cellIndex]).innerHTML;
}

// Get the content of the child of the row that has the specified class.
// Throws if not found.
function getSpanContent(tr: HTMLTableRowElement, childClass: string): string {
    const child = tr.getElementsByClassName(childClass)[0];
    if (!child) {
        throw new Error(
            "element with class " + childClass + " was unexpectedly not found"
        );
    }
    return child.textContent || "";
}

function setLevelCheckBoxValue(id: string, value: string): void {
    // Empty values (in subcells) and hyphens both indicate that no value
    // has been set for this setting, in which case the check box should
    // be off and the text box disabled.
    const checked: boolean = !!value && value !== "-";
    (<HTMLInputElement>document.getElementById("use-" + id)).checked = checked;

    const txt: HTMLInputElement = <HTMLInputElement>(
        document.getElementById("max-" + id)
    );
    txt.value = value === "-" ? "" : value;
    txt.disabled = !checked;
}

function displayAllowedWordsForSelectedStage(wordsStr: string): void {
    const wordList = <HTMLElement>document.getElementById("rs-matching-words");
    wordList.innerHTML = "";

    const wordsObj: Object = JSON.parse(wordsStr);
    const words: string[] = <string[]>_.toArray(wordsObj);

    let result: string = "";
    let longestWord: string = "";
    let longestWordLength: number = 0;

    _.each(words, (w: string) => {
        result += '<div class="book-font lang1InATool word">' + w + "</div>";

        if (w.length > longestWordLength) {
            longestWord = w;
            longestWordLength = longestWord.length;
        }
    });

    // set the list
    wordList.innerHTML = result;

    // make columns
    $.divsToColumnsBasedOnLongestWord("word", longestWord);

    // display the count
    (<HTMLElement>(
        document.getElementById("setup-words-count")
    )).innerHTML = words.length.toString();
}

function displayWordsForSelectedStage(wordsStr: string): void {
    const wordList = <HTMLElement>document.getElementById("rs-matching-words");
    wordList.innerHTML = "";

    const wordsObj: Object = JSON.parse(wordsStr);
    let words: DataWord[] = <DataWord[]>_.toArray(wordsObj);

    // add sight words
    _.each(sightWords, (sw: string) => {
        let word: DataWord | undefined = _.find(words, (w: DataWord) => {
            return w.Name === sw;
        });

        if (typeof word === "undefined") {
            word = new DataWord(sw);

            if (_.contains(currentSightWords, sw)) {
                word.html =
                    '<span class="lang1InATool sight-word current-sight-word">' +
                    sw +
                    "</span>";
            } else {
                word.html =
                    '<span class="lang1InATool sight-word">' + sw + "</span>";
            }
            words.push(word);
        }
    });

    // sort the list
    words = _.sortBy(words, w => {
        return w.Name;
    });

    let result: string = "";
    let longestWord: string = "";
    let longestWordLength: number = 0;

    _.each(words, (w: DataWord) => {
        if (!w.html) w.html = $.markupGraphemes(w.Name, w.GPCForm, desiredGPCs);
        result +=
            '<div class="book-font lang1InATool word">' + w.html + "</div>";

        if (w.Name.length > longestWordLength) {
            longestWord = w.Name;
            longestWordLength = longestWord.length;
        }
    });

    // set the list
    wordList.innerHTML = result;

    // make columns
    $.divsToColumnsBasedOnLongestWord("word", longestWord);

    // display the count
    (<HTMLElement>(
        document.getElementById("setup-words-count")
    )).innerHTML = words.length.toString();
}

function addNewStage(): void {
    const tbody: JQuery = $("#stages-table").find("tbody");
    tbody.append(
        '<tr class="linked"><td>' +
            (tbody.children().length + 1) +
            '</td><td class="book-font"></td><td class="book-font"></td><td class="book-font"></td></tr>'
    );

    // click event for stage rows
    tbody.find("tr:last").onSafe("click", function() {
        selectStage(this);
        displayLetters();
        selectLetters(this);
    });

    // go to the new stage
    tbody.find("tr:last").click();
}

function addNewLevel(): void {
    const tbody: JQuery = $("#levels-table").find("tbody");
    tbody.append(
        '<tr class="linked"><td>' +
            (tbody.children().length + 1) +
            "</td>" +
            levelSettings
                .map(s => {
                    const subcell = s.subcell
                        ? spanForSettingWithText(s.subcell, "-", true)
                        : "";
                    return `<td>${spanForSettingWithText(
                        s,
                        "-",
                        false
                    )}${subcell}</td>`;
                })
                .join("") +
            '<td style="display: none"></td></tr>'
    );

    // click event for stage rows
    tbody.find("tr:last").onSafe("click", function() {
        selectLevel(this);
    });

    // go to the new stage
    tbody.find("tr:last").click();
}

function tabBeforeActivate(ui): void {
    const toolId: string = ui["newPanel"][0].id;

    if (toolId === "dlstabs-2") {
        // Decodable Stages tab

        const allLetters: string[] = cleanSpaceDelimitedList(
            (<HTMLInputElement>(
                document.getElementById("dls_letters")
            )).value.trim()
        ).split(" ");
        const tbody: JQuery = $("#stages-table").find("tbody");

        // update letters grid
        displayLetters();

        // update letters in stages
        const rows: JQuery = tbody.find("tr");
        rows.each(function() {
            // get the letters for this stage
            let letters = (<HTMLTableCellElement>this.cells[1]).innerHTML.split(
                " "
            );

            // make sure each letter for this stage is all in the allLetters list
            letters = _.intersection(letters, allLetters);
            (<HTMLTableCellElement>this.cells[1]).innerHTML = letters.join(" ");
        });

        // select letters for current stage
        const tr = tbody.find("tr.selected");
        if (tr.length === 1) {
            selectLetters(<HTMLTableRowElement>tr[0]);
        }

        // update more words
        const moreWords = (<HTMLInputElement>(
            document.getElementById("dls_more_words")
        )).value;
        if (moreWords !== getPreviousMoreWords()) {
            // save the changes and update lists
            const toolbox = toolboxWindow();
            // Note, this means that changes to sample words (and any other changes we already made) will persist,
            // even if the user eventually cancels the dialog. Not sure if this is desirable. However, if we
            // want updated matching words in the other tab, it will be difficult to achieve without doing this.
            // We'd probably need our own copy of theOneLanguageDataInstance.
            beginSaveChangedSettings().then(() => {
                // remember the new list of more words, but don't do it before we save changed settings because
                // that uses the old value of 'more words' to know to reset Synphony's data.
                setPreviousMoreWords(moreWords);
                requestWordsForSelectedStage();
            });
        }
    }
}

/**
 * Handles special keys in the Things to Remember list, which is a "ul" element
 * @param jqueryEvent
 */
function handleThingsToRemember(jqueryEvent: JQueryEventObject): void {
    switch (jqueryEvent.which) {
        case 13: {
            // carriage return - add new li
            const x = $('<li contenteditable="true"></li>').insertAfter(
                jqueryEvent.target
            );
            jqueryEvent.preventDefault();
            addForcePlainTextPasteHandler(x[0]);
            x.focus();
            break;
        }

        case 38: {
            // up arrow
            const prev = $(jqueryEvent.target).prev();
            if (prev.length) prev.focus();
            break;
        }

        case 40: {
            // down arrow
            const next = $(jqueryEvent.target).next();
            if (next.length) next.focus();
            break;
        }

        case 8: {
            // backspace
            const thisItem = $(jqueryEvent.target);

            // if the item is not blank, return
            if (thisItem.text().length > 0) return;

            // cannot remove the last item
            let otherItem = thisItem.prev();
            if (!otherItem.length) otherItem = thisItem.next();
            if (!otherItem.length) return;

            // OK to remove the item
            thisItem.remove();
            otherItem.focus();
            break;
        }

        default:
    }
}

/**
 * Update the stage when the list of sight words changes
 * @param ta Text area
 */
function updateSightWords(ta: HTMLInputElement): void {
    const words: string = cleanSpaceDelimitedList(ta.value);
    $("#stages-table")
        .find("tbody tr.selected td:nth-child(3)")
        .html(words);
}

function removeStage(): void {
    const tbody: JQuery = $("#stages-table").find("tbody");

    // remove the current stage
    const current_row: JQuery = tbody.find("tr.selected");
    const current_stage: number = parseInt(
        current_row
            .find("td")
            .eq(0)
            .html()
    );

    // remember for the next step
    const allowedWordsFile = current_row
        .find("td")
        .eq(3)
        .html();

    current_row.remove();

    // if there is an Allowed Words file, remove it also
    if (allowedWordsFile.length > 0)
        checkAndDeleteAllowedWordsFile(allowedWordsFile);

    const rows: JQuery = tbody.find("tr");

    if (rows.length > 0) {
        // renumber remaining stages
        renumberRows(rows);

        // select a different stage
        if (rows.length >= current_stage)
            tbody.find("tr:nth-child(" + current_stage + ")").click();
        else tbody.find("tr:nth-child(" + rows.length + ")").click();
    } else {
        resetStageDetail();
    }
}

function resetStageDetail(): void {
    (<HTMLElement>document.getElementById("setup-words-count")).innerHTML = "0";
    (<HTMLElement>document.getElementById("rs-matching-words")).innerHTML = "";
    (<HTMLInputElement>(
        document.getElementById("setup-stage-sight-words")
    )).value = "";
    $(".rs-letters")
        .removeClass("current-letter")
        .removeClass("previous-letter")
        .addClass("unselected-letter");
}

function renumberRows(rows: JQuery): void {
    let rowNum = 1;

    $.each(rows, function() {
        (<HTMLTableCellElement>this.cells[0]).innerHTML = (rowNum++).toString();
    });
}

function removeLevel(): void {
    const tbody: JQuery = $("#levels-table").find("tbody");

    // remove the current level
    const current_row: JQuery = tbody.find("tr.selected");
    const current_stage: number = parseInt(
        current_row
            .find("td")
            .eq(0)
            .html()
    );
    current_row.remove();

    const rows = tbody.find("tr");

    if (rows.length > 0) {
        // renumber remaining levels
        renumberRows(rows);

        // select a different stage
        if (rows.length >= current_stage)
            tbody.find("tr:nth-child(" + current_stage + ")").click();
        else tbody.find("tr:nth-child(" + rows.length + ")").click();
    } else {
        resetLevelDetail();
    }
}

function resetLevelDetail(): void {
    setLevelLabel("0");

    for (let i = 0; i < levelSettings.length; i++) {
        setLevelCheckBoxValue(levelSettings[i].cellClass, "-");
        if (levelSettings[i].subcell) {
            setLevelCheckBoxValue(levelSettings[i].subcell!.cellClass, "-");
        }
    }
    (<HTMLElement>document.getElementById("things-to-remember")).innerHTML =
        '<li contenteditable="true"></li>';
}

/**
 * Converts the items of the "ul" element to a string and stores it in the levels table
 */
function storeThingsToRemember(): void {
    const val: string = getInnerHtmlSafely("things-to-remember").trim();

    // remove html and split into array
    let vals: string[] = val
        .replace(/<li contenteditable="true">/g, "")
        .replace(/<br>/g, "")
        .split("</li>");

    // remove blank lines
    vals = vals.filter(e => {
        const x = e.trim();
        return x.length > 0 && x !== "&nbsp;";
    });

    // store
    $("#levels-table")
        .find(`tbody tr.selected td:nth-child(${levelSettings.length + 2})`)
        .html(vals.join("\n"));
}

function updateNumbers(tableId: string): void {
    const tbody: JQuery = $("#" + tableId).find("tbody");
    const rows = tbody.find("tr");
    renumberRows(rows);

    const currentStage = tbody.find("tr.selected td:nth-child(1)").html();

    if (tableId === "levels-table") {
        setLevelLabel(currentStage);
        (<HTMLElement>(
            document.getElementById("setup-remove-level")
        )).innerHTML = theOneLocalizationManager.getText(
            "ReaderSetup.RemoveLevel",
            "Remove Level {0}",
            currentStage
        );
    } else {
        (<HTMLElement>(
            document.getElementById("setup-stage-number")
        )).innerHTML = currentStage;
        (<HTMLElement>(
            document.getElementById("setup-remove-stage")
        )).innerHTML = theOneLocalizationManager.getText(
            "ReaderSetup.RemoveStage",
            "Remove Stage {0}",
            currentStage
        );
    }
}

/**
 * Called to update the stage numbers on the screen after rows are reordered.
 */
function updateStageNumbers() {
    updateNumbers("stages-table");
}

/**
 * Called to update the level numbers on the screen after rows are reordered.
 */
function updateLevelNumbers() {
    updateNumbers("levels-table");
}

function firstSetupLetters(): boolean {
    $("#dlstabs").tabs("option", "active", 0);
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
    if (typeof $ === "function") {
        $("#open-text-folder").onSafe("click", () => {
            post("readers/ui/openTextsFolder");
            return false;
        });

        $("#setup-add-stage").onSafe("click", () => {
            addNewStage();
            return false;
        });

        $("#setup-stage-sight-words").onSafe("keyup", function() {
            updateSightWords(this);
            requestWordsForSelectedStage();
        });

        $("#setup-remove-stage").onSafe("click", () => {
            removeStage();
            return false;
        });

        $("#setup-add-level").onSafe("click", () => {
            addNewLevel();
            return false;
        });

        $("#setup-remove-level").onSafe("click", () => {
            removeLevel();
            return false;
        });

        const toRemember = $("#things-to-remember");
        toRemember.onSafe("keydown", handleThingsToRemember);
        toRemember.onSafe("keyup", storeThingsToRemember);

        const levelDetail = $("#level-detail");
        levelDetail.find(".level-checkbox").onSafe("change", function() {
            const mainTableSpanClass = this.id.replace(/^use-/, "");
            const txtBox: HTMLInputElement = <HTMLInputElement>(
                document.getElementById("max-" + mainTableSpanClass)
            );
            txtBox.disabled = !this.checked;

            updateMainTableWithValue(
                mainTableSpanClass,
                this.checked ? txtBox.value : "-"
            );
        });

        levelDetail.find(".level-textbox").onSafe("keyup", function() {
            // By design, the id of the .level-textbox element (in the detail pane) is
            // always "max-" appended to the class used to mark the corresponding elements
            // (one per row, so we can't use ID there) in the main table.
            const mainTableSpanClass = this.id.replace(/^max-/, "");
            updateMainTableWithValue(mainTableSpanClass, this.value);
        });

        $('input[name="words-or-letters"]').onSafe("change", () => {
            enableSampleWords();
        });

        $("#setup-choose-allowed-words-file").onSafe("click", () => {
            get("readers/ui/chooseAllowedWordsListFile", result => {
                const fileName = result.data;
                if (fileName) setAllowedWordsFile(fileName);

                // hide stale controls
                $("#setup-stage-matching-words")
                    .find("div")
                    .hide();
            });
            return false;
        });

        $("#remove-allowed-word-file").onSafe("click", () => {
            setAllowedWordsFile("");

            // hide stale controls
            $("#setup-stage-matching-words")
                .find("div")
                .hide();

            return false;
        });

        const allowedDiv = $("#allowed-words-file-div");
        allowedDiv.onSafe("mouseenter", function() {
            const title = getInnerHtmlSafely("remove_word_list");
            const anchor = $(this).find("a");
            anchor.attr("title", title);
            anchor.show();
        });

        allowedDiv.onSafe("mouseleave", function() {
            $(this)
                .find("a")
                .hide();
        });
    }
}

function updateMainTableWithValue(mainTableSpanClass: string, newVal: string) {
    const mainTableSpan = getMainTableSpan(mainTableSpanClass);

    // The logic here is designed to maintain the same cell content
    // as is originally produced by spanForSettingWithText() in readerSetup.io.ts.
    const isSubCell = !!mainTableSpan.previousSibling;
    if (isSubCell) {
        // maintain the expectation that subcell values are surrounded
        // by parentheses, unless we don't have a value, when nothing
        // at all is shown.
        const parent = mainTableSpan.parentElement!;
        if (newVal && !mainTableSpan.innerText) {
            // add parens
            parent.insertBefore(document.createTextNode(" ("), mainTableSpan);
            parent.appendChild(document.createTextNode(")"));
        } else if (mainTableSpan.innerText && !newVal) {
            // remove parens
            parent.removeChild(mainTableSpan.previousSibling!);
            parent.removeChild(mainTableSpan.nextSibling!);
        }
        mainTableSpan.innerText = newVal;
    } else {
        mainTableSpan.innerText = newVal || "-";
    }
}

function getMainTableSpan(spanClassName: string): HTMLSpanElement {
    const levelsTable = document.getElementById("levels-table")!;
    return levelsTable.querySelector(
        `tbody tr.selected td span.${spanClassName}`
    )! as HTMLSpanElement;
}

function setAllowedWordsFile(fileName: string): void {
    const allowedWordsSpan: HTMLSpanElement = <HTMLSpanElement>(
        document.getElementById("allowed-words-file")
    );
    const currentFile: string = allowedWordsSpan.innerHTML;

    // set the new text
    allowedWordsSpan.innerHTML = fileName;

    // I had trouble getting the compiler to accept a string as a boolean parameter.
    // But this 'fileNameIs' works.
    const fileNameIs: boolean = fileName ? true : false;
    setElementDisplay("setup-choose-allowed-words-file", fileNameIs);
    setElementDisplay("allowed-words-file-div", !fileNameIs);
    if (!fileNameIs) {
        fileName = ""; // to be sure it isn't undefined
    }

    $("#stages-table")
        .find("tbody tr.selected td:nth-child(4)")
        .html(fileName);

    // remove file if no longer used
    if (currentFile) {
        checkAndDeleteAllowedWordsFile(currentFile);
    }
}

/**
 * If this file is no longer being used, delete it from the 'Word Lists' directory.
 * @param fileName
 */
function checkAndDeleteAllowedWordsFile(fileName: string): void {
    // loop through the stages looking for the file name
    const stages: JQuery = $("#stages-table").find("tbody tr");
    for (let i: number = 0; i < stages.length; i++) {
        const row: HTMLTableRowElement = <HTMLTableRowElement>stages[i];

        // if this file name is still in use, return now
        if ((<HTMLTableCellElement>row.cells[3]).innerHTML == fileName) {
            return;
        }
    }

    // if you are here, the file name is not in use
    // Using axios directly because delete doesn't return a promise and doesn't need the bloomApi
    // treatment.
    axios.delete("/bloom/api/readers/io/allowedWordsList", {
        params: { fileName: fileName }
    });
}

export function enableSampleWords() {
    // get the selected option
    const useSampleWords =
        $('input[name="words-or-letters"]:checked').val() === "1";

    // initialize control state
    const controls = $("#dlstabs-1").find(".disableable");
    const stagesTable = $("#stages-table");
    controls.removeClass("disabled");
    stagesTable.removeClass("hide-second-column");
    stagesTable.removeClass("hide-third-column");
    stagesTable.removeClass("hide-fourth-column");

    // enable or disable
    if (useSampleWords) {
        controls.addClass("disabled");
        stagesTable.addClass("hide-second-column");
        stagesTable.addClass("hide-third-column");
    } else {
        stagesTable.addClass("hide-fourth-column");
    }

    // controls for letter-based stages
    setElementDisplay("setup-stage-letters-and-words", useSampleWords);
    setElementDisplay("matching-words-span", useSampleWords);
    // controls for word-list-based stages
    setElementDisplay("setup-stage-words-file", !useSampleWords);
    setElementDisplay("allowed-words-span", !useSampleWords);
}

function setElementDisplay(
    elementName: string,
    conditionForNone: boolean
): void {
    const element = document.getElementById(elementName);
    if (!element) return;
    element.style.display = conditionForNone ? "none" : "";
}

function setWordContainerHeight() {
    // set height of word list
    const div: JQuery = $("#setup-stage-matching-words").find(
        "div:first-child"
    );
    const ht = $("setup-words-count").height();
    div.css("height", "calc(100% - " + ht + "px)");
}

// This is a super-simplistic markdown processor that does just as much as we need.
// Currently that is just to handle making text delimited by ** bold.
export function processMarkdown(e: HTMLElement) {
    const parts = e.textContent?.split("**");
    if (!parts || parts.length <= 1) {
        return;
    }
    e.innerText = parts[0];
    for (let i = 1; i < parts.length; i++) {
        const text = parts[i];
        if (i % 2) {
            const boldElement = document.createElement("strong");
            boldElement.innerText = text;
            e.appendChild(boldElement);
        } else {
            if (text.length > 0) {
                e.appendChild(document.createTextNode(text));
            }
        }
    }
}

/**
 * Called after localized strings are loaded.
 */
function finishInitializing() {
    document.querySelectorAll("[data-i18n]").forEach(matchingElem => {
        processMarkdown(matchingElem as HTMLElement);
    });

    $("#stages-table")
        .find("tbody")
        .sortable({ stop: updateStageNumbers });
    $("#levels-table")
        .find("tbody")
        .sortable({ stop: updateLevelNumbers });
    const window = toolboxWindow();
    if (window) {
        window.postMessage("Texts", "*");
    }
    setWordContainerHeight();
}

/**
 * The ReaderTools calls this function to notify the dialog that the word list and/or the list of sample files
 * has changed.
 */
function wordListChangedCallback() {
    const toolbox = toolboxWindow();
    if (!toolbox) return;
    toolbox.postMessage("Texts", "*");
    requestWordsForSelectedStage();
}

import { getToolboxBundleExports } from "../../../js/bloomFrames";

$(document).ready(() => {
    attachEventHandlers();
    $("body")
        .find("*[data-i18n]")
        .localize(finishInitializing);
    //This should always be found in real life, but in unit tests it may not exist.
    getToolboxBundleExports()?.addWordListChangedListener(
        "wordListChanged.ReaderSetup",
        wordListChangedCallback
    );
    // found solution to longpress access here:
    // http://stackoverflow.com/questions/3032770/execute-javascript-function-in-a-another-iframe-when-parent-is-from-different-do
    const container = $("body");
    //   const pageIframe = parent.frames['page'];
    //   pageIframe.toolboxBundle.activateLongPressFor(container.find('textarea'));
    getToolboxBundleExports()?.activateLongPressFor(container.find("textarea"));
});
