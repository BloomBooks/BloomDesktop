import { FunctionComponent, useReducer, useRef, useState } from "react";
import { css, ThemeProvider } from "@emotion/react";
import { ArrowLeft, ArrowRight } from "@mui/icons-material";
import ContentCopy from "@mui/icons-material/ContentCopy";
import BloomButton from "../../../../react_components/bloomButton";
import { Link } from "../../../../react_components/link";
import { Span } from "../../../../react_components/l10nComponents";
import { toolboxTheme } from "../../../../bloomMaterialUITheme";
import {
    kBloomDarkestBackground,
    kBloomLightBlue,
} from "../../../../utils/colorUtils";
import { useMountEffect } from "../../../../utils/useMountEffect";
import { ReaderToolSwitch } from "../ReaderToolSwitch";
import { isReaderToolEnabledOnCurrentPage } from "../readerToolPageState";
import { getTheOneReaderToolsModel } from "../readerToolsModel";

const unlimitedClassName = "disabledLimit";

const statTableStyles = css`
    margin-left: 6px;
    margin-top: 7px;
    border-collapse: collapse;
`;

const sectionLabelStyles = css`
    color: ${kBloomLightBlue};
    padding-top: 6px;
`;

const StatCell: FunctionComponent<{
    id?: string;
    className: string;
    value?: number | string;
}> = (props) => {
    const isUnlimited = props.value === 0;
    return (
        <td
            id={props.id}
            className={`${props.className}${isUnlimited ? ` ${unlimitedClassName}` : ""}`}
        >
            {isUnlimited ? "" : props.value}
        </td>
    );
};

const HeaderRow: FunctionComponent<{ l10nKey: string; children: string }> = (
    props,
) => {
    return (
        <tr>
            <td className="section" colSpan={3} css={sectionLabelStyles}>
                <Span l10nKey={props.l10nKey}>{props.children}</Span>
            </td>
        </tr>
    );
};

const MaxActualHeaderRow: FunctionComponent<{ titleKey?: string }> = (
    props,
) => {
    return (
        <>
            {props.titleKey && (
                <tr>
                    <td
                        className="tableTitle"
                        colSpan={3}
                        css={css`
                            padding-top: 4px;
                        `}
                    >
                        <Span l10nKey={props.titleKey}>
                            {props.titleKey.endsWith("ThisPage")
                                ? "This Page"
                                : "This Book"}
                        </Span>
                    </td>
                </tr>
            )}
            <tr>
                <td className="statistics-label"></td>
                <td className="statistics-max">
                    <Span l10nKey="EditTab.Toolbox.LeveledReaderTool.Max">
                        Max
                    </Span>
                </td>
                <td className="statistics-actual">
                    <Span l10nKey="EditTab.Toolbox.LeveledReaderTool.Actual">
                        Actual
                    </Span>
                </td>
            </tr>
        </>
    );
};

const StatRow: FunctionComponent<{
    labelKey: string;
    label: string;
    maxId: string;
    actualId: string;
    maxValue: number;
}> = (props) => {
    return (
        <tr>
            <td className="statistics-label">
                <Span l10nKey={props.labelKey}>{props.label}</Span>
            </td>
            <StatCell
                id={props.maxId}
                className="statistics-max"
                value={props.maxValue}
            />
            <StatCell
                id={props.actualId}
                className="statistics-actual"
                value="-"
            />
        </tr>
    );
};

const getCurrentLevelThingsToRemember = (): string[] => {
    const model = getTheOneReaderToolsModel();
    const levels = model.synphony?.getLevels() ?? [];
    return levels[model.levelNumber - 1]?.thingsToRemember ?? [];
};

