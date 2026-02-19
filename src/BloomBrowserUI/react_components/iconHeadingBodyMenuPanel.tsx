import { css } from "@emotion/react";

import Typography from "@mui/material/Typography";
import * as React from "react";
import { StringWithOptionalLink } from "./stringWithOptionalLink";

// I don't have a good name for this really. It is a common piece for the statusPanelCommon
// and the BookProblem classes. If we find other uses, particularly for BookProblem somewhere
// other than in the TeamCollectionBookStatusPanel, we'll probably need more generalized
// control of height through props. Many of the dimensions are adapted from constants
// declared in statusPanelCommon.css; converting to Emotion is a work-in-progress.
export const IconHeadingBodyMenuPanel: React.FunctionComponent<{
    icon: JSX.Element;
    heading: string;
    body: string;
    menu?: JSX.Element; // when used in StatusPanelCommon, will eventually show a menu
    className?: string; // additional class for  root div; enables emotion CSS.
}> = (props) => {
    // Might eventually become props
    const panelHeight = 180;
    const mainTitleHeight = 60; // just enough for two lines of heading without a scroll bar
    return (
        <div
            css={css`
                display: flex;
                flex-direction: row;
                flex: 0 0;
                padding-top: 12px;
                padding-bottom: 12px;
            `}
            className={props.className}
        >
            {props.icon && (
                <div
                    css={css`
                        width: 60px;
                        //padding-top: 6px;
                        padding-left: 8px;
                        padding-right: 8px;
                        // The following will center it vertically, relative to the following div containing
                        // the heading and subheading. I (JohnT) think this looks strange when the text is higher than
                        // the icon; better to keep it in the top left.
                        /* display: flex;
                        flex-direction: column;
                        justify-content: center;
                        align-items: flex-start; */
                        img {
                            width: 90%;
                        }
                    `}
                >
                    {props.icon}
                </div>
            )}
            <div
                css={css`
                    display: flex;
                    flex-direction: column;
                    margin-left: ${props.icon ? "0;" : "20px;"};
                    justify-content: center;
                `}
            >
                <Typography
                    css={css`
                        max-height: ${mainTitleHeight}px;
                        // If the main title runs to more than two lines, we allow a scroll bar.
                        overflow-y: auto;
                        // tighten up line spacing; helps fit two lines, and when there are two, makes sure they
                        // are closer to each other than the main heading is to the subheading.
                        line-height: 1.3 !important;
                    `}
                    align="left"
                    variant="h6"
                >
                    <div
                        css={css`
                            // I don't understand this at all, but somehow, with line-height set to 1.3 to make a 2-line heading
                            // tight, if the heading is not wrapped with another div and this much bottom margin,
                            // the browser makes the parent header element shorter than the two lines and shows a
                            // (tiny, confusing) scroll bar.
                            // We could also prevent the scroll bar by setting a height rather than just a max-height for the parent,
                            // but then it could not shrink to a single line in the common case where that's all we need.
                            margin-bottom: 8px;
                        `}
                    >
                        {props.heading}
                    </div>
                </Typography>
                <Typography
                    css={css`
                        max-height: ${panelHeight - mainTitleHeight}px;
                        // If absolutely necessary, we allow a scroll bar in the subtitle.
                        overflow-y: auto;
                        // The MUI default here makes the lines more widely spaced than the gap between the
                        // main heading and the subheading. Previously we added extra margin-bottom on the
                        // header to compensate, but it looks better tighter.
                        line-height: 1.3 !important;
                    `}
                    align="left"
                    variant="subtitle2"
                >
                    <StringWithOptionalLink message={props.body} />
                </Typography>
            </div>
            <div
                css={css`
                    display: flex;
                    flex: 0 0;
                    flex-direction: column;
                    align-items: flex-end;
                    padding-right: 15px; // match right alignment of button below
                `}
            >
                {props.menu}
            </div>
        </div>
    );
};
