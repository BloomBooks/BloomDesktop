import { css } from "@emotion/react";
import { get } from "../../../../utils/bloomApi";
import DeleteOutlineIcon from "@mui/icons-material/DeleteOutline";
import InsertDriveFileOutlinedIcon from "@mui/icons-material/InsertDriveFileOutlined";
import { IconButton } from "@mui/material";
import * as React from "react";
import { Span } from "../../../../react_components/l10nComponents";
import { useL10n } from "../../../../react_components/l10nHooks";
import BloomButton from "../../../../react_components/bloomButton";
import { BloomTooltip } from "../../../../react_components/BloomToolTip";
import { ReaderStage } from "../ReaderSettings";
import { commonHeaderStyles, commonTextStyles } from "./readerDialogShared";

/**
 * The Decodable Stages tab as it looks when the collection defines its stages by lists of
 * allowed words rather than by letters: choose a word list file for this stage, or drop the one
 * it has.
 *
 * Removing a file deletes it from the collection straight away rather than on save; see
 * removeAllowedWordsFile in StagesTab for why that is deliberate and what changing it would take.
 */
export const StageAllowedWordsFile: React.FunctionComponent<{
    stage: ReaderStage;
    allowedWords: string[];
    updateStage: (change: (updatedStage: ReaderStage) => void) => void;
    removeAllowedWordsFile: () => void;
}> = (props) => {
    const removeTooltip = useL10n(
        "Remove from this stage",
        "ReaderSetup.RemoveWordList",
    );
    return (
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
                        {props.stage.allowedWordsFile === "" ? (
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
                                                props.updateStage((updatedStage) => {
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
                                    {props.stage.allowedWordsFile}
                                </span>
                                <div
                                    css={css`
                                        margin-left: auto;
                                        margin-right: 4px;
                                    `}
                                >
                                    <BloomTooltip
                                        tip={removeTooltip}
                                        placement="top-end"
                                    >
                                        <IconButton
                                            aria-label="Remove allowed words file"
                                            onClick={props.removeAllowedWordsFile}
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
);
};
