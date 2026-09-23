import * as React from "react";
import { SimpleProgressDialog } from "./SimpleProgressDialog";
import WebSocketManager, {
    IBloomWebSocketProgressEvent,
} from "../../utils/WebSocketManager";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import { normalDialogEnvironmentForStorybook } from "../BloomDialog/BloomDialogPlumbing";

const kHousekeepingMessage =
    "Please wait while Bloom does some housekeeping on your book...";

// The C# side drives the dialog entirely over the "progress" websocket; fake that here.
function send(event: Partial<IBloomWebSocketProgressEvent>) {
    WebSocketManager.mockSend<IBloomWebSocketProgressEvent>("progress", {
        clientContext: "progress",
        id: "message",
        ...event,
    } as IBloomWebSocketProgressEvent);
}

// Count the bar up to 100% over about five seconds, then run whatever comes next.
function countUp(andThen?: () => void) {
    let percent = 0;
    const timer = window.setInterval(() => {
        percent += 5;
        send({ id: "percent", percent });
        if (percent >= 100) {
            window.clearInterval(timer);
            if (andThen) andThen();
        }
    }, 250);
    return () => window.clearInterval(timer);
}

export default {
    title: "Progress/Simple Progress Dialog",
};

// What the user should see for the whole of a run that goes well: the bar, the sentence, nothing else.
export const UpdateBook = () => {
    const [isOpen, setIsOpen] = React.useState(true);
    React.useEffect(() => countUp(), []);
    return (
        <SimpleProgressDialog
            title="Update Book"
            titleColor="white"
            titleBackgroundColor={kBloomBlue}
            message={kHousekeepingMessage}
            open={isOpen}
            onClose={() => setIsOpen(false)}
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        />
    );
};

UpdateBook.story = { name: "Update Book (the usual case)" };

// The title bar should go red, the message should appear under the sentence, and Close and Report
// should turn up at the bottom.
export const WithAnError = () => {
    const [isOpen, setIsOpen] = React.useState(true);
    React.useEffect(
        () =>
            countUp(() => {
                send({
                    progressKind: "Error",
                    message:
                        "Something went wrong: the page could not be updated.",
                });
                send({ id: "show-buttons" });
            }),
        [],
    );
    return (
        <SimpleProgressDialog
            title="Update Book"
            titleColor="white"
            titleBackgroundColor={kBloomBlue}
            message={kHousekeepingMessage}
            open={isOpen}
            onClose={() => setIsOpen(false)}
            dialogEnvironment={normalDialogEnvironmentForStorybook}
        />
    );
};

WithAnError.story = { name: "With an error" };
