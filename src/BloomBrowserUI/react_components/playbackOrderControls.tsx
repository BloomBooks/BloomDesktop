import React = require("react");
import theme from "../bloomMaterialUITheme";
import Add from "@material-ui/icons/Add";
import Remove from "@material-ui/icons/Remove";
import Typography from "@material-ui/core/Typography";

interface IPlaybackOrderControlsProps {
    sizeOfList: number;
    myOrderNum: number; // NOT zero-based; first number should be "1"
    bumpUp: (whichPositionToBump: number) => void; // increase 'myOrderNum' (Add button)
    bumpDown: (whichPositionToBump: number) => void; // decrease 'myOrderNum' (Remove button)
}

const buttonWidth = 22;
const controlBoxWidth = buttonWidth * 3;
const warningColor: string = theme.palette.warning.main;
const bloomBlue: string = theme.palette.primary.main;
const white: string = "#FFFFFF";
const disabledColor: string = "#b0dee4"; // bloom-lightblue in bloomUI.less
const buttonStyle: React.CSSProperties = {
    backgroundColor: bloomBlue,
    boxShadow: "none",
    border: 0,
    padding: 0,
    width: buttonWidth,
    display: "flex",
    justifyContent: "center",
    alignItems: "center"
};
const containerStyle: React.CSSProperties = {
    width: controlBoxWidth,
    position: "relative",
    display: "flex",
    alignSelf: "center",
    boxShadow: "0px 2px 4px -1px",
    cursor: "not-allowed"
};

const PlaybackOrderControls: React.FC<IPlaybackOrderControlsProps> = props => {
    const leftButtonDisabled = props.myOrderNum === 1;
    const rightButtonDisabled = props.myOrderNum === props.sizeOfList;
    return (
        <div style={containerStyle}>
            <button
                type="button"
                onClick={() => props.bumpDown(props.myOrderNum)}
                disabled={leftButtonDisabled}
                style={buttonStyle}
            >
                <Remove
                    style={{ fill: leftButtonDisabled ? disabledColor : white }}
                    fontSize="small"
                    fontWeight={800}
                    shapeRendering="crispEdges"
                />
            </button>
            <Typography
                style={{
                    backgroundColor: warningColor,
                    width: `${buttonWidth}px`,
                    textAlign: "center",
                    fontWeight: 700
                }}
            >
                {props.myOrderNum}
            </Typography>
            <button
                type="button"
                onClick={() => props.bumpUp(props.myOrderNum)}
                disabled={rightButtonDisabled}
                style={buttonStyle}
            >
                <Add
                    style={{
                        fill: rightButtonDisabled ? disabledColor : white
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
