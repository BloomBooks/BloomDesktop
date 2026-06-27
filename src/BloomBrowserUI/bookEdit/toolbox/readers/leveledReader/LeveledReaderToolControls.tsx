import { FunctionComponent, useReducer, useRef, useState } from "react";
import { ReaderToolSwitch } from "../ReaderToolSwitch";
import { css, ThemeProvider } from "@emotion/react";
import { toolboxTheme } from "../../../../bloomMaterialUITheme";
import { getTheOneReaderToolsModel } from "../readerToolsModel";
import { isReaderToolEnabledOnCurrentPage } from "../readerToolPageState";
import BloomButton from "../../../../react_components/bloomButton";
import { ReaderToolNav } from "../ReaderToolNav";
import { Div } from "../../../../react_components/l10nComponents";
import { kBloomLightBlue } from "../../../../utils/colorUtils";
import { useMountEffect } from "../../../../utils/useMountEffect";
import { Link } from "../../../../react_components/link";

const DataRow: FunctionComponent<{
    l10nKeyInsert: string;
    maxNum: number;
    actualNum: number | string;
    children: string;
}> = (props) => {
    return (
        <div
            css={css`
                display: flex;
                align-items: flex-start;
                margin-left: 6px;
            `}
        >
            <Div
                l10nKey={`EditTab.Toolbox.LeveledReaderTool.${props.l10nKeyInsert}`}
                css={css`
                    flex: 0 0 83px;
                    min-width: 83px;
                    padding-right: 5px;
                `}
            >
                {props.children}
            </Div>
            <div
                css={css`
                    flex: 0 0 30px;
                    max-width: 30px;
                    padding-right: 3px;
                    overflow-wrap: break-word;
                    text-align: center;
                `}
            >
                {props.maxNum}
            </div>
            <div
                css={css`
                    flex: 0 0 40px;
                    max-width: 40px;
                    padding-right: 3px;
                    overflow-wrap: break-word;
                    text-align: center;
                `}
            >
                {props.actualNum}
            </div>
        </div>
    );
};

const HeaderRow: FunctionComponent = () => {
    return (
        <div
            css={css`
                display: flex;
                align-items: flex-start;
                margin-left: 6px;
            `}
        >
            <div
                css={css`
                    flex: 0 0 83px;
                    min-width: 83px;
                    padding-right: 5px;
                `}
            ></div>
            <Div
                l10nKey="EditTab.Toolbox.LeveledReaderTool.Max"
                css={css`
                    flex: 0 0 30px;
                    max-width: 30px;
                    padding-right: 3px;
                    text-align: center;
                `}
            >
                Max
            </Div>
            <Div
                l10nKey="EditTab.Toolbox.LeveledReaderTool.Actual"
                css={css`
                    flex: 0 0 40px;
                    max-width: 40px;
                    padding-right: 3px;
                    text-align: center;
                `}
            >
                Actual
            </Div>
        </div>
    );
};

const StatsSection: FunctionComponent<{
    l10nKeyInsert: string;
    divColor: string;
    children: React.ReactNode;
}> = (props) => {
    return (
        <>
            <Div
                l10nKey={`EditTab.Toolbox.LeveledReaderTool.${props.l10nKeyInsert}`}
                css={css`
                    color: ${props.divColor};
                `}
            />
            <HeaderRow />
            {props.children}
        </>
    );
};

function formatAverage(value: number): string {
    return value.toFixed(1);
}

