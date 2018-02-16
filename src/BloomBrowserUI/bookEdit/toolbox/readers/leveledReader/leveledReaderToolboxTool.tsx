/// <reference path="../../toolbox.ts" />
import * as React from "react";
import * as ReactDOM from "react-dom";
import { DRTState, getTheOneReaderToolsModel, MarkupType } from "../readerToolsModel";
import { H3, Div, IUILanguageAwareProps, Label } from "../../../../react_components/l10n";
import { beginInitializeLeveledReaderTool } from "../readerTools";
import { ToolBox, ITool } from "../../toolbox";
import axios from "axios";

//There is a line in toolboxBootstrap.ts which causes this to be included in the master toolbox
//It adds and instance of LeveledReaderToolboxTool to ToolBox.getMasterToolList().
export class LeveledReaderToolboxTool implements ITool {
    rootControl: LeveledReaderControl;
    makeRootElement(): HTMLDivElement {
        const root = document.createElement("div");
        root.setAttribute("class", "ui-LeveledReaderBody");
        this.rootControl = ReactDOM.render(
            <LeveledReaderControl />,
            root
        );
        const initialState = this.getStateFromHtml();
        this.rootControl.setState(initialState);
        return root as HTMLDivElement;
    }

    beginRestoreSettings(opts: string): JQueryPromise<void> {
        return beginInitializeLeveledReaderTool().then(() => {
            const leveledReaderStr = "leveledReaderState";
            if (opts[leveledReaderStr]) {
                // The true passed here prevents re-saving the state we just read.
                // One non-obvious implication is that simply opening a level-4 book
                // will not switch the default level for new books to 4. That only
                // happens when you CHANGE the level in the toolbox.
                getTheOneReaderToolsModel().setLevelNumber(parseInt(opts[leveledReaderStr], 10), true);
            } else {
                axios.get("/bloom/api/readers/io/defaultLevel").then(result => {
                    // Presumably a brand new book. We'd better save the settings we come up with in it.
                    getTheOneReaderToolsModel().setLevelNumber(parseInt(result.data, 10));
                });
            }
        });
    }

    isAlwaysEnabled(): boolean {
        return false;
    }

    showTool() {
        // change markup based on visible options
        getTheOneReaderToolsModel().setCkEditorLoaded(); // we don't call showTool until it is.
        if (!getTheOneReaderToolsModel().setMarkupType(2)) getTheOneReaderToolsModel().doMarkup();
    }

    hideTool() {
        getTheOneReaderToolsModel().setMarkupType(0);
    }

