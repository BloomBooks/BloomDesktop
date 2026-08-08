import { FunctionComponent } from "react";
import { css } from "@emotion/react";
import BloomButton from "../../../react_components/bloomButton";
import { readerContainedButtonCss } from "./readerToolStyles";

// Shared "Set Up Stages/Levels" outline button shown along the bottom of both
// reader tool panels. It opens the corresponding setup dialog, differing only
// in the stage-vs-level wording. (BL-16585)
export const ReaderSetupButton: FunctionComponent<{
    isForLeveled: boolean;
    /**
     * Supply this to open something other than the legacy jQuery setup dialog. The Decodable
     * Reader passes it so its converted React dialog is used instead (BL-16607); the Leveled
     * Reader has not been converted yet and so still falls back to the href below.
     */
    onClick?: () => void;
}> = (props) => {
    let legacyDialogHref: string | undefined;
    if (props.onClick) {
        legacyDialogHref = undefined; // the caller is opening its own dialog
    } else if (props.isForLeveled) {
        legacyDialogHref =
            "javascript:window.toolboxBundle.showSetupDialog('levels');";
    } else {
        legacyDialogHref =
            "javascript:window.toolboxBundle.showSetupDialog('stages');";
    }
    return (
        <BloomButton
            href={legacyDialogHref}
            onClick={props.onClick}
            l10nKey={
                props.isForLeveled
                    ? "EditTab.Toolbox.LeveledReaderTool.SetUpLevels"
                    : "EditTab.Toolbox.DecodableReaderTool.SetUpStages"
            }
            variant="text"
            enabled={true}
            hasText={true}
            iconBeforeText={
                // The same white tool icon shown in the accordion header (steps
                // for Leveled, keys for Decodable), rendered as a background image
                // so it matches the header exactly. It reads cleanly as white on
                // the contained button's blue fill. (BL-16585)
                <span
                    css={css`
                        width: 16px;
                        height: 16px;
                        display: inline-block;
                        background-position: center;
                        background-repeat: no-repeat;
                        background-size: contain;
                        flex-shrink: 0;
                    `}
                    style={{
                        backgroundImage: `url(${
                            props.isForLeveled
                                ? "/bloom/images/steps-white.png"
                                : "/bloom/images/keys-white.png"
                        })`,
                    }}
                />
            }
            css={readerContainedButtonCss}
        >
            {props.isForLeveled ? "Set Up Levels" : "Set Up Stages"}
        </BloomButton>
    );
};
