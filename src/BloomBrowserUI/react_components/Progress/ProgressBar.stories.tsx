import { css } from "@emotion/react";
import { ProgressBar } from "./ProgressBar";

const barFrame = (progressBar: JSX.Element) => (
    <div
        css={css`
            width: 280px;
            height: 75px;
            padding-top: 5px;
            padding-right: 10px;
            padding-left: 20px;
            background-color: rgb(45, 45, 45);
        `}
    >
        {progressBar}
    </div>
);

export default {
    title: "Progress/Progress Bar",
};

export const _20 = () => barFrame(<ProgressBar percentage={20} />);

_20.story = {
    name: "20%",
};

export const _100 = () => barFrame(<ProgressBar percentage={100} />);

_100.story = {
    name: "100%",
};

export const _0 = () => barFrame(<ProgressBar percentage={0} />);

_0.story = {
    name: "0%",
};