const LeveledReaderStats: FunctionComponent = () => {
    const model = getTheOneReaderToolsModel();

    return (
        <>
            <Div
                l10nKey="EditTab.Toolbox.LeveledReaderTool.WordCounts"
                css={css`
                    color: ${kBloomLightBlue};
                `}
            />
            <StatsSection l10nKeyInsert="ThisPage" divColor="white">
                <DataRow
                    l10nKeyInsert="PerPage"
                    maxNum={model.maxWordsPerPage()}
                    actualNum={model.getElementsToCheck().getTotalWordCount()}
                >
                    per page
                </DataRow>
                <DataRow
                    l10nKeyInsert="PerSentence"
                    maxNum={model.maxWordsPerSentenceOnThisPage()}
                    actualNum={model
                        .getElementsToCheck()
                        .getMaxSentenceLength()}
                >
                    longest sentence
                </DataRow>
            </StatsSection>
            <StatsSection l10nKeyInsert="ThisBook" divColor="white">
                <DataRow
                    l10nKeyInsert="Total"
                    maxNum={model.maxWordsPerBook()}
                    actualNum={model.getTotalWordsInBook()}
                >
                    total
                </DataRow>
                <DataRow
                    l10nKeyInsert="PerPage"
                    maxNum={model.maxWordsPerPage()}
                    actualNum={model.getMaxWordsPerPageInBook()}
                >
                    per page
                </DataRow>
                <DataRow
                    l10nKeyInsert="Unique"
                    maxNum={model.maxUniqueWordsPerBook()}
                    actualNum={model.getUniqueWordsInBook()}
                >
                    unique
                </DataRow>
                <DataRow
                    l10nKeyInsert="MaxSentenceLength"
                    maxNum={model.maxWordsPerSentenceOnThisPage()}
                    actualNum={model.getMaxSentenceLengthInBook()}
                >
                    longest sentence
                </DataRow>
                <DataRow
                    l10nKeyInsert="Average"
                    maxNum={model.maxAverageWordsPerSentence()}
                    actualNum={formatAverage(
                        model.getAverageWordsPerSentenceInBook(),
                    )}
                >
                    avg per sentence
                </DataRow>
                <DataRow
                    l10nKeyInsert="AveragePerPage"
                    maxNum={model.maxAverageWordsPerPage()}
                    actualNum={formatAverage(
                        model.getAverageWordsPerPageInBook(),
                    )}
                >
                    avg per page
                </DataRow>
            </StatsSection>
            <StatsSection
                l10nKeyInsert="WordLengths"
                divColor={kBloomLightBlue}
            >
                <DataRow
                    l10nKeyInsert="ThisPageLC"
                    maxNum={model.maxGlyphsPerWord()}
                    actualNum={model.getCurrentPageMaxGlyphsPerWord()}
                >
                    this page
                </DataRow>
                <DataRow
                    l10nKeyInsert="MaxInBook"
                    maxNum={model.maxGlyphsPerWord()}
                    actualNum={model.getMaxGlyphsPerWordInBook()}
                >
                    max in book
                </DataRow>
                <DataRow
                    l10nKeyInsert="AverageInBook"
                    maxNum={model.maxAverageGlyphsPerWord()}
                    actualNum={formatAverage(
                        model.getAverageGlyphsPerWordInBook(),
                    )}
                >
                    avg in book
                </DataRow>
            </StatsSection>
            <StatsSection
                l10nKeyInsert="SentenceCounts"
                divColor={kBloomLightBlue}
            >
                <DataRow
                    l10nKeyInsert="ThisPageLC"
                    maxNum={model.maxSentencesPerPage()}
                    actualNum={model.getCurrentPageSentenceCount()}
                >
                    this page
                </DataRow>
                <DataRow
                    l10nKeyInsert="TotalInBook"
                    maxNum={model.maxSentencesPerBook()}
                    actualNum={model.getTotalSentencesInBook()}
                >
                    total in book
                </DataRow>
                <DataRow
                    l10nKeyInsert="AverageInBook"
                    maxNum={model.maxAverageSentencesPerPage()}
                    actualNum={formatAverage(
                        model.getAverageSentencesPerPageInBook(),
                    )}
                >
                    avg in book
                </DataRow>
            </StatsSection>
        </>
    );
};

const LeveledReaderList: FunctionComponent<{
    isLinkList: boolean;
    l10nKeyInsert: string;
}> = (props) => {
    const listItems = !props.isLinkList
        ? getTheOneReaderToolsModel().getLevelReminders()
        : [
              "Vocabulary",
              "Formatting",
              "Predictability",
              "Illustration Support",
              "Choice of Topic",
          ];
    const attributeInserts = props.isLinkList
        ? [
              "Vocabulary",
              "Formatting",
              "Predictability",
              "IllustrationSupport",
              "ChoiceOfTopic",
          ]
        : [];
    return (
        <>
            <Div
                l10nKey={`EditTab.Toolbox.LeveledReaderTool.${props.l10nKeyInsert}`}
                css={css`
                    color: ${kBloomLightBlue};
                `}
            />
            <ul>
                {listItems.map((item, index) => (
                    <li key={index}>
                        {!props.isLinkList ? (
                            item
                        ) : (
                            <Link
                                l10nKey={`EditTab.Toolbox.LeveledReaderTool.${attributeInserts[index]}`}
                                href={`api/externalLink?path=leveledRTInfo/leveledReaderInfo-en.html&fragment=${attributeInserts[index]}`}
                                css={css`
                                    text-decoration: underline;
                                `}
                            >
                                {item}
                            </Link>
                        )}
                    </li>
                ))}
            </ul>
        </>
    );
};

