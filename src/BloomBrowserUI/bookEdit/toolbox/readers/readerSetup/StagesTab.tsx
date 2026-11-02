import { css } from "@emotion/react";
import axios from "axios";
import * as React from "react";
import { useEffect, useState } from "react";
import { getToolboxBundleExports } from "../../../js/workspaceFrames";
import {
    getBloomApiPrefix,
    getWithConfigAsync,
} from "../../../../utils/bloomApi";
import { useMountEffect } from "../../../../utils/useMountEffect";
import { useL10n } from "../../../../react_components/l10nHooks";
import { Span } from "../../../../react_components/l10nComponents";
import { ReaderSettings, ReaderStage } from "../ReaderSettings";
import { kBloomBlue } from "../../../../utils/colorUtils";
import { MatchingWordsPanel } from "./MatchingWordsPanel";
import { StageLettersEditor } from "./StageLettersEditor";
import { StageAllowedWordsFile } from "./StageAllowedWordsFile";
import { ReaderDialogPhaseSection } from "./ReaderDialogPhaseSection";
import {
    cleanSpaceDelimitedList,
    cloneReaderSettings,
    hasOnlyKnownGraphemes,
} from "./decodableStagesUtils";
import { updateSettings } from "./readerDialogShared";

export const StagesTab: React.FunctionComponent<{
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

    // An effect is warranted here: the list of words Bloom knows lives in the toolbox frame's
    // Synphony data, not in this component's props, and it is filled in asynchronously after the
    // sample text files are read. Subscribing to that external store and dropping the
    // subscription on unmount is effect territory; the version counter exists only to re-run the
    // matching-words memo below when the store changes. See .github/skills/react-useeffect.
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

    // Drops from every stage any letter that is no longer in the alphabet.
    //
    // This one is an effect that reads props and writes state, which
    // .github/skills/react-useeffect names as something to avoid rather than something effects
    // are for. It is kept deliberately (reviewed 2026-08, BL-16607) because the legacy dialog
    // reconciled at effectively the same moments, so the behaviour is not a regression and
    // changing it would change what gets saved. What follows is what it costs, so that whoever
    // reads this next is deciding rather than discovering.
    //
    // It runs on every MOUNT of this tab, not once when the dialog opens: only the active tab is
    // rendered, so every switch away to Letters and back runs it again. So a teacher who removes
    // a letter from the alphabet -- replacing "ch" with "sh", say -- and comes back here before
    // finishing loses "ch" from every stage that taught it, silently. Re-adding "ch" to the
    // alphabet does NOT bring those stage assignments back; they have to be clicked in again, one
    // stage at a time, with nothing having said they went. There is no undo inside the dialog,
    // only Cancel, which throws away everything else too.
    //
    // It is also inconsistent about it: saving from the Letters tab WITHOUT coming back here
    // leaves the out-of-alphabet letters in the stages untouched. Whether a collection's stages
    // get rewritten therefore depends on which tabs the user happened to visit before pressing
    // OK, which is not something they can see or predict.
    //
    // The fix, if it is ever judged worth it, is the one the skill points at: derive the pruned
    // letters during render for display, and do the pruning for real once in
    // prepareSettingsForSave, where the drop-empty-stages rule already lives. Then nothing
    // mutates state on mount, the tab-switch surprise goes away, and what is saved stops
    // depending on where the user clicked.
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
    // Deleting the word list file happens NOW, not when the dialog is saved -- which is what
    // the legacy dialog did too, so this is deliberate rather than an oversight. The cost is
    // that pressing Cancel afterwards leaves the saved settings naming a file that is no longer
    // on disk: the settings were never rewritten, but the file is already gone.
    //
    // To improve on that, the dialog would collect the files the user has removed rather than
    // deleting them, and act on that list in prepareSettingsForSave/save -- deleting only the
    // ones no surviving stage references, and only once the user has committed to the change.
    // Cancel would then leave both the settings and the files untouched. Whoever does that
    // should handle the same case in removeSelectedStage (ReaderDialogPhaseSection), which
    // deletes on the same terms. (BL-16607)
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
                    <StageLettersEditor
                        stage={stage}
                        fontName={props.fontName}
                        allLetters={allLetters}
                        stageLetters={stageLetters}
                        previousLetters={previousLetters}
                        selectLetter={selectLetter}
                        updateSightWords={updateSightWords}
                        sightWordsBoxLabel={sightWordsBoxLabel}
                        setCurTab={props.setCurTab}
                    />
                ) : (
                    <StageAllowedWordsFile
                        stage={stage}
                        updateStage={updateStage}
                        removeAllowedWordsFile={removeAllowedWordsFile}
                    />
                )}
                <MatchingWordsPanel
                    matchingWords={matchingWords}
                    fontName={props.fontName}
                />
            </div>
        </div>
    );
};
