import { getTheOneReaderToolsModel } from "../readerToolsModel";
import { ReaderToolSwitch } from "../ReaderToolSwitch";
import {
    useLayoutEffect,
    useRef,
    useState,
    FunctionComponent,
    useReducer,
} from "react";
import { Span } from "../../../../react_components/l10nComponents";
import BloomButton from "../../../../react_components/bloomButton";
import { css, ThemeProvider } from "@emotion/react";
import { isReaderToolEnabledOnCurrentPage } from "../readerToolPageState";
import { DataWord } from "../libSynphony/bloomSynphonyExtensions";
import { useL10n } from "../../../../react_components/l10nHooks";
import {
    kBloomBlue,
    kBloomDarkestBackground,
} from "../../../../utils/colorUtils";
import { ReaderToolNav } from "../ReaderToolNav";
import { ReaderSetupButton } from "../ReaderSetupButton";
import {
    kReaderAccent,
    readerSectionHeaderCss,
    readerTextButtonCss,
} from "../readerToolStyles";
import { SummarizeOutlined } from "@mui/icons-material";
import { toolboxTheme } from "../../../../bloomMaterialUITheme";
import { useMountEffect } from "../../../../utils/useMountEffect";
import { BloomTooltip } from "../../../../react_components/BloomToolTip";

// This component displays a list of words in a grid
// where the number of rows and columns is dynamically
// determined based on the word with the greatest width
const DecodableGrid: FunctionComponent<{
    direction: "row" | "column";
    words: DataWord[] | string[];
}> = (props) => {
    const measureRef = useRef<HTMLDivElement>(null);
    const gridRef = useRef<HTMLDivElement>(null);
    const [curColCount, setCurColCount] = useState(1);
    const [curRowCount, setCurRowCount] = useState(1);

    // useLayoutEffect is being used here so that the proper dimensions
    // needed for the grid can be computed and applied before the grid
    // is displayed in the toolbar.
    useLayoutEffect(() => {
        if (!measureRef.current || !gridRef.current) return;

        // Here, getBoundingClientRect().width is used on a hidden div
        // and the actual grid div to properly determine how many rows
        // and columns will be needed to nicely fit all the words within
        // the grid. This means that words are placed in both the hidden
        // div and the grid div.
        const width = Math.ceil(
            measureRef.current.getBoundingClientRect().width,
        );

        const gridWidth = gridRef.current.getBoundingClientRect().width;

        if (width !== 0) {
            let colCount = Math.floor((gridWidth - 20) / width);
            if (colCount < 1) {
                colCount = 1;
            } else if (colCount > 7) {
                colCount = 7; // this way, the graphemes and small words won't be so clumped together
            }
            setCurColCount(colCount);
            const rowCount = Math.ceil(props.words.length / colCount);
            setCurRowCount(rowCount);
        } else {
            setCurColCount(1);
            setCurRowCount(1);
        }
    }, [props.words]);

    return (
        <>
            {/* This is the hidden div that's used to get the width of the widest word,
                by displaying all the words in a single column*/}
            <div
                ref={measureRef}
                css={css`
                    position: absolute;
                    visibility: hidden;
                    white-space: nowrap;
                    pointer-events: none;
                    font-family: ${getTheOneReaderToolsModel().fontName};
                `}
            >
                {props.words.map((item, index) => (
                    <div key={index} className="lang1InATool">
                        {typeof item === "string" ? item : item.Name}
                    </div>
                ))}
            </div>
            <div
                ref={gridRef}
                css={css`
                    display: grid;
                    align-content: start;
                    grid-template-columns: repeat(${curColCount}, auto);
                    grid-template-rows: repeat(${curRowCount}, auto);
                    grid-auto-flow: ${props.direction};
                    min-height: 0;
                    flex: 1 1 auto;
                    overflow: auto;
                    // Reserve space for the vertical scrollbar so its appearance
                    // (when the word list grows tall) doesn't shrink/reflow the
                    // grid, matching the Leveled Reader stabilization. (BL-16585)
                    scrollbar-gutter: stable;
                    margin-top: 4px;
                    padding-bottom: 0.6em;
                    font-family: ${getTheOneReaderToolsModel().fontName};
                `}
            >
                {props.words.map((item, index) => (
                    <div
                        key={index}
                        className="lang1InATool"
                        css={css`
                            line-height: 1.3;
                            padding-bottom: 0.2em;
                            color: ${typeof item !== "string" &&
                            item.isSightWord
                                ? "#87cefa"
                                : "white"};
                        `}
                    >
                        {typeof item === "string" ? item : item.Name}
                    </div>
                ))}
            </div>
        </>
    );
};

