import * as React from "react";
import { Range } from "rc-slider";

interface SliderProps {
    maxDuration: number;
    trimHandlePositions: number[];
    onChange: (start: number, end: number) => void;
    onAfterChange: (start: number) => void;
}

export class VideoTrimSlider extends React.Component<SliderProps, {}> {
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
                onChange={(v: number[]) => this.props.onChange(v[0], v[1])}
                onAfterChange={
                    // set video back to start point in case we were viewing the end point
                    (v: number[]) => this.props.onAfterChange(v[0])
                }
            />
        );
    }
}

export default VideoTrimSlider;
