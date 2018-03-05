/// <reference path="../../toolbox.ts" />
import * as React from "react";
import * as ReactDOM from "react-dom";
import { DRTState, getTheOneReaderToolsModel, MarkupType } from "../readerToolsModel";
import { H3, Div, Span, IUILanguageAwareProps, Label } from "../../../../react_components/l10n";
import Link from "../../../../react_components/link";
import { beginInitializeDecodableReaderTool } from "../readerTools";
import { ToolBox, ITool } from "../../toolbox";
import { EditTool } from "../../../toolbox/editTool";
import axios from "axios";

//There is a line in toolboxBootstrap.ts which causes this to be included in the master toolbox
//It adds an instance of DecodableReaderToolboxTool to ToolBox.getMasterToolList().
export class DecodableReaderToolboxTool extends EditTool {
    rootControl: DecodableReaderControl;
    makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        root.setAttribute("class", "ui-decodableReaderBody");
        this.rootControl = ReactDOM.render(
            <DecodableReaderControl />,
            root
        );
        this.rootControl.setState(this.getStateFromHtml());
        return root as HTMLDivElement;
    }

    beginRestoreSettings(settings: string): JQueryPromise<void> {
        return beginInitializeDecodableReaderTool().then(() => {
            const decodableReaderStr = "decodableReaderState";
            if (settings[decodableReaderStr]) {
                var state = new DRTState();
                var decState = settings[decodableReaderStr];
                if (decState.startsWith("stage:")) {
                    var parts = decState.split(";");
                    var stage = parseInt(parts[0].substring("stage:".length), 10);
                    var sort = parts[1].substring("sort:".length);
                    // The true's passed here prevent re-saving the state we just read.
                    // One non-obvious implication is that simply opening a stage-4 book
                    // will not switch the default stage for new books to 4. That only
                    // happens when you CHANGE the stage in the toolbox.
                    getTheOneReaderToolsModel().setSort(sort, true);
                    getTheOneReaderToolsModel().setStageNumber(stage, true);
                    console.log("set stage in beginRestoreSettings to " + stage);
                } else {
                    // old state
                    getTheOneReaderToolsModel().setStageNumber(parseInt(decState, 10), true);
                }
            } else {
                axios.get("/bloom/api/readers/io/defaultStage").then(result => {
                    // Presumably a brand new book. We'd better save the settings we come up with in it.
                    getTheOneReaderToolsModel().setStageNumber(parseInt(result.data, 10));
                });
            }
        });
    }

    setupReaderKeyAndFocusHandlers(container: HTMLElement): void {
        const bloomEditStr = ".bloom-editable";
        // invoke function when a bloom-editable element loses focus.
        $(container).find(bloomEditStr).focusout(function () {
            getTheOneReaderToolsModel().doMarkup();
        });

        $(container).find(bloomEditStr).focusin(function () {
            getTheOneReaderToolsModel().noteFocus(this); // 'This' is the element that just got focus.
        });

        $(container).find(bloomEditStr).keydown(function (e) {
            if ((e.keyCode === 90 || e.keyCode === 89) && e.ctrlKey) { // ctrl-z or ctrl-Y
                if (getTheOneReaderToolsModel().currentMarkupType !== MarkupType.None) {
                    e.preventDefault();
                    if (e.shiftKey || e.keyCode === 89) { // ctrl-shift-z or ctrl-y
                        getTheOneReaderToolsModel().redo();
                    } else {
                        getTheOneReaderToolsModel().undo();
                    }
                    return false;
                }
            }
        });
    }
    // required for ITool interface
    hasRestoredSettings: boolean;
    /* tslint:disable:no-empty */
    // We need these to implement the interface, but don't need them to do anything.
    configureElements(container: HTMLElement) {
        this.setupReaderKeyAndFocusHandlers(container);
    }
    // Some things were impossible to do i18n on via the jade/pug
    /* tslint:enable:no-empty */

    updateMarkup() {
        // Most cases don't require setMarkupType(), but when switching pages
        // it will have been set to 0 by hideTool() on the old page.
        getTheOneReaderToolsModel().setMarkupType(1);
        getTheOneReaderToolsModel().doMarkup();
    }

    showTool() {
        this.updateMarkup();
        // change markup based on visible options
        getTheOneReaderToolsModel().setCkEditorLoaded(); // we don't call showTool until it is.
        if (!getTheOneReaderToolsModel().setMarkupType(1)) getTheOneReaderToolsModel().doMarkup();
    }

    hideTool() {
        getTheOneReaderToolsModel().setMarkupType(0);
    }

    id() {
        return "decodableReader";
    }

    getStateFromHtml(): IDecodableReaderState {
        let level = 1;
        return { start: level };
    }
}

