import * as React from "react";
import Typography from "@material-ui/core/Typography";
import "./PublishScreenBaseComponents.less";
import { LocalizedString } from "../../react_components/l10nComponents";

// This file contains a collection of components which works together with the PublishScreenTemplate
// to create the basic layout of a publishing screen in Bloom.

export const PreviewPanel: React.FunctionComponent = props => {
    return <section className={"preview"}>{props.children}</section>;
};

export const PublishPanel: React.FunctionComponent = props => {
    return <section className={"publish"}>{props.children}</section>;
};

export const SettingsPanel: React.FunctionComponent = props => {
    return <React.Fragment>{props.children}</React.Fragment>;
};

export const SettingsGroup: React.FunctionComponent<{
    label: string;
}> = props => {
    return (
        <section className={"settingsGroup"}>
            <Typography variant="h6">{props.label}</Typography>
            {props.children}
        </section>
    );
};

export const HelpGroup: React.FunctionComponent = props => {
    return (
        <section className={"helpGroup"}>
            <Typography variant="h6">
                <LocalizedString l10nKey="Common.Help">Help</LocalizedString>
            </Typography>
            {props.children}
        </section>
    );
};

export const CommandsGroup: React.FunctionComponent = props => {
    return <section className={"commandGroup"}>{props.children}</section>;
};
