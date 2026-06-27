import {
    FunctionComponent,
    useEffect,
    useLayoutEffect,
    useReducer,
    useRef,
    useState,
} from "react";
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
import { ContentCopySharp } from "@mui/icons-material";

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
                margin-top: 4px;
            `}
        >
            <Div
                l10nKey={`EditTab.Toolbox.LeveledReaderTool.${props.l10nKeyInsert}`}
                css={css`
                    flex: 0 0 83px;
                    min-width: 83px;
                    padding-right: 5px;
                    align-self: center;
                `}
            >
                {props.children}
            </Div>
            <div
                css={css`
                    flex: 0 0 40px;
                    max-width: 40px;
                    padding-right: 3px;
                    overflow-wrap: break-word;
                    text-align: center;
                `}
            >
                {props.maxNum !== 0 ? props.maxNum : ""}
            </div>
            <div
                css={css`
                    flex: 0 0 40px;
                    max-width: 40px;
                    padding-right: 3px;
                    overflow-wrap: break-word;
                    text-align: center;
                    color: ${(props.actualNum as number) > props.maxNum &&
                    props.maxNum !== 0
                        ? "orange"
                        : "lightgreen"};
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
                    flex: 0 0 40px;
                    max-width: 40px;
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
        <div
            css={css`
                margin-bottom: 25px;
            `}
        >
            <Div
                l10nKey={`EditTab.Toolbox.LeveledReaderTool.${props.l10nKeyInsert}`}
                css={css`
                    color: ${props.divColor};
                    margin-bottom: 2px;
                `}
            />
            <HeaderRow />
            {props.children}
        </div>
    );
};

function formatAverage(value: number): string {
    return value.toFixed(1);
}

const LeveledReaderStats: FunctionComponent = () => {
    const model = getTheOneReaderToolsModel();

    return (
        <div
            css={css`
                margin-left: 8px;
                margin-top: 20px;
            `}
        >
            <Div
                l10nKey="EditTab.Toolbox.LeveledReaderTool.WordCounts"
                css={css`
                    color: ${kBloomLightBlue};
                    margin-bottom: 3px;
                `}
            />
            <StatsSection l10nKeyInsert="ThisPage" divColor="white">
                <DataRow
                    l10nKeyInsert="PerPage"
                    maxNum={model.maxWordsPerPage()}
                    actualNum={0}
                >
                    per page
                </DataRow>
                <DataRow
                    l10nKeyInsert="PerSentence"
                    maxNum={model.maxWordsPerSentenceOnThisPage()}
                    actualNum={0}
                >
                    longest sentence
                </DataRow>
            </StatsSection>
            <StatsSection l10nKeyInsert="ThisBook" divColor="white">
                <DataRow
                    l10nKeyInsert="Total"
                    maxNum={model.maxWordsPerBook()}
                    actualNum={0}
                >
                    total
                </DataRow>
                <DataRow
                    l10nKeyInsert="PerPage"
                    maxNum={model.maxWordsPerPage()}
                    actualNum={0}
                >
                    per page
                </DataRow>
                <DataRow
                    l10nKeyInsert="Unique"
                    maxNum={model.maxUniqueWordsPerBook()}
                    actualNum={0}
                >
                    unique
                </DataRow>
                <DataRow
                    l10nKeyInsert="MaxSentenceLength"
                    maxNum={model.maxWordsPerSentenceOnThisPage()}
                    actualNum={0}
                >
                    longest sentence
                </DataRow>
                <DataRow
                    l10nKeyInsert="Average"
                    maxNum={model.maxAverageWordsPerSentence()}
                    actualNum={0}
                >
                    avg per sentence
                </DataRow>
                <DataRow
                    l10nKeyInsert="AveragePerPage"
                    maxNum={model.maxAverageWordsPerPage()}
                    actualNum={0}
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
                    actualNum={0}
                >
                    this page
                </DataRow>
                <DataRow
                    l10nKeyInsert="MaxInBook"
                    maxNum={model.maxGlyphsPerWord()}
                    actualNum={0}
                >
                    max in book
                </DataRow>
                <DataRow
                    l10nKeyInsert="AverageInBook"
                    maxNum={model.maxAverageGlyphsPerWord()}
                    actualNum={0}
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
                    actualNum={0}
                >
                    this page
                </DataRow>
                <DataRow
                    l10nKeyInsert="TotalInBook"
                    maxNum={model.maxSentencesPerBook()}
                    actualNum={0}
                >
                    total in book
                </DataRow>
                <DataRow
                    l10nKeyInsert="AverageInBook"
                    maxNum={model.maxAverageSentencesPerPage()}
                    actualNum={0}
                >
                    avg in book
                </DataRow>
            </StatsSection>
        </div>
    );
};

const LeveledReaderList: FunctionComponent<{
    isLinkList: boolean;
    l10nKeyInsert: string;
}> = (props) => {
    const listItems = props.isLinkList
        ? [
              "Vocabulary",
              "Formatting",
              "Predictability",
              "Illustration Support",
              "Choice Of Topic",
          ]
        : getTheOneReaderToolsModel().getLevelReminders();
    return (
        <div
            css={css`
                padding-left: 8px;
                margin-top: 15px;
            `}
        >
            <Div
                l10nKey={`EditTab.Toolbox.LeveledReaderTool.${props.l10nKeyInsert}`}
                css={css`
                    color: ${kBloomLightBlue};
                `}
            />
            <ul
                css={css`
                    padding-left: 16px;
                    margin-top: 5px;
                `}
            >
                {listItems.map((item, index) => (
                    <li key={index}>
                        {!props.isLinkList ? (
                            item
                        ) : (
                            <Link
                                l10nKey={`EditTab.Toolbox.LeveledReaderTool.${item.replace(/\s/g, "")}`}
                                href={`api/externalLink?path=leveledRTInfo/leveledReaderInfo-en.html&fragment=${item.replace(/\s/g, "")}`}
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
        model.refreshFuncLeveled = () => updateStateRef.current();
        return () => {
            model.refreshFuncLeveled = undefined;
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
                        <div
                            css={css`
                                display: flex;
                                margin-right: auto;
                                margin-left: 8px;
                                margin-bottom: 10px;
                            `}
                        >
                            <BloomButton
                                l10nKey="EditTab.Toolbox.LeveledReaderTool.CopyBookStatistics"
                                variant="text"
                                enabled={true}
                                hasText={true}
                                iconBeforeText={
                                    <ContentCopySharp
                                        css={css`
                                            color: white;
                                        `}
                                    />
                                }
                                onClick={() =>
                                    model.copyLeveledReaderStatsToClipboard()
                                }
                                css={css`
                                    text-transform: uppercase;
                                    color: white;
                                    font-weight: normal;
                                    border-radius: 0;
                                    text-align: left;

                                    &:hover {
                                        background-color: black;
                                    }

                                    &:active {
                                        background-color: black;
                                        transform: translateY(2px);
                                    }
                                `}
                            >
                                Copy Book Stats
                            </BloomButton>
                        </div>
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
