import { css } from "@emotion/react";
import InsertDriveFileOutlinedIcon from "@mui/icons-material/InsertDriveFileOutlined";
import { RadioGroup } from "@mui/material";
import * as React from "react";
import { useState } from "react";
import { getToolboxBundleExports } from "../../../js/workspaceFrames";
import { get, post } from "../../../../utils/bloomApi";
import { useMountEffect } from "../../../../utils/useMountEffect";
import { useL10n } from "../../../../react_components/l10nHooks";
import { Div, Span } from "../../../../react_components/l10nComponents";
import { ReaderSettings } from "../ReaderSettings";
import { kBloomRed } from "../../../../utils/colorUtils";
import { Link } from "../../../../react_components/link";
import { MuiRadio } from "../../../../react_components/muiRadio";
import { ReaderDialogTextarea } from "./ReaderDialogTextarea";
import {
    commonHeaderStyles,
    commonTextStyles,
    updateSettings,
} from "./readerDialogShared";

export const SampleWordsTab: React.FunctionComponent<{
    settings: ReaderSettings;
    setSettings: (value: ReaderSettings) => void;
    fontName: string;
}> = (props) => {
    const [sampleTextFiles, setSampleTextFiles] = useState<
        { path: string; readable: boolean; hasExtension: boolean }[]
    >([]);

    // An effect is warranted here: this synchronizes React state with three things outside React
    // -- Bloom's api for the folder listing, the toolbox frame's non-React watcher on the Sample
    // Texts folder, and the window's focus event (the folder can change while Bloom is in the
    // background). Subscribing to an external source and unsubscribing on unmount is exactly
    // what effects are for; there is no way to derive this during render. See
    // .github/skills/react-useeffect.
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
                                testId="reader-setup-sample-words-box"
                                onValueChange={updateMoreWords}
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
                                        id="readerSetupNoSampleTexts"
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
                                        data-testid="reader-setup-sample-text-file"
                                        data-readable={file.readable}
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
                        </div>
                    </div>
                </>
            )}
        </div>
    );
};
