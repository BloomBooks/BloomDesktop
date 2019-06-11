import * as React from "react";
import { Typography } from "@material-ui/core";
import "./BasePublishScreen.less";
import { LocalizedString } from "../../react_components/l10nComponents";

// This file contains a collection of components which together comprise the BasePublishScreen,
// which creates the basic layout of a publishing screen in Bloom.

/*
  Usage:
  <BasePublishScreen>
      <PreviewPanel>Preview this!</PreviewPanel>
      <PublishPanel>
        Some controls for actually publishing
      </PublishPanel>
      <SettingsPanel>
        <HelpGroup>
          some help links
        </HelpGroup>
      </SettingsPanel>
  </BasePublishScreen>
*/

export const BasePublishScreen: React.FunctionComponent<{
    className: string;
}> = props => (
    <div className={"screen " + props.className}>
        <main className={"main"}>
            {findOne(props.children, PreviewPanel)}
            {findOne(props.children, PublishPanel)}
        </main>
        <aside className={"sidePanel"}>
            {findOne(props.children, SettingsPanel)}
            {findOne(props.children, HelpGroup)}
        </aside>
    </div>
);

export const PreviewPanel: React.FunctionComponent = props => {
    return <section className={"preview"}>{props.children}</section>;
};
export const PublishPanel: React.FunctionComponent = props => {
    return <section className={"publish"}>{props.children}</section>;
};
export const SettingsPanel: React.FunctionComponent = props => {
    return <>{props.children}</>;
};
export const SettingsGroup: React.FunctionComponent<{
    label: string;
}> = props => {
    return (
        <section className={"settingsGroup"}>
            <Typography component="h1" variant="h6">
                {props.label}
            </Typography>
            {props.children}
        </section>
    );
};
export const HelpGroup: React.FunctionComponent = props => {
    return (
        <section className={"helpGroup"}>
            <Typography component="h1" variant="h6">
                <LocalizedString l10nKey="Common.Help">Help</LocalizedString>
            </Typography>
            {props.children}
        </section>
    );
};
function findOne(children: React.ReactNode, classRef: React.ReactNode) {
    return findAny(children, classRef)[0];
}
function findAny(children: React.ReactNode, classRef: React.ReactNode) {
    return React.Children.toArray(children).filter(
        element => React.isValidElement(element) && element.type === classRef
    );
}
