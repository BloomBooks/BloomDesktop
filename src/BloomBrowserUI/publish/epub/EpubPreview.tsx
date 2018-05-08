import * as React from "react";
import * as ReactDOM from "react-dom";
import { H1, Div, IUILanguageAwareProps } from "../../react_components/l10n";

interface IComponentState {}
// This is a screen of controls that gives the user instructions and controls
// for creating epubs
export default class EpubPreview extends React.Component<
    IUILanguageAwareProps,
    IComponentState
> {
    public render() {
        return <p>Todo: show a phone with the preview</p>;
    }
}