    updateMarkup() {
        // Most cases don't require setMarkupType(), but when switching pages
        // it will have been set to 0 by hideTool() on the old page.
        getTheOneReaderToolsModel().setMarkupType(2);
        getTheOneReaderToolsModel().doMarkup();
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
    // Unneeded in Leveled Reader, since Bloom.web.ExternalLinkController
    // 'translates' external links to include the current UI language.
    /* tslint:enable:no-empty */

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

    id() {
        return "leveledReader";
    }

    //Do we need from here down?

    observer: MutationObserver;
    updateLeveledReaderState(): void {
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
        this.observer = new MutationObserver(() => this.updateLeveledReaderState());
    }

    getStateFromHtml(): ILeveledReaderState {
        let level = 1;
        return { start: level };
    }

    public setup(root): LeveledReaderControl {
        return ReactDOM.render(
            <LeveledReaderControl />,
            root
        );
    }

}

interface ILeveledReaderState {
    start: number;
}

// This react class implements the UI for the pan and zoom toolbox.
export class LeveledReaderControl extends React.Component<{}, ILeveledReaderState> {
    constructor(props) {
        super(props);
        // This state won't last long, client sets it immediately. But must have something.
        // To minimize flash we start with both off.
        this.state = { start: 1 };
    }
    //H3 data-i18n="LeveledReader"
    public render() {
        return (
            <div className="ui-LeveledReaderBody">
                <H3 data-i18n="EditTab.Toolbox.LeveledReaderTool"
                    data-order="20"
                    data-toolId="leveledReaderTool"
                    l10nKey="EditTab.Toolbox.LeveledReader.Heading">
                    Leveled Reader Tool</H3>
                <div data-toolId="leveledReaderTool">
                    <div id="setupStages">
                        <img id="leveled-edit" src="/bloom/images/edit-white.png" />
                        <span className="setup noSelect">
                            <a data-i18n="EditTab.Toolbox.LeveledReaderTool.SetUpLevels"
                                href="javascript:window.FrameExports.showSetupDialog('levels');">
                                Set up Levels
                            </a>
                        </span>
                    </div>
                    <div className="stageLine clear noSelect">
                        <span className="scroll-button ui-icon ui-icon-triangle-1-w" id="decLevel" />
                        <span className="stageLabel stageLine noSelect">
                            <span data-i18n="EditTab.Toolbox.LeveledReaderTool.Level">Level</span>
                            <span id="levelNumber">1</span>
                            <span className="ofStage" data-i18n="EditTab.Toolbox.LeveledReaderTool.LevelOf">of</span>
                            <span className="ofStage" id="numberOfLevels">2</span>
                        </span>
                        <span className="scroll-button ui-icon ui-icon-triangle-1-e" id="incLevel" />
                    </div>
                    <table className="statistics clear ui-leveled-Reader-table">
                        <tr>
                            <td className="section" data-i18n="EditTab.Toolbox.LeveledReaderTool.WordCounts">
                                Word Counts
                            </td>
                        </tr>
                        <tr>
                            <td
                                className="tableTitle thisPageSection"
                                data-i18n="EditTab.Toolbox.LeveledReaderTool.ThisPage">
                                This Page
                            </td>
                        </tr>
                        <tr>
                            <td className="statistics-label" />
                            <td className="statistics-max" data-i18n="EditTab.Toolbox.LeveledReaderTool.Max">Max</td>
                            <td className="statistics-actual" data-i18n="EditTab.Toolbox.LeveledReaderTool.Actual">
                                Actual
                            </td>
                        </tr>
                        <tr>
                            <td className="statistics-label" data-i18n="EditTab.Toolbox.LeveledReaderTool.PerPage">
                                per page </td>
                            <td className="statistics-max" id="maxWordsPerPage" />
                            <td className="statistics-actual" id="actualWordsPerPage">-</td>
                        </tr>
                        <tr>
                            <td
                                className="statistics-label"
                                data-i18n="EditTab.Toolbox.LeveledReaderTool.PerSentence">
                                longest sentence
                            </td>
                            <td className="statistics-max" id="maxWordsPerSentence" />
                            <td className="statistics-actual" id="actualWordsPerSentence">-</td>
                        </tr>
                    </table>
                    <table className="statistics clear ui-leveled-Reader-table">
                        <tr>
                            <td className="tableTitle" data-i18n="EditTab.Toolbox.LeveledReaderTool.ThisBook">
                                This Book
                            </td>
                        </tr>
                        <tr>
                            <td className="statistics-label" />
                            <td className="statistics-max" data-i18n="EditTab.Toolbox.LeveledReaderTool.Max">Max</td>
                            <td className="statistics-actual" data-i18n="EditTab.Toolbox.LeveledReaderTool.Actual">
                                Actual
                            </td>
                        </tr>
                        <tr>
                            <td className="statistics-label" data-i18n="EditTab.Toolbox.LeveledReaderTool.Total">
                                total
                            </td>
                            <td className="statistics-max" id="maxWordsPerBook" />
                            <td className="statistics-actual" id="actualWordCount">-</td>
                        </tr>
                        <tr>
                            <td className="statistics-label" data-i18n="EditTab.Toolbox.LeveledReaderTool.PerPage">
                                per page
                            </td>
                            <td className="statistics-max" id="maxWordsPerPageBook" />
                            <td className="statistics-actual" id="actualWordsPerPageBook">-</td>
                        </tr>
                        <tr>
                            <td className="statistics-label" data-i18n="EditTab.Toolbox.LeveledReaderTool.Unique">
                                unique
                            </td>
                            <td className="statistics-max" id="maxUniqueWordsPerBook" />
                            <td className="statistics-actual" id="actualUniqueWords">-</td>
                        </tr>
                        <tr>
                            <td className="statistics-label" data-i18n="EditTab.Toolbox.LeveledReaderTool.Average">
                                avg per sentence
                            </td>
                            <td className="statistics-max" id="maxAverageWordsPerSentence" />
                            <td className="statistics-actual" id="actualAverageWordsPerSentence">-</td>
                        </tr>
                    </table>
                    <div className="ui-leveledReader-div2" />
                    <div className="section ui-leveledReader-div">
                        <span data-i18n="EditTab.Toolbox.LeveledReaderTool.FoThisLevel">For this Level</span>
                        <ul id="thingsToRemember" />
                    </div>
                    <div className="section ui-leveledReader-div" id="keepInMindLinks">
                        <span data-i18n="EditTab.Toolbox.LeveledReaderTool.KeepInMind">Keep in mind</span>
                        <ul>
                            <li>
                                <a data-i18n="EditTab.Toolbox.LeveledReaderTool.Vocabulary"
                                    href="api/externalLink/leveledRTInfo/leveledReaderInfo-en.html?fragment=Vocabulary">
                                    Vocabulary
                                </a>
                            </li>
                            <li>
                                <a data-i18n="EditTab.Toolbox.LeveledReaderTool.Formatting"
                                    href="api/externalLink/leveledRTInfo/leveledReaderInfo-en.html?fragment=Formatting">
                                    Formatting
                                </a>
                            </li>
                            <li>
                                <a data-i18n="EditTab.Toolbox.LeveledReaderTool.Predictability">
                                    Predictability
                                </a>
                            </li>
                            <li>
                                <a data-i18n="EditTab.Toolbox.LeveledReaderTool.IllustrationSupport"
                                    href="api/externalLink/leveledRTInfo/leveledReaderInfo-en.html?fragment=IllustrationSupport">
                                    Illustration Support
                                </a>
                            </li>
                            <li>
                                <a data-i18n="EditTab.Toolbox.LeveledReaderTool.ChoiceOfTopic"
                                    href="api/externalLink/leveledRTInfo/leveledReaderInfo-en.html?fragment=ChoiceOfTopic">
                                    Choice of Topic
                                </a>
                            </li>
                        </ul>
                    </div>
                </div>
            </div>
        );
    }
}