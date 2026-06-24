import { FunctionComponent } from "react";
import { getTheOneReaderToolsModel } from "./readerToolsModel";
import BloomButton from "../../../react_components/bloomButton";
import { ArrowLeft, ArrowRight } from "@mui/icons-material";
import { css } from "@emotion/react";
import { Span } from "../../../react_components/l10nComponents";

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

    return (
        <div>
            <BloomButton
                iconBeforeText={
                    curPhaseNum() > 1 ? (
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
                disabled={curPhaseNum() <= 1}
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
                l10nKey="EditTab.Toolbox.DecodableReaderTool.StageNofM"
                l10nParam0={curPhaseNum().toString()}
                l10nParam1={numberOfPhases().toString()}
                css={css`
                    font-size: 21px;
                `}
            >
                {props.isForLeveled ? "Level {0} of {1}" : "Stage {0} of {1}"}
            </Span>
            <BloomButton
                iconBeforeText={
                    curPhaseNum() !== numberOfPhases() ? (
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
                disabled={curPhaseNum() === numberOfPhases()}
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
