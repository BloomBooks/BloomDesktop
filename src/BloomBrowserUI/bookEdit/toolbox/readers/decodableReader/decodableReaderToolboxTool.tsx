/// <reference path="../../toolbox.ts" />
import * as React from "react";
import * as ReactDOM from "react-dom";
import { DRTState, getTheOneReaderToolsModel, MarkupType } from "../readerToolsModel";
import { H3, Div, IUILanguageAwareProps, Label } from "../../../../react_components/l10n";
import { beginInitializeDecodableReaderTool } from "../readerTools";
import { ToolBox, ITool } from "../../toolbox";
import axios from "axios";

//There is a line in toolboxBootstrap.ts which causes this to be included in the master toolbox
//It adds and instance of DecodableReaderToolboxTool to ToolBox.getMasterToolList().
export class DecodableReaderToolboxTool implements ITool {
    rootControl: DecodableReaderControl;
    makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        root.setAttribute("class", "ui-DecodableReaderBody");
        this.rootControl = ReactDOM.render(
            <DecodableReaderControl />,
            root
        );
        const initialState = this.getStateFromHtml();
        this.rootControl.setState(initialState);
        return root as HTMLDivElement;
    }

    beginRestoreSettings(settings: string): JQueryPromise<void> {
        return beginInitializeDecodableReaderTool().then(() => {
            const leveledReaderStr = "decodableReaderState";
            if (settings[leveledReaderStr]) {
                var state = new DRTState();
                var decState = settings[leveledReaderStr];
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

    isAlwaysEnabled(): boolean {
        return false;
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
    // This gives us a hook to finish up the more difficult spots
    finishToolLocalization(pane: HTMLElement) { }
    // Unneeded in Decodable Reader, since Bloom.web.ExternalLinkController
    // 'translates' external links to include the current UI language.
    /* tslint:enable:no-empty */

    updateMarkup() {
        const newState = this.getStateFromHtml();
        // Most cases don't require setMarkupType(), but when switching pages
        // it will have been set to 0 by hideTool() on the old page.
        getTheOneReaderToolsModel().setMarkupType(1);
        getTheOneReaderToolsModel().doMarkup();
    }

    showTool() {
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

    //Do we need from here down?

    observer: MutationObserver;
    updateDecodableReaderState(): void {
        this.rootControl.setState({ start: 1 });
        this.observer.disconnect();
        this.updateMarkup(); // one effect is to show the rectangles.
    }
    updateDataAttributes(): void {
        const page = this.getPage();
    }
    private wrapperClassName = "bloom-ui-animationWrapper";
    public getPageFrame(): HTMLIFrameElement {
        return parent.window.document.getElementById("page") as HTMLIFrameElement;
    }

    // The document object of the editable page, a root for searching for document content.
    public getPage(): HTMLDocument {
        var page = this.getPageFrame();
        if (!page) return null;
        return page.contentWindow.document;
    }

    setupObserver(): void {
        // Arrange to update things when they DO change the Level.
        this.observer = new MutationObserver(() => this.updateDecodableReaderState());
    }

    getStateFromHtml(): IDecodableReaderState {
        let level = 1;
        return { start: level };
    }

    public setup(root): DecodableReaderControl {
        return ReactDOM.render(
            <DecodableReaderControl />,
            root
        );
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
    //H3 data-i18n="DecodableReader"
    public render() {
        return (
            <div className="ui-DecodableReaderBody">
                <H3 data-i18n="EditTab.Toolbox.DecodableReaderTool"
                    data-order="10"
                    data-toolId="decodableReaderTool"
                    l10nKey="EditTab.Toolbox.DecodableReader.Heading">
                    Decodable Reader Tool
                </H3>
                <div data-toolId="decodableReaderTool">
                    <div id="setupStages">
                        <img id="decodable-edit" src="/bloom/images/edit-white.png" />
                        <span className="setup noSelect">
                            <a data-i18n="EditTab.Toolbox.DecodableReaderTool.SetUpStages"
                                href="javascript:window.FrameExports.showSetupDialog('stages');">
                                Set up Stages
                            </a>
                        </span>
                    </div>
                    <div className="stageLine clear noSelect">
                        <span className="scroll-button ui-icon ui-icon-triangle-1-w" id="decStage" />
                        <span className="stageLabel stageLine noSelect ui-Decodable-Reader-span1">
                            <span data-i18n="EditTab.Toolbox.DecodableReaderTool.Stage"> Stage </span>
                            <span id="stageNumber"> 1 </span>
                            <span className="ofStage ui-Decodable-Reader-span1"
                                data-i18n="EditTab.Toolbox.DecodableReaderTool.StageOf"> of </span>
                            <span className="ofStage ui-Decodable-Reader-span1" id="numberOfStages"> 2 </span>
                        </span>
                        <span className="scroll-button ui-icon ui-icon-triangle-1-e" id="incStage" />
                    </div>
                    <div className="section clear ui-Decodable-Reader-sect1" id="letters-in-this-stage">
                        <span data-i18n="EditTab.Toolbox.DecodableReaderTool.LettersInThisStage">
                            Letters in this stage
                        </span>
                    </div>
                    <div className="tableHolder clear" id="lettersTable">
                        <div className="letterList" id="letterList" />
                    </div>
                    <div className="section clear ui-Decodable-Reader-sect1">
                        <table>
                            <tr>
                                <td className="ui-Decodable-Reader-td1">
                                    <span
                                        data-i18n="EditTab.Toolbox.DecodableReaderTool.SampleWordsInThisStage"
                                        id="sample-words-this-stage">
                                        Sample words in this stage
                                    </span>
                                    <span
                                        data-i18n="EditTab.Toolbox.DecodableReaderTool.AllowedWordsInThisStage"
                                        id="allowed-words-this-stage">
                                        Allowed words in this stage
                                    </span>
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
                        </table>
                    </div>
                    <div className="tableHolder clear">
                        <div className="wordList" id="wordList" />
                    </div>
                    <div className="clear" id="make-letter-word-list-div">
                        <a data-i18n="EditTab.Toolbox.DecodableReaderTool.MakeLetterWordReport"
                            href="javascript:FrameExports.makeLetterWordList();"
                            id="make-letter-word-list">
                            Generate a letter and word list report
                        </a>
                    </div>
                    <div className="clear ui-Decodable-Reader-div-red" id="allowed-word-list-truncated" />
                    <div className="clear ui-Decodable-Reader-div-none" id="hiddenWordListForDecodableReader">
                        <label data-i18n="EditTab.Toolbox.DecodableReaderTool.AllowedWordListTruncated"
                            id="allowed_word_list_truncated_text">
                            Bloom can handle only the first {0} words.
                        </label>
                    </div>
                </div>
            </div>
        );
    }
}