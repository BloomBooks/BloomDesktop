import React = require("react");
import Add from "@material-ui/icons/Add";
import Remove from "@material-ui/icons/Remove";
import Typography from "@material-ui/core/Typography";
import "./playbackOrderControls.less";

interface IPlaybackOrderControlsProps {
    sizeOfList: number;
    myOrderNum: number; // NOT zero-based; first number should be "1"
    bumpUp: (whichPositionToBump: number) => void; // increase 'myOrderNum' (Add button)
    bumpDown: (whichPositionToBump: number) => void; // decrease 'myOrderNum' (Remove button)
}

const disabledColor: string = "#b0dee4"; // bloom-lightblue in bloomUI.less

const PlaybackOrderControls: React.FC<IPlaybackOrderControlsProps> = props => {
    const leftButtonDisabled = props.myOrderNum === 1;
    const rightButtonDisabled = props.myOrderNum === props.sizeOfList;
    return (
        <div className="playbackOrderContainer">
            <button
                type="button"
                className="playbackOrderButton"
                onClick={() => props.bumpDown(props.myOrderNum)}
                disabled={leftButtonDisabled}
            >
                <Remove
                    style={{
                        fill: leftButtonDisabled ? disabledColor : "white"
                    }}
                    fontSize="small"
                    fontWeight={800}
                    shapeRendering="crispEdges"
                />
            </button>
            <Typography className="playbackOrderNumber">
                {props.myOrderNum}
            </Typography>
            <button
                type="button"
                className="playbackOrderButton"
                onClick={() => props.bumpUp(props.myOrderNum)}
                disabled={rightButtonDisabled}
            >
                <Add
                    style={{
                        fill: rightButtonDisabled ? disabledColor : "white"
                    }}
                    fontSize="small"
                    fontWeight={800}
                    shapeRendering="crispEdges"
                />
            </button>
        </div>
    );
};

export default PlaybackOrderControls;
