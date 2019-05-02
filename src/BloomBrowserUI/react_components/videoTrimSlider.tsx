import * as React from "react";
import { Range } from "rc-slider";

interface SliderProps {
    maxDuration: number;
    trimHandlePositions: number[];
    onChangeFunc: (start: number, end: number) => void;
    onAfterChangeFunc: (start: number) => void;
}

export class VideoTrimSlider extends React.Component<SliderProps, {}> {
    constructor(props: SliderProps) {
        super(props);
    }
    public render() {
        return (
            <Range
                className="videoTrimSlider"
                count={1}
                step={0.1}
                min={0.0}
                allowCross={false}
                pushable={false}
                max={this.props.maxDuration}
                value={this.props.trimHandlePositions}
                onChange={(v: number[]) => this.props.onChangeFunc(v[0], v[1])}
                onAfterChange={
                    // set video back to start point in case we were viewing the end point
                    (v: number[]) => this.props.onAfterChangeFunc(v[0])
                }
            />
        );
    }
}

export default VideoTrimSlider;
