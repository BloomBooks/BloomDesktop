import * as React from "react";
import IncreaseIcon from "@mui/icons-material/Add";
import DecreaseIcon from "@mui/icons-material/Remove";
import Typography from "@mui/material/Typography";
import "./playbackOrderControls.less";

interface IPlaybackOrderControlsProps {
    maxOrder: number;
    orderOneBased: number;
    onIncrease: (whichPositionToChange: number) => void;
    onDecrease: (whichPositionToChange: number) => void;
}

const PlaybackOrderButton: React.FC<{
    icon: JSX.Element;
    disabled: boolean;
    onClick: () => void;
}> = (props) => {
    const icon = React.cloneElement(props.icon, {
        fontSize: "small",
        fontWeight: 800,
        shapeRendering: "crispEdges",
        fill: "white",
    });
    return (
        <button
            type="button"
            className={`playbackOrderButton ${
                props.disabled ? "disabled" : ""
            }`}
            {...props} //click and disabled
        >
            {icon}
        </button>
    );
};

const PlaybackOrderControls: React.FC<IPlaybackOrderControlsProps> = (
    props,
) => {
    return (
        <div className="playbackOrderContainer">
            <PlaybackOrderButton
                disabled={props.orderOneBased === 1}
                onClick={() => props.onDecrease(props.orderOneBased)}
                icon={<DecreaseIcon />}
            />
            <Typography className="playbackOrderNumber">
                {props.orderOneBased}
            </Typography>
            <PlaybackOrderButton
                disabled={props.orderOneBased === props.maxOrder}
                onClick={() => props.onIncrease(props.orderOneBased)}
                icon={<IncreaseIcon />}
            />
        </div>
    );
};

export default PlaybackOrderControls;
