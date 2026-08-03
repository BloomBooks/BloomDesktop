import { css } from "@emotion/react";
import DeleteOutlineIcon from "@mui/icons-material/DeleteOutline";
import InsertDriveFileOutlinedIcon from "@mui/icons-material/InsertDriveFileOutlined";
import { Chip, IconButton, RadioGroup, Tab, Tabs } from "@mui/material";
import axios from "axios";
import * as React from "react";
import { useEffect, useState } from "react";
import { getToolboxBundleExports } from "../../../js/workspaceFrames";
import {
    get,
    getBloomApiPrefix,
    getWithConfigAsync,
    post,
} from "../../../../utils/bloomApi";
import { useMountEffect } from "../../../../utils/useMountEffect";
import { useL10n } from "../../../../react_components/l10nHooks";
import { Div, Span } from "../../../../react_components/l10nComponents";
import BloomButton from "../../../../react_components/bloomButton";
import { ReaderSettings, ReaderStage } from "../ReaderSettings";
import { kBloomBlue, kBloomRed } from "../../../../utils/colorUtils";
import { ReaderDialogPhaseSection } from "./ReaderDialogPhaseSection";
import {
    cleanSpaceDelimitedList,
    cloneReaderSettings,
    hasOnlyKnownGraphemes,
} from "./decodableStagesUtils";
import { Link } from "../../../../react_components/link";
import { MuiRadio } from "../../../../react_components/muiRadio";
import { BloomTooltip } from "../../../../react_components/BloomToolTip";
import { ReaderDialogTextarea } from "./ReaderDialogTextarea";

const kMutedText = "#707477";

const commonTextStyles = css`
    color: ${kMutedText};
    font-size: 9pt;
`;

const commonHeaderStyles = css`
    color: ${kMutedText};
    font-size: 8pt;
    font-weight: 700;
    letter-spacing: 0.03em;
    text-transform: uppercase;
`;

/** Applies a tab change to a cloned settings object and publishes the updated draft. */
const updateSettings = (
    props: {
        settings: ReaderSettings;
        setSettings: (value: ReaderSettings) => void;
    },
    update: (settings: ReaderSettings) => void,
) => {
    const updatedSettings = cloneReaderSettings(props.settings);
    update(updatedSettings);
    props.setSettings(updatedSettings);
};

