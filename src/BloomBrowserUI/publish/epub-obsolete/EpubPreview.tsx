import * as React from "react";
import { IUILanguageAwareProps } from "../../react_components/l10nComponents";
import WebSocketManager from "../../utils/WebSocketManager";
import "errorHandler";

interface IPreviewProps extends IUILanguageAwareProps {
    websocketClientContext: string;
}

interface IComponentState {
    previewSrc: string;
}

// This component shows a simulated device with a live epub inside of it.
// The preview lives in an iframe and is activated by setting the src of the iframe
// by broadcasting a message on the web socket. The message should have id 'preview'
// and the "message" should be the URL for the preview iframe. An empty string may be broadcast
// to clear the preview.
export default class EpubPreview extends React.Component<
    IPreviewProps,
    IComponentState
> {
    public readonly state: IComponentState = {
        previewSrc: ""
    };

    constructor(props) {
        super(props);
        WebSocketManager.addListener(props.websocketClientContext, e => {
            if (e.id === "epubPreview" && e.message) {
                this.setState({ previewSrc: e.message });
            }
        });
    }
    public render() {
        return (
            <div id="device">
                <iframe src={this.state.previewSrc} />
            </div>
        );
    }
}