const LeveledReminders: FunctionComponent = () => {
    const model = getTheOneReaderToolsModel();
    const reminders = model.synphony
        ? model.synphony.getLevels()[model.levelNumber - 1].thingsToRemember
        : [];

    return (
        <div>
            <Div
                l10nKey="EditTab.Toolbox.LeveledReaderTool.FoThisLevel"
                css={css`
                    color: ${kBloomLightBlue};
                `}
            >
                For this Level
            </Div>
            <ul>
                {reminders.map((thingToRemember, index) => (
                    <li key={index}>{thingToRemember}</li>
                ))}
            </ul>
        </div>
    );
};

const LeveledLinks: FunctionComponent = () => {
    return (
        <div>
            <Div l10nKey="EditTab.Toolbox.LeveledReaderTool.KeepInMind">
                Keep in mind
            </Div>
            <ul>
                <li>
                    <Link
                        l10nKey="EditTab.Toolbox.LeveledReaderTool.Vocabulary"
                        href="api/externalLink?path=leveledRTInfo/leveledReaderInfo-en.html&fragment=Vocabulary"
                        css={css`
                            text-decoration: underline;
                        `}
                    >
                        Vocabulary
                    </Link>
                </li>
                <li>
                    <Link
                        l10nKey="EditTab.Toolbox.LeveledReaderTool.Formatting"
                        href="api/externalLink?path=leveledRTInfo/leveledReaderInfo-en.html&fragment=Formatting"
                        css={css`
                            text-decoration: underline;
                        `}
                    >
                        Formatting
                    </Link>
                </li>
                <li>
                    <Link
                        l10nKey="EditTab.Toolbox.LeveledReaderTool.Predictability"
                        href="api/externalLink?path=leveledRTInfo/leveledReaderInfo-en.html&fragment=Predictability"
                        css={css`
                            text-decoration: underline;
                        `}
                    >
                        Predictability
                    </Link>
                </li>
                <li>
                    <Link
                        l10nKey="EditTab.Toolbox.LeveledReaderTool.IllustrationSupport"
                        href="api/externalLink?path=leveledRTInfo/leveledReaderInfo-en.html&fragment=IllustrationSupport"
                        css={css`
                            text-decoration: underline;
                        `}
                    >
                        Illustration Support
                    </Link>
                </li>
                <li>
                    <Link
                        l10nKey="EditTab.Toolbox.LeveledReaderTool.ChoiceOfTopic"
                        href="api/externalLink?path=leveledRTInfo/leveledReaderInfo-en.html&fragment=ChoiceOfTopic"
                        css={css`
                            text-decoration: underline;
                        `}
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
        forceUpdate();
    }

    function changeLevel(increment: boolean): void {
        if (increment) {
            model.incrementLevel();
        } else {
            model.decrementLevel();
        }
        updateState();
    }

    const updateStateRef = useRef(updateState);
    updateStateRef.current = updateState;

    // This mount effect synchronizes this React component with the external reader tools model refresh callback.
    useMountEffect(() => {
        model.refreshFunc = () => updateStateRef.current();
        return () => {
            model.refreshFunc = undefined;
        };
    });

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
                                padding-right: 2px;
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
                                Set Up Levels
                            </BloomButton>
                        </div>
                        <ReaderToolNav
                            isForLeveled={true}
                            changeFunction={changeLevel}
                        />
                        <LeveledReaderStats />
                        <LeveledReaderList
                            isLinkList={false}
                            l10nKeyInsert="FoThisLevel"
                        />
                        <LeveledReaderList
                            isLinkList={true}
                            l10nKeyInsert="KeepInMind"
                        />
                    </div>
                )}
                <ReaderToolSwitch
                    isForLeveled={true}
                    changeDisplayFunc={() => setShowTool((prev) => !prev)}
                    css={css`
                        margin-left: 50px;
                    `}
                />
            </div>
        </ThemeProvider>
    );
};
