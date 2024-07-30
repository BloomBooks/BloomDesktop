/** @jsx jsx **/
import { jsx, css } from "@emotion/react";
import * as React from "react";
import Typography from "@mui/material/Typography";
import "./PublishScreenBaseComponents.less";
import { LocalizedString } from "../../react_components/l10nComponents";
import { kOptionPanelBackgroundColor } from "../../bloomMaterialUITheme";
import { DisabledContext } from "../../react_components/BloomToolTip";
import { kBloomDisabledOpacity } from "../../utils/colorUtils";

// This file contains a collection of components which works together with the PublishScreenTemplate
// to create the basic layout of a publishing screen in Bloom.

export const PreviewPanel: React.FunctionComponent<{
    className?: string;
}> = props => {
    return (
        <section
            css={css`
                background-color: ${kOptionPanelBackgroundColor};
                padding-left: 20px;
                padding-top: 10px;
                box-sizing: border-box;
                display: flex;
                flex-shrink: 0;
                flex-grow: 1;
                flex-direction: column;
            `}
            className={props.className} // mainly to allow CSS
        >
            {props.children}
        </section>
    );
};

export const PublishPanel: React.FunctionComponent = props => (
    <section
        css={css`
            display: flex;
            flex-direction: column;
            padding-left: 20px;
            padding-top: 10px;
            padding-bottom: 10px;
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
    const disabledContext = React.useContext(DisabledContext);
    return (
        <section
            css={css`
                margin-top: 20px;
                opacity: ${disabledContext.valueOf()
                    ? kBloomDisabledOpacity
                    : 1};
            `}
        >
            <Typography variant="h6">{props.label}</Typography>
            {props.children}
        </section>
    );
};

const helpAndCommandGroupCss =
    "margin-top: 20px; display: flex; flex-direction: column;";

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