interface IDecodableReaderState {
    start: number;
}

// This react class implements the UI for the pan and zoom toolbox.
export class DecodableReaderControl extends React.Component<{}, IDecodableReaderState> {
    constructor(props) {
        super(props);
        // This state won't last long, client sets it immediately. But must have something.
        // To minimize flash we start with both off.
        this.state = { start: 1 };
    }
    public render() {
        return (
            <div>
                <div id="setupStages" >
                    <img id="decodable-edit" src="/bloom/images/edit-white.png" />
                    <span className="setup noSelect">
                        <Link l10nKey="EditTab.Toolbox.DecodableReaderTool.SetUpStages"
                            id="showSetupDialog-stages" href="javascript:window.FrameExports.showSetupDialog('stages');">
                            Set up Stages
                            </Link>
                    </span>
                </div>
                <div className="stageLine clear noSelect">
                    <span className="scroll-button ui-icon ui-icon-triangle-1-w" id="decStage" />
                    <span className="stageLabel stageLine noSelect ui-Decodable-Reader-span1">
                        <Span l10nKey="EditTab.Toolbox.DecodableReaderTool.Stage"> Stage </Span>
                        <span id="stageNumber"> 1 </span>
                        <Span className="ofStage ui-Decodable-Reader-span1"
                            l10nKey="EditTab.Toolbox.DecodableReaderTool.StageOf"> of </Span>
                        <span className="ofStage ui-Decodable-Reader-span1" id="numberOfStages"> 2 </span>
                    </span>
                    <span className="scroll-button ui-icon ui-icon-triangle-1-e" id="incStage" />
                </div>
                <div className="section clear ui-Decodable-Reader-letters-words" id="letters-in-this-stage">
                    <Span l10nKey="EditTab.Toolbox.DecodableReaderTool.LettersInThisStage">
                        Letters in this stage
                        </Span>
                </div>
                <div className="tableHolder clear" id="lettersTable">
                    <div className="letterList" id="letterList" />
                </div>
                <div className="section clear ui-Decodable-Reader-letters-words">
                    <table>
                        <thead>
                            <tr>
                                <td className="ui-Decodable-Reader-words">
                                    <Span
                                        l10nKey="EditTab.Toolbox.DecodableReaderTool.SampleWordsInThisStage"
                                        className="sample-words-this-stage">
                                        Sample words in this stage
                                    </Span>
                                    <Span
                                        l10nKey="EditTab.Toolbox.DecodableReaderTool.AllowedWordsInThisStage"
                                        className="allowed-words-this-stage">
                                        Allowed words in this stage
                                    </Span>
                                </td>
                                <td className="ui-Decodable-Reader-td1">
                                    <div className="sortBlock clear">
                                        <div className="sortItem rightBorder sortIconSelected" id="sortAlphabetic">
                                            <i className="fa fa-sort-alpha-asc" title="Sort alphabetically" />
                                        </div>
                                        <div className="sortItem rightBorder" id="sortLength">
                                            <i className="fa fa-sort-amount-asc" title="Sort by word length" />
                                        </div>
                                        <div className="sortItem" id="sortFrequency">
                                            <i className="fa fa-long-arrow-up" title="Sort by frequency" />
                                            <i className="fa fa-facebook" id="sortFrequency2" title="Sort by frequency" />
                                        </div>
                                    </div>
                                </td>
                            </tr>
                        </thead>
                    </table>
                </div>
                <div className="tableHolder clear">
                    <div className="wordList" id="wordList" />
                </div>
                <div className="clear" id="make-letter-word-list-div">
                    <Link l10nKey="EditTab.Toolbox.DecodableReaderTool.MakeLetterWordReport"
                        href="javascript:FrameExports.makeLetterWordList();"
                        id="make-letter-word-list">
                        Generate a letter and word list report
                        </Link>
                </div>
                <div className="clear ui-Decodable-Reader-letters-words ui-Decodable-Reader-red-font"
                    id="allowed-word-list-truncated" />
                <div className="clear ui-Decodable-Reader-letters-words ui-Decodable-Reader-no-display"
                    id="hiddenWordListForDecodableReader">
                    <Label l10nKey="EditTab.Toolbox.DecodableReaderTool.AllowedWordListTruncated"
                        className="allowed_word_list_truncated_text">
                        Bloom can handle only the first {0} words.
                        </Label>
                </div>
            </div>
        );
    }
}