export const DecodableStagesSetup: React.FunctionComponent<{
    settings: ReaderSettings;
    setSettings: React.Dispatch<React.SetStateAction<ReaderSettings>>;
    fontName: string;
    maxAllowedWords: number;
}> = (props) => {
    const [curTab, setCurTab] = useState(2);
    const [selectedStageIndex, setSelectedStageIndex] = useState(0);

    const lettersTab = useL10n("Letters", "ReaderSetup.Letters");
    const sampleWordsTab = useL10n("Sample Words", "ReaderSetup.SampleWords");
    const stagesTab = useL10n(
        "Decodable Stages",
        "ReaderSetup.DecodableStages",
    );
    const tabsLabel = useL10n(
        "Set up Decodable Reader Tool",
        "ReaderSetup.SetUpDecodableReaderTool",
    );

    let activeTab: React.ReactNode;
    if (curTab === 0) {
        activeTab = (
            <LettersTab
                settings={props.settings}
                setSettings={props.setSettings}
                fontName={props.fontName}
            />
        );
    } else if (curTab === 1) {
        activeTab = (
            <SampleWordsTab
                settings={props.settings}
                setSettings={props.setSettings}
                fontName={props.fontName}
            />
        );
    } else {
        activeTab = (
            <StagesTab
                settings={props.settings}
                setSettings={props.setSettings}
                setCurTab={setCurTab}
                fontName={props.fontName}
                curStageIndex={selectedStageIndex}
                setCurStageIndex={setSelectedStageIndex}
                maxAllowedWords={props.maxAllowedWords}
            />
        );
    }

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                flex: 1 1 auto;
                height: 100%;
                min-height: 0;
                margin: -20px -24px;
                background: #f4f5f5;
            `}
        >
            <Tabs
                value={curTab}
                // onChange (rather than onClick on each Tab) is what lets MUI's own
                // arrow-key navigation actually change tabs, not just move focus.
                onChange={(_event, newTab: number) => setCurTab(newTab)}
                aria-label={tabsLabel}
                css={css`
                    min-height: 50px;
                    padding: 0 6px;
                    background: white;
                    border-bottom: 1px solid #e5e5e5;
                    .MuiTab-root {
                        min-height: 50px;
                        min-width: 0;
                        padding: 0 22px;
                        font-size: 15px;
                        text-transform: none;
                        font-weight: 600;
                    }
                    .Mui-selected {
                        color: ${kBloomBlue} !important;
                    }
                    .MuiTabs-indicator {
                        background-color: ${kBloomBlue};
                    }
                `}
            >
                <Tab label={lettersTab} />
                <Tab label={sampleWordsTab} />
                <Tab label={stagesTab} />
            </Tabs>
            <div
                css={css`
                    flex: 1 1 auto;
                    min-height: 0;
                    margin: 24px;
                    background: white;
                    border: 1px solid #dddddd;
                    border-radius: 8px;
                    box-shadow: 0 1px 2px rgb(0 0 0 / 8%);
                    overflow: hidden;
                `}
            >
                {activeTab}
            </div>
        </div>
    );
};

const LettersTab: React.FunctionComponent<{
    settings: ReaderSettings;
    setSettings: (value: ReaderSettings) => void;
    fontName: string;
}> = (props) => {
    const updateLetters = (value: string) => {
        updateSettings(props, (updatedSettings) => {
            updatedSettings.letters = value;
        });
    };
    const lettersBoxLabel = useL10n(
        "Letters and Letter Combinations",
        "ReaderSetup.Letters.Header",
    );
    return (
        <div
            css={css`
                grid-column: 1 / -1;
                min-width: 0;
                padding: 22px;
                box-sizing: border-box;
            `}
        >
            <Div
                l10nKey="ReaderSetup.Letters.Header"
                css={css`
                    margin-bottom: 7px;
                    ${commonHeaderStyles}
                `}
            >
                Letters and Letter Combinations
            </Div>
            <ReaderDialogTextarea
                updateSettings={updateLetters}
                value={props.settings.letters}
                ariaLabel={lettersBoxLabel}
                extraStyles={css`
                    display: block;
                    width: 325px;
                    height: 55px;
                    font-family: ${props.fontName};
                `}
            />
            <Div
                css={css`
                    margin-top: 7px;
                    margin-bottom: 24px;
                    ${commonTextStyles}
                `}
                l10nKey="ReaderSetup.Letters.Intro"
            >
                To help you make decodable readers, Bloom needs to know the
                letters and letter combinations that you will be teaching.
            </Div>
            <Div
                l10nKey="ReaderSetup.Letters.LetterHelp1"
                css={css`
                    max-width: 720px;
                    margin-bottom: 4px;
                    line-height: 1.45;
                    ${commonTextStyles}
                `}
            >
                Separate each letter or letter combination with a space. For
                example, here is what we might use for the English language:
            </Div>
            <div
                css={css`
                    max-width: 720px;
                    margin-bottom: 24px;
                    line-height: 1.45;
                    ${commonTextStyles}
                `}
            >
                a b c ch d e f g h i j k l m n ng o p q r s sh t th u v w x y z
                ' -
            </div>
            <div
                css={css`
                    max-width: 720px;
                    margin-bottom: 24px;
                    line-height: 1.45;
                    ${commonTextStyles}
                `}
            >
                <Span l10nKey="ReaderSetup.Letters.LetterHelp2">
                    Notice that the English list includes symbols that are used
                    to make words, like ' in&nbsp;
                </Span>
                <Span
                    l10nKey="ReaderSetup.Letters.LetterHelp3"
                    css={css`
                        font-style: italic;
                    `}
                >
                    it's
                </Span>
                <Span l10nKey="ReaderSetup.Letters.LetterHelp4">.</Span>
            </div>
            <Div
                l10nKey="ReaderSetup.Letters.LetterHelp5"
                css={css`
                    max-width: 720px;
                    line-height: 1.45;
                    ${commonTextStyles}
                `}
            >
                Do not include punctuation in this list. Bloom does not support
                the inclusion of punctuation in decodable stages.
            </Div>
        </div>
    );
};

const SampleWordsTab: React.FunctionComponent<{
    settings: ReaderSettings;
    setSettings: (value: ReaderSettings) => void;
    fontName: string;
}> = (props) => {
    const [sampleTextFiles, setSampleTextFiles] = useState<
        { path: string; readable: boolean; hasExtension: boolean }[]
    >([]);

    useMountEffect(() => {
        let isMounted = true;
        const listenerName = "sampleTextFiles.DecodableReaderSetup";
        const toolbox = getToolboxBundleExports();
        if (!toolbox) {
            throw new Error(
                "The Reader toolbox must be loaded before its setup dialog.",
            );
        }

        const refreshSampleTextFiles = () => {
            get("readers/ui/sampleTextsList", (result) => {
                if (isMounted) {
                    // Every file is listed, including ones Bloom cannot read; those are shown
                    // with an explanation instead of being silently dropped. The toolbox does
                    // the classifying so this agrees with the files Bloom actually loads.
                    setSampleTextFiles(
                        toolbox.classifySampleTextFiles(
                            result.data
                                .split("\r")
                                .filter((path: string) => path),
                        ),
                    );
                }
            });
        };

        // This subscription synchronizes React state with Bloom's non-React Sample Texts folder watcher.
        refreshSampleTextFiles();
        toolbox.addSampleTextFilesChangedListener(
            listenerName,
            refreshSampleTextFiles,
        );
        window.addEventListener("focus", refreshSampleTextFiles);

        return () => {
            isMounted = false;
            toolbox.removeSampleTextFilesChangedListener(listenerName);
            window.removeEventListener("focus", refreshSampleTextFiles);
        };
    });

    const updateMoreWords = (value: string) => {
        updateSettings(props, (updatedSettings) => {
            updatedSettings.moreWords = value;
        });
    };
    const moreWordsBoxLabel = useL10n(
        "1) Type Words Here",
        "ReaderSetup.Words.TypeWordsHere",
    );

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                height: 100%;
                min-height: 0;
            `}
        >
            <RadioGroup
                value={
                    props.settings.useAllowedWords === 0
                        ? "lettersSightWords"
                        : "allowedWords"
                }
                onChange={(event) => {
                    updateSettings(props, (updatedSettings) => {
                        updatedSettings.useAllowedWords =
                            event.target.value === "lettersSightWords" ? 0 : 1;
                    });
                }}
                css={css`
                    margin: 20px 24px 16px;
                    gap: 0;
                    .MuiFormControlLabel-label {
                        line-height: 1.4;
                        ${commonTextStyles}
                    }
                `}
            >
                <MuiRadio
                    value="allowedWords"
                    label="We are using lists of allowed words to define stages"
                    l10nKey="ReaderSetup.Words.UseAllowedWords"
                />
                <MuiRadio
                    value="lettersSightWords"
                    label="We are using letters with sight words to define stages"
                    l10nKey="ReaderSetup.Words.UseLetters"
                />
            </RadioGroup>
            {props.settings.useAllowedWords === 0 && (
                <>
                    <Div
                        l10nKey="ReaderSetup.Words.Intro"
                        css={css`
                            margin: 0 24px 18px;
                            line-height: 1.4;
                            ${commonTextStyles}
                        `}
                    >
                        To help you make decodable readers, Bloom can suggest
                        words that fit within the current stage. There are two
                        ways to give words to Bloom:
                    </Div>
                    <div
                        css={css`
                            display: grid;
                            grid-template-columns: minmax(300px, 40%) minmax(
                                    0,
                                    1fr
                                );
                            flex: 1 1 auto;
                            min-height: 0;
                            border-top: 1px solid #e2e4e6;
                        `}
                    >
                        <div
                            css={css`
                                display: flex;
                                flex-direction: column;
                                min-width: 0;
                                min-height: 0;
                                padding: 22px 24px;
                                border-right: 1px solid #e2e4e6;
                            `}
                        >
                            <Div
                                l10nKey="ReaderSetup.Words.TypeWordsHere"
                                css={css`
                                    margin-bottom: 8px;
                                    ${commonHeaderStyles}
                                `}
                            >
                                1) Type Words Here
                            </Div>
                            <ReaderDialogTextarea
                                updateSettings={updateMoreWords}
                                value={props.settings.moreWords}
                                ariaLabel={moreWordsBoxLabel}
                                extraStyles={css`
                                    flex: 1 1 auto;
                                    min-height: 100px;
                                    width: 100%;
                                    font-family: ${props.fontName};
                                `}
                            />
                        </div>
                        <div
                            css={css`
                                display: flex;
                                flex-direction: column;
                                min-width: 0;
                                min-height: 0;
                                padding: 22px 24px;
                            `}
                        >
                            <div
                                css={css`
                                    margin-bottom: 8px;
                                    ${commonHeaderStyles}
                                `}
                            >
                                <Span l10nKey="ReaderSetup.Words.PlaceTextFiles">
                                    2) Place Text Files in Your
                                </Span>{" "}
                                <Link
                                    l10nKey="ReaderSetup.Words.SampleTextFolder"
                                    onClick={() =>
                                        post("readers/ui/openTextsFolder")
                                    }
                                    css={css`
                                        font-size: inherit;
                                        font-weight: inherit;
                                    `}
                                >
                                    Sample Texts Folder
                                </Link>
                            </div>
                            <div
                                css={css`
                                    flex: 1 1 auto;
                                    min-height: 0;
                                    border: 1px solid #d8dce0;
                                    border-radius: 6px;
                                    background: #fbfbfb;
                                    overflow: auto;
                                    line-height: 1.5;
                                    ${commonTextStyles}
                                `}
                            >
                                {sampleTextFiles.length === 0 && (
                                    <Div
                                        l10nKey="ReaderSetup.NoSampleTextsYet"
                                        css={css`
                                            padding: 12px;
                                        `}
                                    >
                                        No sample texts yet. Add text files to
                                        the Sample Texts folder.
                                    </Div>
                                )}
                                {sampleTextFiles.map((file) => (
                                    <div
                                        key={file.path}
                                        css={css`
                                            display: flex;
                                            align-items: center;
                                            min-width: 0;
                                            min-height: 40px;
                                            padding: 0 12px;
                                            border-bottom: 1px solid #e2e4e6;
                                        `}
                                    >
                                        <InsertDriveFileOutlinedIcon
                                            aria-hidden="true"
                                            css={css`
                                                flex: none;
                                                margin-right: 10px;
                                                color: #8a949d;
                                                font-size: 19px;
                                            `}
                                        />
                                        <div
                                            css={css`
                                                overflow: hidden;
                                                text-overflow: ellipsis;
                                                white-space: nowrap;
                                            `}
                                        >
                                            {file.path.split(/[\\/]/).pop()}
                                        </div>
                                        {/* Say why a file is being ignored, rather than
                                            hiding it and leaving the user to wonder. */}
                                        {!file.readable && (
                                            <Span
                                                l10nKey={
                                                    file.hasExtension
                                                        ? "ReaderSetup.FormatNotSupported"
                                                        : "ReaderSetup.FileNeedsTxtExtension"
                                                }
                                                css={css`
                                                    flex: none;
                                                    margin-left: 10px;
                                                    font-style: italic;
                                                    color: ${kBloomRed};
                                                `}
                                            >
                                                {file.hasExtension
                                                    ? "Cannot read this format"
                                                    : "File needs .TXT extension"}
                                            </Span>
                                        )}
                                    </div>
                                ))}
                            </div>
                            {sampleTextFiles.some((file) => !file.readable) && (
                                <Link
                                    l10nKey="ReaderSetup.HowToExport"
                                    href="/bloom/api/help?topic=Tasks/Edit_tasks/Decodable_Reader_Tool/Language_tab.htm"
                                    css={css`
                                        margin-top: 8px;
                                        font-size: inherit;
                                        font-weight: inherit;
                                    `}
                                >
                                    Help exporting and converting files to use
                                    as sample texts
                                </Link>
                            )}
                        </div>
                    </div>
                </>
            )}
        </div>
    );
};