const StageGraphemes: FunctionComponent = () => {
    return (
        <div
            css={css`
                margin-top: 15px;
                display: flex;
                flex-direction: column;
                flex: 0 1 auto;
                min-height: 100px;
                overflow: hidden;
            `}
        >
            <Span
                l10nKey="EditTab.Toolbox.DecodableReaderTool.LettersInThisStage"
                css={css`
                    ${readerSectionHeaderCss};
                    // Make the underline rule span the full panel width, matching
                    // the Leveled Reader's "Word Counts" section header.
                    display: block;
                `}
            >
                Letters in this stage
            </Span>
            <DecodableGrid
                direction="row"
                words={getTheOneReaderToolsModel().getKnownGraphemesSorted()}
            />
        </div>
    );
};

const SortButton: FunctionComponent<{
    sortType: 0 | 1 | 2;
    changeSortFunc: (which: 0 | 1 | 2) => void;
}> = (props) => {
    const model = getTheOneReaderToolsModel();
    let keyInsert: string;
    let tipInsert: string;
    let unicodeIcon: string;
    let shouldHighlight: boolean = false;
    switch (props.sortType) {
        case 0:
            keyInsert = "Alphabetically";
            tipInsert = "alphabetically";
            unicodeIcon = "\uf15d";
            shouldHighlight = model.sort === "alphabetic" ? true : false;
            break;
        case 1:
            keyInsert = "ByWordLength";
            tipInsert = "by word length";
            unicodeIcon = "\uf160";
            shouldHighlight = model.sort === "byLength" ? true : false;
            break;
        case 2:
            keyInsert = "ByFrequency";
            tipInsert = "by frequency";
            unicodeIcon = "\uf176\uf09a";
            shouldHighlight = model.sort === "byFrequency" ? true : false;
            break;
    }
    // this component makes use of useL10n to localize the tooltip text
    // for the bloom tooltip component
    const tooltip = useL10n(
        `Sort ${tipInsert}`,
        `EditTab.Toolbox.DecodableReaderTool.Sort${keyInsert}`,
    );
    return (
        <BloomTooltip
            tip={tooltip}
            placement="top-end"
            slotProps={{
                tooltip: { sx: { width: "auto", maxWidth: "165px" } },
            }}
        >
            <BloomButton
                l10nKey="already-localized"
                alreadyLocalized={true}
                variant="text"
                enabled={true}
                hasText={true}
                onClick={() => props.changeSortFunc(props.sortType)}
                css={css`
                    font-family: FontAwesome;
                    width: 20px;
                    min-width: unset;
                    height: 20px;
                    margin-top: 1px;
                    border-radius: 0;
                    border: 1px solid ${kReaderAccent};
                    // Accent outline when idle; the selected sort fills solid
                    // kBloomBlue with a white icon so the current choice stands
                    // out. (BL-16585)
                    color: ${shouldHighlight ? "white" : kReaderAccent};
                    background-color: ${shouldHighlight
                        ? kBloomBlue
                        : "transparent"};

                    &&,
                    &&:hover {
                        border-width: 1px;
                    }

                    &:hover {
                        background-color: ${shouldHighlight
                            ? kBloomBlue
                            : "transparent"};
                    }
                `}
            >
                {unicodeIcon}
            </BloomButton>
        </BloomTooltip>
    );
};

