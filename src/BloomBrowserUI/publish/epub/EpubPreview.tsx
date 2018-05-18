import * as React from "react";
import * as ReactDOM from "react-dom";
import { H1, Div, IUILanguageAwareProps } from "../../react_components/l10n";
import WebSocketManager from "../../utils/WebSocketManager";

interface IPreviewProps extends IUILanguageAwareProps {
    lifetimeLabel: string;
}

interface IComponentState {
    previewSrc: string;
}
// This component shows a simulated device with a live epub inside of it.
// The preview lives in an iframe and is activated by setting the src of the iframe
// by broadcasting a message on the web socket. The message should have id 'preview'
// and payload the URL for the preview iframe. An empty string may be broadcast
// to clear the preview.
export default class EpubPreview extends React.Component<
    IPreviewProps,
    IComponentState
    > {
    constructor(props) {
        super(props);
        WebSocketManager.addListener(props.lifetimeLabel, event => {
            var e = JSON.parse(event.data);
            if (e.id === "epubPreview") {
                this.setState({ previewSrc: e.payload });
            }
        });
        this.state = { previewSrc: "" };
    }
    public render() {
        return <div id="device">
            <iframe src={this.state.previewSrc}></iframe>
        </div>;
    }
}
