import * as React from "react";
import PlaybackOrderControls from "../playbackOrderControls";

import { Meta, StoryObj } from "@storybook/react-vite";

const meta: Meta = {
    title: "PlaybackOrderControls",
};

export default meta;
type Story = StoryObj;

const playbackControlsDivStyles: React.CSSProperties = {
    width: "150px",
    height: "80px",
    border: "1px solid red",
    display: "flex",
    justifyContent: "center",
};

const bumpUp = (whichPositionToBump: number): void => {
    console.log(
        `Bump up myOrderNum from ${whichPositionToBump} to ${++whichPositionToBump}`,
    );
};

const bumpDown = (whichPositionToBump: number): void => {
    console.log(
        `Bump down myOrderNum from ${whichPositionToBump} to ${--whichPositionToBump}`,
    );
};

export const PlaybackOrderButtonsStory: Story = {
    name: "PlaybackOrder buttons",
    render: () => (
        <>
            <div style={playbackControlsDivStyles}>
                <PlaybackOrderControls
                    maxOrder={3}
                    orderOneBased={2}
                    onIncrease={bumpUp}
                    onDecrease={bumpDown}
                />
            </div>
            <div style={playbackControlsDivStyles}>
                <PlaybackOrderControls
                    maxOrder={3}
                    orderOneBased={1}
                    onIncrease={bumpUp}
                    onDecrease={bumpDown}
                />
            </div>
            <div style={playbackControlsDivStyles}>
                <PlaybackOrderControls
                    maxOrder={3}
                    orderOneBased={3}
                    onIncrease={bumpUp}
                    onDecrease={bumpDown}
                />
            </div>
        </>
    ),
};