const SortedStageWords: FunctionComponent<{
    changeSortFunc: (which: 0 | 1 | 2) => void;
}> = (props) => {
    const model = getTheOneReaderToolsModel();
    return (
        <div
            css={css`
                margin-top: 15px;
                display: flex;
                flex-direction: column;
                flex: 1 1 200px;
                min-height: 200px;
                overflow: hidden;
            `}
        >
            {/* The section header (styled like the Leveled Reader's "Word Counts")
                shares its row with the sort buttons; the underline rule spans the
                whole width beneath both. */}
            <div
                css={css`
                    ${readerSectionHeaderCss};
                    display: flex;
                    align-items: center;
                `}
            >
                {model.synphony?.source.useAllowedWords === 1 ? (
                    <Span
                        l10nKey="EditTab.Toolbox.DecodableReaderTool.AllowedWordsInThisStage"
                        css={css`
                            flex: 1;
                            // Keep the label off the sort buttons on its right.
                            padding-right: 12px;
                        `}
                    >
                        Allowed words in this stage
                    </Span>
                ) : (
                    <Span
                        l10nKey="EditTab.Toolbox.DecodableReaderTool.SampleWordsInThisStage"
                        css={css`
                            flex: 1;
                            // Keep the label off the sort buttons on its right.
                            padding-right: 12px;
                        `}
                    >
                        Sample words in this stage
                    </Span>
                )}
                <SortButton
                    sortType={0}
                    changeSortFunc={props.changeSortFunc}
                />
                <SortButton
                    sortType={1}
                    changeSortFunc={props.changeSortFunc}
                />
                {model.synphony?.source.useAllowedWords === 0 && (
                    <SortButton
                        sortType={2}
                        changeSortFunc={props.changeSortFunc}
                    />
                )}
            </div>
            <DecodableGrid
                direction="column"
                words={
                    model.synphony?.source.useAllowedWords === 1
                        ? model.getAllowedWordsSorted(model.stageNumber)
                        : model.getStageSightWordsSorted(model.stageNumber)
                }
            />
        </div>
    );
};

