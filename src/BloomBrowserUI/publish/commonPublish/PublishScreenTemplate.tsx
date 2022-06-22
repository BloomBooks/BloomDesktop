/** @jsxFrag React.Fragment */
/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import PublishScreenBanner from "./PublishScreenBanner";

export const PublishScreenTemplate: React.FunctionComponent<{
    bannerRightSideControls?: React.ReactNode;
    bannerTitleEnglish: string;
    bannerTitleL10nId: string;
    bannerDescriptionMarkdown?: string;
    bannerDescriptionL10nId?: string;
    // This one "node" should include all the publishing mode options as well as the Help links.
    optionsPanelContents?: React.ReactNode;
}> = props => (
    <div
        id="publishScreenTemplate"
        css={css`
            display: flex;
            height: 100%;
            width: 100%;
            flex-direction: column;
            overflow: hidden;
        `}
    >
        <PublishScreenBanner
            css={css`
                flex: 1; // The banner stays relatively small compared to the rest of the screen
            `}
            titleEnglish={props.bannerTitleEnglish}
            titleL10nId={props.bannerTitleL10nId}
            descriptionMarkdown={props.bannerDescriptionMarkdown}
            descriptionL10nId={props.bannerDescriptionL10nId}
        >
            {props.bannerRightSideControls}
        </PublishScreenBanner>
        <div
            css={css`
                flex: 5; // The part under the banner takes up most of the space.
                display: flex;
                flex-direction: row;
                min-height: 0; // Enables scrolling to work correctly in the sub-containers.
            `}
        >
            <MainPanel>{props.children}</MainPanel>
            <OptionPanel>{props.optionsPanelContents}</OptionPanel>
        </div>
    </div>
);

// Takes "children" and displays them in the largest part of the screen (lower left).
export const MainPanel: React.FunctionComponent = props => (
    <div
        css={css`
            flex: 5; // The MainPanel fills out the rest of the width not taken by the OptionPanel.
            display: flex;
            background-color: #fafafa;
            padding: 1.5rem 0 0 1.5rem; // top and left
            overflow-y: auto;
        `}
    >
        {props.children}
    </div>
);

// Takes "children" and displays them in the Options sidebar (right side).
export const OptionPanel: React.FunctionComponent = props => (
    <div
        css={css`
            background-color: #f1f3f4;
            padding-left: 20px;
            width: 250px;
            flex: 1 0;
            display: flex;
            flex-direction: column;
            overflow-y: auto;
        `}
    >
        {props.children}
    </div>
);

export default PublishScreenTemplate;
