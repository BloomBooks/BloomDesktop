/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import Typography from "@material-ui/core/Typography";
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
}> = props => {
    // Might eventually become props
    const panelHeight = 180;
    const mainTitleHeight = 52;
    return (
        <div
            css={css`
                display: flex;
                flex-direction: row;
                flex: 1;
                padding-top: 12px;
            `}
            className={props.className}
        >
            {props.icon && (
                <div
                    css={css`
                        width: 60px;
                        padding-top: 6px;
                        padding-left: 8px;
                        padding-right: 8px;
                        display: flex;
                        justify-content: center;
                        align-items: flex-start;
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
                `}
            >
                <Typography
                    css={css`
                        max-height: ${mainTitleHeight}px;
                        // If the main title runs to more than two lines, we allow a scroll bar.
                        overflow-y: auto;
                        // We don't need much here, but without this the spacing between the title lines
                        // is more than the space between the main and sub titles.
                        margin-bottom: 6px !important;
                    `}
                    align="left"
                    variant="h6"
                >
                    {props.heading}
                </Typography>
                <Typography
                    css={css`
                        max-height: ${panelHeight - mainTitleHeight}px;
                        // If absolutely necessary, we allow a scroll bar in the subtitle.
                        overflow-y: auto;
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
                    flex: 1;
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