export const DecodableReaderToolControls: FunctionComponent = () => {
    const model = getTheOneReaderToolsModel();
    const [showTool, setShowTool] = useState<boolean>(
        isReaderToolEnabledOnCurrentPage(false),
    );

    const [, forceUpdate] = useReducer((x) => x + 1, 0);

    function updateState(): void {
        // If the current sort function is byFrequency when switching to
        // using allowed words, change the current sort function to
        // sortAlphabetically, since allowed words doesn't use frequency
        // sort.
        if (
            model.synphony?.source.useAllowedWords === 1 &&
            model.sort === "byFrequency"
        ) {
            model.sortAlphabetically();
        }

        // I include this line so that all the sort getter functions can
        // receive the correct graphemes for the current stage. Wihout this
        // line, the displaying of the graphemes and words will be one stage
        // behind. For example, if you are in stage 1 of 3 and go to stage 2
        // of 3, the tool will still display the data from stage 1. If you
        // then go to stage 3, it will then display the data from stage 2.
        model.stageGraphemes = model.getKnownGraphemes(model.stageNumber);
        forceUpdate();
    }

    // This useRef and useMountEffect handle assigning the updateState()
    // function, defined above, to the refreshFunc variable in
    // readerToolsModel.ts, so that this component updates and re-renders
    // whenever updateControlContents() is called in readerToolsModel.ts.
    //
    // This approach is intended to guard against potential problems where
    // model.refreshFunc may be called when it is undefined, which could
    // possibly lead to the component being out of sync with the data.
    // The chance of this happening is not super likely, but it is still
    // good to make sure that this possibility won't happen.
    const updateStateRef = useRef(updateState);
    updateStateRef.current = updateState;

    useMountEffect(() => {
        model.refreshFuncDecodable = () => updateStateRef.current();
        return () => {
            model.refreshFuncDecodable = undefined;
        };
    });

    function changeStage(increment: boolean): void {
        if (increment) {
            model.incrementStage();
        } else {
            model.decrementStage();
        }
        updateState();
    }

    function changeSortFunc(which: 0 | 1 | 2): void {
        switch (which) {
            case 0:
                model.sortAlphabetically();
                break;
            case 1:
                model.sortByLength();
                break;
            case 2:
                model.sortByFrequency();
                break;
            default:
        }
        forceUpdate();
    }

    return (
        <ThemeProvider theme={toolboxTheme}>
            <div
                css={css`
                    display: flex;
                    flex-direction: column;
                    height: 100%;
                    min-height: 0;
                    overflow-x: hidden;
                `}
            >
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        flex: 1 1 auto;
                        min-height: 0;
                        overflow-x: hidden;
                        // A single horizontal padding on the whole panel
                        // gives every row one shared left edge (matching
                        // the Leveled Reader), instead of the stage being
                        // hard against the left edge. (BL-16585)
                        padding: 8px 12px 0 12px;
                    `}
                >
                    {showTool && (
                        <>
                            <ReaderToolNav
                                isForLeveled={false}
                                changeFunction={changeStage}
                            />
                            {model.synphony?.source.useAllowedWords === 0 && (
                                <StageGraphemes />
                            )}
                            <SortedStageWords changeSortFunc={changeSortFunc} />
                            {model.getAllowedWordsAsObjects(model.stageNumber)
                                .length >= model.maxAllowedWords && (
                                <Span
                                    l10nKey="EditTab.Toolbox.DecodableReaderTool.AllowedWordListTruncated"
                                    l10nParam0={model.maxAllowedWords.toString()}
                                    css={css`
                                        padding: 8px 4px;
                                        background-color: ${kBloomDarkestBackground};
                                        color: red;
                                    `}
                                >
                                    {
                                        "Bloom can handle only the first {0} words."
                                    }
                                </Span>
                            )}
                        </>
                    )}
                    {/* Bottom group, top to bottom: Generate Report, the "Book is
                        Decodable" toggle, then the Set Up Stages button last.
                        Generate Report only appears when sampling words; allowed-
                        words mode has no report to generate. margin-top:auto pushes
                        the group to the bottom while the tool content shows; it
                        scrolls with the content when the panel overflows. The toggle
                        stays mounted even when the content is hidden so the user can
                        turn the book back into a decodable reader. */}
                    <div
                        css={css`
                            display: flex;
                            flex-direction: column;
                            align-items: flex-start;
                            gap: 8px;
                            margin-bottom: 10px;
                            ${showTool
                                ? "margin-top: auto; padding-top: 12px;"
                                : ""}
                        `}
                    >
                        {showTool &&
                            model.synphony?.source.useAllowedWords === 0 && (
                                <BloomButton
                                    l10nKey="EditTab.Toolbox.DecodableReaderTool.GenerateReport"
                                    l10nTipEnglishEnabled="Create and open a text file listing the letters and words available at each decodable stage."
                                    href="javascript:toolboxBundle.makeLetterWordList();"
                                    variant="text"
                                    enabled={true}
                                    hasText={true}
                                    iconBeforeText={
                                        <SummarizeOutlined
                                            css={css`
                                                // currentColor picks up the
                                                // button's accent text color.
                                                color: currentColor;
                                                font-size: 16px;
                                            `}
                                        />
                                    }
                                    css={readerTextButtonCss}
                                >
                                    Generate Report
                                </BloomButton>
                            )}
                        <ReaderToolSwitch
                            isForLeveled={false}
                            changeDisplayFunc={() =>
                                setShowTool((prev) => !prev)
                            }
                        />
                        {showTool && <ReaderSetupButton isForLeveled={false} />}
                    </div>
                </div>
            </div>
        </ThemeProvider>
    );
};
