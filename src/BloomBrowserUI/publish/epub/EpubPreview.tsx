import * as React from "react";
import * as ReactDOM from "react-dom";
import { H1, Div, IUILanguageAwareProps } from "../../react_components/l10n";

interface IComponentState {}
// This component shows a device with a live epub inside of it.
export default class EpubPreview extends React.Component<
    IUILanguageAwareProps,
    IComponentState
> {
    public render() {
        return <p>Todo: show a phone with the preview</p>;
    }
}
