import { DialogTitle, Typography } from "@material-ui/core";
import * as React from "react";
import { useRef, useState } from "react";
import ReactDOM = require("react-dom");
import { BloomApi } from "../utils/bloomApi";
import WebSocketManager from "../utils/WebSocketManager";
import BloomButton from "./bloomButton";
import { Link } from "./link";
import ProgressBox from "./progressBox";
import "./IndependentProgressDialog.less";
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";

// Root element rendered to progress dialog, using ReactDialog in C#

export const IndependentProgressDialog: React.FunctionComponent = props => {
    const urlParams = new URLSearchParams(window.location.search);
    const dialogTitle = urlParams.get("title");
    const [showButtons, setShowButtons] = useState(false);
    const progress = useRef("");
    const kWebSocketContext = "IndependentProgressDialog";

    React.useEffect(() => {
        const listener = e => {
            if (e.id === "show-buttons") {
                setShowButtons(true);
            }
        };
        WebSocketManager.addListener(kWebSocketContext, listener);
    }, []);
    const sendToClipboard = () => {
        BloomApi.postJson("common/clipboardText", { text: progress.current });
    };
    return (
        <ThemeProvider theme={theme}>
            <div id="progress-root">
                {/* Review: do we want a large title in the dialog, or a standard
                one in the title bar? */}
                <DialogTitle className="dialog-title">
                    <Typography variant="h6">{dialogTitle}</Typography>
                </DialogTitle>
                <div
                    id="copy-progress-row"
                    className={showButtons ? "with-buttons" : ""}
                >
                    <button
                        id="copy-button"
                        onClick={sendToClipboard}
                        title="Copy to Clipboard"
                    />
                    <ProgressBox
                        clientContext={kWebSocketContext}
                        notifyProgressChange={p => (progress.current = p)}
                    />
                </div>
                {showButtons && (
                    <div id="progress-buttons">
                        <Link
                            id="progress-report"
                            color="secondary"
                            l10nKey="Common.Report"
                            onClick={() => {
                                BloomApi.postJson("problemReport/showDialog", {
                                    message: progress.current,
                                    // Enhance: this will need to be configurable if we use this
                                    // dialog for something else...maybe by url param?
                                    shortMessage:
                                        "The user reported a problem in Team Collection Sync"
                                });
                            }}
                        >
                            REPORT
                        </Link>
                        <BloomButton
                            id="close-button"
                            l10nKey="ReportProblemDialog.Close" // Should we have Common.Close?
                            hasText={true}
                            enabled={true}
                            onClick={() => {
                                BloomApi.post("common/closeReactDialog");
                            }}
                        >
                            Close
                        </BloomButton>
                    </div>
                )}
            </div>
        </ThemeProvider>
    );
};

// Trick to make a function that can be called directly by the HTML file that is
// the root for the BrowserDialog window containing this dialog. This allows us
// to get the ReactDOM.render call applied even though we can't do React inside
// a local script element.
(window as any).connectProgressDialogRoot = element => {
    ReactDOM.render(<IndependentProgressDialog />, element);
};