const StagesTab: React.FunctionComponent<{
    settings: ReaderSettings;
    setSettings: (value: ReaderSettings) => void;
    setCurTab: (value: number) => void;
    fontName: string;
    curStageIndex: number;
    setCurStageIndex: (value: number) => void;
    maxAllowedWords: number;
}> = (props) => {
    const [wordListVersion, setWordListVersion] = useState(0);
    const [allowedWords, setAllowedWords] = useState<string[]>([]);

    useMountEffect(() => {
        const listenerName = "matchingWords.DecodableReaderSetup";
        const toolbox = getToolboxBundleExports();
        if (!toolbox) {
            throw new Error(
                "The Reader toolbox must be loaded before its setup dialog.",
            );
        }

        // The reader model invokes this only after it has finished loading sample-text words.
        toolbox.addWordListChangedListener(listenerName, () => {
            setWordListVersion((version) => version + 1);
        });

        return () => toolbox.removeWordListChangedListener(listenerName);
    });

    // Drops from every stage any letter that is no longer in the alphabet. Note this runs on
    // every mount, and only the active tab is rendered, so it also runs each time the user
    // leaves the Letters tab and comes back here — which means a letter mid-way through being
    // retyped over there can get pruned out of the stages. Reviewed 2026-08 (BL-16607) and
    // deliberately left alone: the legacy dialog reconciles at effectively the same moments, so
    // this is not a regression, and making it precise is not worth the added complexity.
    useMountEffect(() => {
        const configuredLetters = new Set(
            cleanSpaceDelimitedList(props.settings.letters)
                .split(/\s+/)
                .filter(Boolean),
        );

        const updatedSettings = cloneReaderSettings(props.settings);
        let changed = false;

        for (const stage of updatedSettings.stages) {
            const filteredLetters = cleanSpaceDelimitedList(stage.letters)
                .split(/\s+/)
                .filter((letter) => configuredLetters.has(letter))
                .join(" ");

            if (stage.letters !== filteredLetters) {
                stage.letters = filteredLetters;
                changed = true;
            }
        }

        if (changed) {
            props.setSettings(updatedSettings);
        }
    });

    const stage = props.settings.stages[props.curStageIndex]!;
    const stageLetters = new Set(stage.letters.split(" ").filter(Boolean));

    // Allowed-word files live outside React, so fetch their server-cleaned contents whenever the selected stage changes.
    useEffect(() => {
        if (props.settings.useAllowedWords !== 1) {
            return;
        }

        let isCurrent = true;
        const fileNames = props.settings.stages
            .slice(0, props.curStageIndex + 1)
            .map((oneStage) => oneStage.allowedWordsFile)
            .filter(Boolean);

        void Promise.all(
            fileNames.map((fileName) =>
                getWithConfigAsync<string>("readers/io/allowedWordsList", {
                    params: { fileName },
                }),
            ),
        ).then((responses) => {
            if (!isCurrent) {
                return;
            }

            setAllowedWords(
                Array.from(
                    new Set(
                        responses
                            .flatMap(
                                (response) => response?.data.split(",") ?? [],
                            )
                            .map((word) => word.trim())
                            .filter(Boolean),
                    ),
                )
                    .sort((firstWord, secondWord) =>
                        firstWord.localeCompare(secondWord),
                    )
                    .slice(0, props.maxAllowedWords),
            );
        });

        return () => {
            isCurrent = false;
        };
    }, [props.settings, props.curStageIndex]);

    const allLetters = cleanSpaceDelimitedList(props.settings.letters)
        .split(/\s+/)
        .filter(Boolean);

    const matchingWords = React.useMemo(() => {
        if (props.settings.useAllowedWords === 1) {
            return allowedWords;
        }

        const knownGpcs = props.settings.stages
            .slice(0, props.curStageIndex + 1)
            .flatMap((oneStage) => oneStage.letters.split(/\s+/))
            .filter(Boolean);
        const sightWords = props.settings.stages
            .slice(0, props.curStageIndex + 1)
            .flatMap((oneStage) =>
                cleanSpaceDelimitedList(oneStage.sightWords).split(/\s+/),
            )
            .filter(Boolean);
        const toolbox = getToolboxBundleExports();
        if (!toolbox) {
            throw new Error(
                "The Reader toolbox must be loaded before its setup dialog.",
            );
        }
        // Only the toolbox frame's Synphony data knows these, so they have to come from there.
        const alwaysMatchSymbols = toolbox.getSynphonyAlwaysMatchSymbols();
        const typedSampleWords = cleanSpaceDelimitedList(
            props.settings.moreWords,
        )
            .split(/\s+/)
            .filter(Boolean)
            .filter((word) =>
                hasOnlyKnownGraphemes(
                    word,
                    allLetters,
                    knownGpcs,
                    alwaysMatchSymbols,
                ),
            );
        const sampleTextMatchingWords =
            knownGpcs.length === 0
                ? []
                : toolbox.getDecodableStageMatchingWords(knownGpcs);

        return Array.from(
            new Set([
                ...sampleTextMatchingWords,
                ...sightWords,
                ...typedSampleWords,
            ]),
        ).sort((firstWord, secondWord) => firstWord.localeCompare(secondWord));
    }, [allowedWords, props.settings, props.curStageIndex, wordListVersion]);

    const previousLetters = new Set(
        props.settings.stages
            .slice(0, props.curStageIndex)
            .flatMap((previousStage) => previousStage.letters.split(" ")),
    );

    const updateStage = (change: (updatedStage: ReaderStage) => void) => {
        updateSettings(props, (updatedSettings) => {
            change(updatedSettings.stages[props.curStageIndex]!);
        });
    };

    const updateSightWords = (value: string) => {
        updateStage((updatedStage) => {
            updatedStage.sightWords = value;
        });
    };

    /** Removes an unshared allowed-words file after clearing this stage's reference. */
    const removeAllowedWordsFile = () => {
        const fileName = stage.allowedWordsFile;
        updateStage((updatedStage) => {
            updatedStage.allowedWordsFile = "";
        });

        const fileIsUsedByAnotherStage = props.settings.stages.some(
            (oneStage, index) =>
                index !== props.curStageIndex &&
                oneStage.allowedWordsFile === fileName,
        );
        if (!fileIsUsedByAnotherStage) {
            axios.delete(`${getBloomApiPrefix()}readers/io/allowedWordsList`, {
                params: { fileName },
            });
        }
    };

    const selectLetter = (letter: string) => {
        if (previousLetters.has(letter)) {
            return;
        }
        updateStage((updatedStage) => {
            const updatedLetters = new Set(updatedStage.letters.split(" "));
            if (updatedLetters.has(letter)) {
                updatedLetters.delete(letter);
            } else {
                updatedLetters.add(letter);
            }
            updatedStage.letters = allLetters
                .filter((knownLetter) => updatedLetters.has(knownLetter))
                .join(" ");
        });
    };

    const tooltip = useL10n(
        "Remove from this stage",
        "ReaderSetup.RemoveWordList",
    );
    const sightWordsBoxLabel = useL10n(
        "New Sight Words",
        "ReaderSetup.SightWordLabel",
    );

    return (
        <div
            css={css`
                display: grid;
                grid-template-columns: minmax(280px, 34%) minmax(0, 1fr);
                height: 100%;
            `}
        >
            <ReaderDialogPhaseSection
                settings={props.settings}
                setSettings={props.setSettings}
                selectedStageIndex={props.curStageIndex}
                setSelectedStageIndex={props.setCurStageIndex}
                fontName={props.fontName}
            />
            <div
                css={css`
                    display: grid;
                    grid-template-columns: minmax(300px, 65%) minmax(0, 1fr);
                    grid-template-rows: 56px 1fr;
                    min-width: 0;
                    min-height: 0;
                `}
            >
                <div
                    css={css`
                        grid-column: 1 / -1;
                        display: flex;
                        align-items: center;
                        padding: 0 22px;
                        border-bottom: 1px solid #e5e5e5;
                        font-size: 14pt;
                        font-weight: 600;
                    `}
                >
                    <span
                        css={css`
                            width: 8px;
                            height: 8px;
                            margin-right: 10px;
                            border-radius: 50%;
                            background: ${kBloomBlue};
                        `}
                    />{" "}
                    <Span l10nKey="ReaderSetup.StageLabel">Stage</Span>{" "}
                    {props.curStageIndex + 1}
                </div>
                {props.settings.useAllowedWords == 0 ? (
                    <div
                        css={css`
                            min-width: 0;
                            padding: 22px;
                        `}
                    >
                        <div
                            css={css`
                                margin-bottom: 7px;
                                ${commonHeaderStyles}
                            `}
                        >
                            <Span l10nKey="ReaderSetup.SightWordLabel">
                                New Sight Words
                            </Span>
                        </div>
                        <ReaderDialogTextarea
                            updateSettings={updateSightWords}
                            value={stage.sightWords}
                            ariaLabel={sightWordsBoxLabel}
                            extraStyles={css`
                                display: block;
                                width: 325px;
                                height: 35px;
                                font-family: ${props.fontName};
                            `}
                        />
                        <div
                            css={css`
                                margin-top: 7px;
                                margin-bottom: 20px;
                                ${commonTextStyles}
                            `}
                        >
                            <Span l10nKey="ReaderSetup.SeparateWordsWithSpaces">
                                Separate words with spaces.
                            </Span>
                        </div>
                        <div
                            css={css`
                                margin-bottom: 10px;
                                ${commonHeaderStyles}
                            `}
                        >
                            <Span l10nKey="ReaderSetup.SelectedLetters">
                                Previous and New Letters
                            </Span>
                        </div>
                        {allLetters.length === 0 ? (
                            <div
                                css={css`
                                    margin: 20px 0;
                                    ${commonTextStyles}
                                `}
                            >
                                <Span l10nKey="ReaderSetup.FirstSetupAlphabet">
                                    First,
                                </Span>{" "}
                                <Link
                                    l10nKey="ReaderSetup.SetupAlphabet"
                                    onClick={() => props.setCurTab(0)}
                                >
                                    set up the alphabet for this language.
                                </Link>
                            </div>
                        ) : (
                            <div
                                css={css`
                                    display: grid;
                                    grid-template-columns: repeat(7, 46px);
                                    gap: 8px;
                                `}
                            >
                                {allLetters.map((letter) => {
                                    const isPrevious =
                                        previousLetters.has(letter);
                                    const isCurrent = stageLetters.has(letter);
                                    let textColor = "#b7bec5";
                                    if (isCurrent) {
                                        textColor = "white";
                                    } else if (isPrevious) {
                                        textColor = kBloomBlue;
                                    }
                                    return (
                                        <button
                                            key={letter}
                                            onClick={() => selectLetter(letter)}
                                            css={css`
                                                width: 46px;
                                                height: 46px;
                                                border: ${isCurrent ||
                                                isPrevious
                                                    ? `1px solid ${kBloomBlue}`
                                                    : "1px solid #e2e5e7"};
                                                border-radius: 6px;
                                                background: ${isCurrent
                                                    ? kBloomBlue
                                                    : "white"};
                                                color: ${textColor};
                                                cursor: ${isPrevious
                                                    ? "default"
                                                    : "pointer"};
                                                font-family: ${props.fontName};
                                                font-size: 14pt;
                                                overflow: hidden;
                                            `}
                                        >
                                            {letter}
                                        </button>
                                    );
                                })}
                            </div>
                        )}
                        <div
                            css={css`
                                margin-top: 12px;
                                ${commonTextStyles}
                            `}
                        >
                            <Span l10nKey="ReaderSetup.ClickLetter">
                                Click on letters to add them to this stage.
                            </Span>
                        </div>
                    </div>
                ) : (
                    <div
                        css={css`
                            min-width: 0;
                            padding: 22px;
                        `}
                    >
                        <div
                            css={css`
                                margin-bottom: 7px;
                                ${commonHeaderStyles}
                            `}
                        >
                            <Span l10nKey="ReaderSetup.AllowedWordsFile">
                                Allowed Words File
                            </Span>
                        </div>
                        {stage.allowedWordsFile === "" ? (
                            <BloomButton
                                l10nKey="ReaderSetup.ChooseAllowedWordsFile"
                                hasText={true}
                                enabled={true}
                                variant="outlined"
                                onClick={() =>
                                    get(
                                        "readers/ui/chooseAllowedWordsListFile",
                                        (result) => {
                                            if (result.data) {
                                                updateStage((updatedStage) => {
                                                    updatedStage.allowedWordsFile =
                                                        result.data;
                                                });
                                            }
                                        },
                                    )
                                }
                            >
                                Choose...
                            </BloomButton>
                        ) : (
                            <div
                                css={css`
                                    display: flex;
                                    align-items: center;
                                    min-height: 42px;
                                    box-sizing: border-box;
                                    border: 1px solid #e1e4e6;
                                    border-radius: 7px;
                                    ${commonTextStyles}
                                `}
                            >
                                <InsertDriveFileOutlinedIcon
                                    css={css`
                                        margin: 0 12px;
                                        color: #8a949d;
                                        font-size: 19px;
                                    `}
                                />
                                <span
                                    css={css`
                                        min-width: 0;
                                        overflow: hidden;
                                        text-overflow: ellipsis;
                                        white-space: nowrap;
                                    `}
                                >
                                    {stage.allowedWordsFile}
                                </span>
                                <div
                                    css={css`
                                        margin-left: auto;
                                        margin-right: 4px;
                                    `}
                                >
                                    <BloomTooltip
                                        tip={tooltip}
                                        placement="top-end"
                                    >
                                        <IconButton
                                            aria-label="Remove allowed words file"
                                            onClick={removeAllowedWordsFile}
                                            css={css`
                                                color: #858a8e;
                                                .MuiSvgIcon-root {
                                                    font-size: 18px;
                                                }
                                            `}
                                        >
                                            <DeleteOutlineIcon />
                                        </IconButton>
                                    </BloomTooltip>
                                </div>
                            </div>
                        )}
                    </div>
                )}
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        min-width: 0;
                        min-height: 0;
                        background: #fafafa;
                        border-left: 1px solid #e5e5e5;
                    `}
                >
                    <strong
                        css={css`
                            flex: 0 0 auto;
                            padding: 22px 22px 0;
                        `}
                    >
                        <span
                            css={css`
                                color: ${kBloomBlue};
                            `}
                        >
                            {matchingWords.length}{" "}
                        </span>
                        <Span l10nKey="ReaderSetup.MatchingWords">
                            matching words
                        </Span>
                    </strong>
                    <div
                        css={css`
                            flex: 1 1 auto;
                            min-height: 0;
                            overflow: auto;
                            margin-top: 15px;
                        `}
                    >
                        <div
                            css={css`
                                display: flex;
                                flex-wrap: wrap;
                                align-content: flex-start;
                                gap: 8px;
                                min-width: 100%;
                                min-height: 100%;
                                box-sizing: border-box;
                                padding: 0 22px 22px;
                            `}
                        >
                            {matchingWords.map((word) => (
                                <Chip
                                    key={word}
                                    label={word}
                                    css={css`
                                        height: auto;
                                        padding: 4px 10px;
                                        border-radius: 16px;
                                        background: #f1f3f4;
                                        color: #4a4a4a;
                                        font-family: ${props.fontName};
                                        font-size: 11pt;
                                        .MuiChip-label {
                                            padding: 0;
                                        }
                                    `}
                                />
                            ))}
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
};