const LeveledReaderNav: FunctionComponent<{
    changeFunction: (increment: boolean) => void;
}> = (props) => {
    const model = getTheOneReaderToolsModel();
    const levels = model.synphony?.getLevels() ?? [];
    const currentLevelNumber = levels.length === 0 ? 0 : model.levelNumber;
    const currentLevelLabel =
        currentLevelNumber === 0
            ? "0"
            : (levels[currentLevelNumber - 1]?.getName() ?? "0");

    return (
        <div>
            <BloomButton
                iconBeforeText={
                    currentLevelNumber > 1 ? (
                        <ArrowLeft
                            css={css`
                                color: white;
                            `}
                        />
                    ) : (
                        <></>
                    )
                }
                variant="text"
                disabled={currentLevelNumber <= 1}
                l10nKey=""
                hasText={false}
                enabled={true}
                onClick={() => props.changeFunction(false)}
                css={css`
                    width: 6px;
                    min-width: unset;
                    height: 18px;
                    margin-left: 5px;
                    margin-bottom: 8px;
                    padding-left: 10px;
                    padding-right: 3px;
                `}
            />
            <Span
                l10nKey="EditTab.Toolbox.LeveledReaderTool.LevelNofM"
                l10nParam0={currentLevelLabel}
                l10nParam1={levels.length.toString()}
                css={css`
                    font-size: 21px;
                `}
            >
                Level {0} of {1}
            </Span>
            <BloomButton
                iconBeforeText={
                    currentLevelNumber !== levels.length ? (
                        <ArrowRight
                            css={css`
                                color: white;
                            `}
                        />
                    ) : (
                        <></>
                    )
                }
                variant="text"
                disabled={currentLevelNumber === levels.length}
                l10nKey=""
                hasText={false}
                enabled={true}
                onClick={() => props.changeFunction(true)}
                css={css`
                    width: 16px;
                    min-width: unset;
                    height: 18px;
                    padding-left: 12px;
                    padding-right: 1px;
                    margin-bottom: 8px;
                `}
            />
        </div>
    );
};

const LeveledReaderStatistics: FunctionComponent = () => {
    const model = getTheOneReaderToolsModel();

    return (
        <>
            <table className="statistics clear" css={statTableStyles}>
                <tbody>
                    <HeaderRow l10nKey="EditTab.Toolbox.LeveledReaderTool.WordCounts">
                        Word Counts
                    </HeaderRow>
                    <MaxActualHeaderRow titleKey="EditTab.Toolbox.LeveledReaderTool.ThisPage" />
                    <StatRow
                        labelKey="EditTab.Toolbox.LeveledReaderTool.PerPage"
                        label="per page"
                        maxId="maxWordsPerPage"
                        actualId="actualWordsPerPage"
                        maxValue={model.maxWordsPerPage()}
                    />
                    <StatRow
                        labelKey="EditTab.Toolbox.LeveledReaderTool.PerSentence"
                        label="longest sentence"
                        maxId="maxWordsPerSentence"
                        actualId="actualWordsPerSentence"
                        maxValue={model.maxWordsPerSentenceOnThisPage()}
                    />
                </tbody>
            </table>
            <table className="statistics clear" css={statTableStyles}>
                <tbody>
                    <MaxActualHeaderRow titleKey="EditTab.Toolbox.LeveledReaderTool.ThisBook" />
                    <StatRow
                        labelKey="EditTab.Toolbox.LeveledReaderTool.Total"
                        label="total"
                        maxId="maxWordsPerBook"
                        actualId="actualWordCount"
                        maxValue={model.maxWordsPerBook()}
                    />
                    <StatRow
                        labelKey="EditTab.Toolbox.LeveledReaderTool.PerPage"
                        label="per page"
                        maxId="maxWordsPerPageBook"
                        actualId="actualWordsPerPageBook"
                        maxValue={model.maxWordsPerPage()}
                    />
                    <StatRow
                        labelKey="EditTab.Toolbox.LeveledReaderTool.Unique"
                        label="unique"
                        maxId="maxUniqueWordsPerBook"
                        actualId="actualUniqueWords"
                        maxValue={model.maxUniqueWordsPerBook()}
                    />
                    <StatRow
                        labelKey="EditTab.Toolbox.LeveledReaderTool.MaxSentenceLength"
                        label="longest sentence"
                        maxId="maxWordsPerSentenceInBook"
                        actualId="actualMaxWordsPerSentence"
                        maxValue={model.maxWordsPerSentenceOnThisPage()}
                    />
                    <StatRow
                        labelKey="EditTab.Toolbox.LeveledReaderTool.Average"
                        label="avg per sentence"
                        maxId="maxAverageWordsPerSentence"
                        actualId="actualAverageWordsPerSentence"
                        maxValue={model.maxAverageWordsPerSentence()}
                    />
                    <StatRow
                        labelKey="EditTab.Toolbox.LeveledReaderTool.AveragePerPage"
                        label="avg per page"
                        maxId="maxAverageWordsPerPage"
                        actualId="actualAverageWordsPerPage"
                        maxValue={model.maxAverageWordsPerPage()}
                    />
                </tbody>
            </table>
            <table className="statistics clear" css={statTableStyles}>
                <tbody>
                    <HeaderRow l10nKey="EditTab.Toolbox.LeveledReaderTool.WordLengths">
                        Word Lengths
                    </HeaderRow>
                    <MaxActualHeaderRow />
                    <StatRow
                        labelKey="EditTab.Toolbox.LeveledReaderTool.ThisPageLC"
                        label="this page"
                        maxId="maxLettersPerWord"
                        actualId="actualLettersPerWord"
                        maxValue={model.maxGlyphsPerWord()}
                    />
                    <StatRow
                        labelKey="EditTab.Toolbox.LeveledReaderTool.MaxInBook"
                        label="max in book"
                        maxId="maxGlyphsPerWordInBook"
                        actualId="actualMaxGlyphsPerWord"
                        maxValue={model.maxGlyphsPerWord()}
                    />
                    <StatRow
                        labelKey="EditTab.Toolbox.LeveledReaderTool.AverageInBook"
                        label="avg in book"
                        maxId="maxAverageGlyphsPerWord"
                        actualId="actualAverageGlyphsPerWord"
                        maxValue={model.maxAverageGlyphsPerWord()}
                    />
                </tbody>
            </table>
            <table className="statistics clear" css={statTableStyles}>
                <tbody>
                    <HeaderRow l10nKey="EditTab.Toolbox.LeveledReaderTool.SentenceCounts">
                        Sentence Counts
                    </HeaderRow>
                    <MaxActualHeaderRow />
                    <StatRow
                        labelKey="EditTab.Toolbox.LeveledReaderTool.ThisPageLC"
                        label="this page"
                        maxId="maxSentencesPerPage"
                        actualId="actualSentencesPerPage"
                        maxValue={model.maxSentencesPerPage()}
                    />
                    <StatRow
                        labelKey="EditTab.Toolbox.LeveledReaderTool.TotalInBook"
                        label="total in book"
                        maxId="maxSentencesPerBook"
                        actualId="actualSentenceCount"
                        maxValue={model.maxSentencesPerBook()}
                    />
                    <StatRow
                        labelKey="EditTab.Toolbox.LeveledReaderTool.AverageInBook"
                        label="avg in book"
                        maxId="maxAverageSentencesPerPage"
                        actualId="actualAverageSentencesPerPage"
                        maxValue={model.maxAverageSentencesPerPage()}
                    />
                </tbody>
            </table>
        </>
    );
};

