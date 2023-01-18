/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import Typography from "@material-ui/core/Typography";
import "./PublishScreenBaseComponents.less";
import { LocalizedString } from "../../react_components/l10nComponents";

// This file contains a collection of components which works together with the PublishScreenTemplate
// to create the basic layout of a publishing screen in Bloom.

export const PreviewPanel: React.FunctionComponent = props => {
    return (
        <section
            css={css`
                height: 470px;
                width: 100%;
                background: radial-gradient(
                        641.32px at 29.05% 29.83%,
                        rgba(112, 112, 112, 0) 0%,
                        #0c0c0c 100%
                    ),
                    #2d2d2d;
                padding-left: 20px;
                padding-top: 10px;
                box-sizing: border-box;
                display: flex;
                flex-shrink: 0;
                flex-grow: 1;
                flex-direction: column;
            `}
        >
            {props.children}
        </section>
    );
};

// This component contains the padding needed when this panel is above a PreviewPanel.
export const UnderPreviewPanel: React.FunctionComponent = props => (
    <div
        css={css`
            padding-left: 20px;
            padding-top: 10px;
            padding-bottom: 10px;
        `}
    >
        <PublishPanel>{props.children}</PublishPanel>
    </div>
);

// This component provides no padding. If you need a standard padding, use the above UnderPreviewPanel.
export const PublishPanel: React.FunctionComponent = props => (
    <section
        css={css`
            display: flex;
            flex-direction: column;
        `}
    >
        {props.children}
    </section>
);

export const SettingsPanel: React.FunctionComponent = props => {
    return <React.Fragment>{props.children}</React.Fragment>;
};

export const SettingsGroup: React.FunctionComponent<{
    label: string;
}> = props => {
    return (
        <section
            css={css`
                margin-top: 20px;
            `}
        >
            <Typography variant="h6">{props.label}</Typography>
            {props.children}
        </section>
    );
};

const helpAndCommandGroupCss =
    "margin-bottom: 20px; display: flex; flex-direction: column;";

export const HelpGroup: React.FunctionComponent = props => {
    return (
        <section
            css={css`
                ${helpAndCommandGroupCss}
            `}
        >
            <Typography variant="h6">
                <LocalizedString l10nKey="Common.Help">Help</LocalizedString>
            </Typography>
            {props.children}
        </section>
    );
};

export const CommandsGroup: React.FunctionComponent = props => {
    return (
        <section
            css={css`
                ${helpAndCommandGroupCss}
            `}
        >
            {props.children}
        </section>
    );
};
