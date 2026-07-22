import { FunctionComponent } from "react";
import { getTheOneReaderToolsModel } from "./readerToolsModel";
import BloomButton from "../../../react_components/bloomButton";
import { ArrowLeft, ArrowRight } from "@mui/icons-material";
import { css } from "@emotion/react";
import { Span } from "../../../react_components/l10nComponents";
import { kReaderAccent, kReaderMuted } from "./readerToolStyles";

// This component displays the phase navigation for both the
// decodable reader tool and the leveled reader tool (the part
// that says "Stage # of #" or "Level # of #"), and provides
// the functionality needed to be able to switch to a different
// phase
export const ReaderToolNav: FunctionComponent<{
    isForLeveled: boolean;
    changeFunction: (increment: boolean) => void;
}> = (props) => {
    const model = getTheOneReaderToolsModel();

    function numberOfPhases(): number {
        const total: number | undefined = props.isForLeveled
            ? model.getNumberOfLevels()
            : model.getNumberOfStages();
        // make sure we return a number to prevent any possible
        // errors. If the data needed to get the total number of
        // phases is not yet loaded, just use a 0
        return typeof total === "number" ? total : 0;
    }

    function curPhaseNum(): number {
        if (numberOfPhases() === 0) {
            return 0;
        }
        return props.isForLeveled ? model.levelNumber : model.stageNumber;
    }

    // Boxed square buttons at the right, matching the mockup. Unlike before,
    // the button at an end is shown but disabled (greyed) rather than hidden,
    // so the pair of boxes is always present. Kept fairly small so the whole
    // "Level N of M" line fits on a single row.
    const arrowButtonCss = css`
        // && beats MUI's .MuiButton-root sizing so the box stays square.
        && {
            min-width: unset;
            width: 26px;
            height: 26px;
            padding: 0;
            border: 1px solid ${kReaderAccent};
            border-radius: 4px;
            color: ${kReaderAccent};
        }
        // With no button text, MUI's startIcon margin would shove the arrow
        // off-center; zero it so the arrow sits in the middle of the box.
        & .MuiButton-startIcon {
            margin: 0;
        }
        &.Mui-disabled {
            opacity: 0.35;
        }
    `;

    return (
        <div
            css={css`
                display: flex;
                align-items: center;
                justify-content: space-between;
                margin-bottom: 8px;
            `}
        >
            {/* The level/stage indicator is two separately-styled localized
                pieces: the large "Level {0}" and the smaller, muted "of {1}".
                They were split from a single "Level {0} of {1}" string so each
                part can be styled; nowrap keeps them on one line. */}
            <div
                css={css`
                    display: flex;
                    align-items: baseline;
                    gap: 6px;
                    white-space: nowrap;
                `}
            >
                <Span
                    l10nKey={
                        props.isForLeveled
                            ? "EditTab.Toolbox.LeveledReaderTool.LevelNumber"
                            : "EditTab.Toolbox.DecodableReaderTool.StageNumber"
                    }
                    l10nParam0={curPhaseNum().toString()}
                    css={css`
                        font-size: 17px;
                        font-weight: bold;
                    `}
                >
                    {props.isForLeveled ? "Level {0}" : "Stage {0}"}
                </Span>
                <Span
                    l10nKey="EditTab.Toolbox.ReaderTools.OfCount"
                    l10nParam0={numberOfPhases().toString()}
                    css={css`
                        font-size: 11px;
                        color: ${kReaderMuted};
                    `}
                >
                    {"of {0}"}
                </Span>
            </div>
            <div
                css={css`
                    display: flex;
                    gap: 6px;
                `}
            >
                <BloomButton
                    iconBeforeText={
                        <ArrowLeft
                            css={css`
                                color: currentColor;
                            `}
                        />
                    }
                    variant="text"
                    disabled={curPhaseNum() <= 1}
                    l10nKey=""
                    hasText={false}
                    enabled={true}
                    onClick={() => props.changeFunction(false)}
                    css={arrowButtonCss}
                />
                <BloomButton
                    iconBeforeText={
                        <ArrowRight
                            css={css`
                                color: currentColor;
                            `}
                        />
                    }
                    variant="text"
                    disabled={curPhaseNum() === numberOfPhases()}
                    l10nKey=""
                    hasText={false}
                    enabled={true}
                    onClick={() => props.changeFunction(true)}
                    css={arrowButtonCss}
                />
            </div>
        </div>
    );
};