const LeveledReaderReminders: FunctionComponent = () => {
    const thingsToRemember = getCurrentLevelThingsToRemember();

    return (
        <div
            className="section"
            css={css`
                clear: both;
                margin-left: 8px;
                padding-top: 4px;
            `}
        >
            <Span l10nKey="EditTab.Toolbox.LeveledReaderTool.FoThisLevel">
                For this Level
            </Span>
            <ul id="thingsToRemember">
                {thingsToRemember.map((thingToRemember, index) => (
                    <li
                        key={index}
                        dangerouslySetInnerHTML={{ __html: thingToRemember }}
                    ></li>
                ))}
            </ul>
        </div>
    );
};

const LeveledReaderLinks: FunctionComponent = () => {
    return (
        <div
            className="section"
            id="keepInMindLinks"
            css={css`
                margin-left: 8px;
            `}
        >
            <Span l10nKey="EditTab.Toolbox.LeveledReaderTool.KeepInMind">
                Keep in mind
            </Span>
            <ul>
                <li>
                    <Link
                        l10nKey="EditTab.Toolbox.LeveledReaderTool.Vocabulary"
                        href="api/externalLink?path=leveledRTInfo/leveledReaderInfo-en.html&fragment=Vocabulary"
                    >
                        Vocabulary
                    </Link>
                </li>
                <li>
                    <Link
                        l10nKey="EditTab.Toolbox.LeveledReaderTool.Formatting"
                        href="api/externalLink?path=leveledRTInfo/leveledReaderInfo-en.html&fragment=Formatting"
                    >
                        Formatting
                    </Link>
                </li>
                <li>
                    <Link
                        l10nKey="EditTab.Toolbox.LeveledReaderTool.Predictability"
                        href="api/externalLink?path=leveledRTInfo/leveledReaderInfo-en.html&fragment=Predictability"
                    >
                        Predictability
                    </Link>
                </li>
                <li>
                    <Link
                        l10nKey="EditTab.Toolbox.LeveledReaderTool.IllustrationSupport"
                        href="api/externalLink?path=leveledRTInfo/leveledReaderInfo-en.html&fragment=IllustrationSupport"
                    >
                        Illustration Support
                    </Link>
                </li>
                <li>
                    <Link
                        l10nKey="EditTab.Toolbox.LeveledReaderTool.ChoiceOfTopic"
                        href="api/externalLink?path=leveledRTInfo/leveledReaderInfo-en.html&fragment=ChoiceOfTopic"
                    >
                        Choice of Topic
                    </Link>
                </li>
            </ul>
        </div>
    );
};

export const LeveledReaderToolControls: FunctionComponent = () => {
    const model = getTheOneReaderToolsModel();
    const [showTool, setShowTool] = useState<boolean>(
        isReaderToolEnabledOnCurrentPage(true),
    );
    const [, forceUpdate] = useReducer((x) => x + 1, 0);

    function updateState(): void {
        setShowTool(isReaderToolEnabledOnCurrentPage(true));
        forceUpdate();
    }

    const updateStateRef = useRef(updateState);
    updateStateRef.current = updateState;

    // This mount effect synchronizes this React component with the external reader tools model refresh callback.
    useMountEffect(() => {
        model.refreshFunc = () => updateStateRef.current();
        model.updateControlContents();
        return () => {
            model.refreshFunc = undefined;
        };
    });

    function changeLevel(increment: boolean): void {
        if (increment) {
            model.incrementLevel();
        } else {
            model.decrementLevel();
        }
        updateState();
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
                {showTool && (
                    <div
                        id="leveled-reader-tool-content"
                        css={css`
                            display: flex;
                            flex-direction: column;
                            flex: 1 1 auto;
                            min-height: 0;
                            overflow-x: hidden;
                        `}
                    >
                        <div
                            css={css`
                                display: flex;
                                margin-left: auto;
                                margin-bottom: 10px;
                                padding-right: 10px;
                            `}
                        >
                            <BloomButton
                                href="javascript:window.toolboxBundle.showSetupDialog('levels');"
                                l10nKey="EditTab.Toolbox.LeveledReaderTool.SetUpLevels"
                                variant="text"
                                enabled={true}
                                hasText={true}
                                enabledImageFile="/bloom/bookEdit/toolbox/readers/edit-white.png"
                                css={css`
                                    font-size: xx-small;
                                    text-decoration: underline;
                                    font-weight: normal;
                                    height: 22px;
                                    img {
                                        height: 14px;
                                        margin-right: 2px;
                                        margin-bottom: 2px;
                                    }
                                    &:hover {
                                        text-decoration: underline;
                                    }
                                `}
                            >
                                Set up Levels
                            </BloomButton>
                        </div>
                        <LeveledReaderNav changeFunction={changeLevel} />
                        <LeveledReaderStatistics />
                        <LeveledReaderReminders />
                        <LeveledReaderLinks />
                        <BloomButton
                            l10nKey="EditTab.Toolbox.LeveledReaderTool.CopyBookStatistics"
                            variant="text"
                            enabled={true}
                            hasText={true}
                            iconBeforeText={
                                <ContentCopy
                                    css={css`
                                        color: white;
                                        width: 20px;
                                        height: 20px;
                                    `}
                                />
                            }
                            onClick={() =>
                                model.copyLeveledReaderStatsToClipboard()
                            }
                            css={css`
                                align-self: flex-start;
                                margin: 10px;
                                padding: 2px 4px;
                                color: white;
                                text-transform: uppercase;
                                background-color: transparent;
                                &:hover {
                                    background-color: black;
                                }
                            `}
                        >
                            Copy Book Stats
                        </BloomButton>
                    </div>
                )}
                <div id="leveled-reader-tool-toggle-react-container">
                    <ReaderToolSwitch
                        isForLeveled={true}
                        changeDisplayFunc={() => setShowTool((prev) => !prev)}
                        css={css`
                            margin-left: 50px;
                            background-color: ${kBloomDarkestBackground};
                        `}
                    />
                </div>
            </div>
        </ThemeProvider>
    );
};
