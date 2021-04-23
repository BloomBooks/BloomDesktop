import * as React from "react";
import { storiesOf } from "@storybook/react";
import { ProgressDialog } from "./ProgressDialog";
import WebSocketManager, {
    IBloomWebSocketProgressEvent
} from "../../utils/WebSocketManager";
import { kBloomBlue } from "../../bloomMaterialUITheme";

const kContext = "mock_progress";
interface IStoryMessage {
    id?: string;
    k?: "Error" | "Warning" | "Progress" | "Note" | "Instruction";
    m?: string;
    delay?: number;
    progress?: "indefinite" | "off";
}
function sendEvents(events: Array<IStoryMessage>) {
    sendNextEvent([...events]);
}
// I don't know why we run into trouble occasion
function sendNextEvent(events: Array<IStoryMessage>) {
    console.log("Sending events to progress.");
    // I don't know why we run into trouble occasionally without this
    if (!events || events.length === 0) {
        return;
    }
    const e = events.shift();
    WebSocketManager.mockSend<IBloomWebSocketProgressEvent>(kContext, {
        clientContext: kContext,
        id: e!.id || "message",
        progressKind: e!.k,
        message: e!.m
    });

    if (events.length)
        window.setTimeout(() => {
            sendNextEvent(events);
        }, e!.delay ?? 100);
}

const kLongListOfAllTypes: Array<IStoryMessage> = [
    { k: "Progress", m: "Starting up..." },
    { k: "Progress", m: "Working hard...", delay: 3000 },
    {
        k: "Warning",
        m:
            "This warning be in gold and should cause the title bar to also turn gold, while the spinner and title text should be black color.",
        delay: 3000
    },
    {
        k: "Progress",
        m:
            "Cillum reprehenderit sit esse amet ut ullamco ex deserunt mollit aliqua nulla aute ipsum..."
    },
    {
        k: "Progress",
        m:
            "Bunch of progress messages to force us to scroll. Reprehenderit commodo proident ad minim velit velit cupidatat excepteur do. Magna laborum elit culpa qui veniam aliqua laboris enim magna aute irure aliqua quis. Cillum aliqua et anim nulla ipsum consectetur aliquip eu est. Ex aliquip consequat officia et."
    },
    {
        k: "Progress",
        m:
            "Bunch of progress messages to force us to scroll. Reprehenderit commodo proident ad minim velit velit cupidatat excepteur do. Magna laborum elit culpa qui veniam aliqua laboris enim magna aute irure aliqua quis. Cillum aliqua et anim nulla ipsum consectetur aliquip eu est. Ex aliquip consequat officia et."
    },
    {
        k: "Progress",
        m:
            "Bunch of progress messages to force us to scroll. Reprehenderit commodo proident ad minim velit velit cupidatat excepteur do. Magna laborum elit culpa qui veniam aliqua laboris enim magna aute irure aliqua quis. Cillum aliqua et anim nulla ipsum consectetur aliquip eu est. Ex aliquip consequat officia et."
    },
    {
        k: "Progress",
        m:
            "Bunch of progress messages to force us to scroll. Reprehenderit commodo proident ad minim velit velit cupidatat excepteur do. Magna laborum elit culpa qui veniam aliqua laboris enim magna aute irure aliqua quis. Cillum aliqua et anim nulla ipsum consectetur aliquip eu est. Ex aliquip consequat officia et."
    },
    {
        k: "Progress",
        m:
            "Bunch of progress messages to force us to scroll. Reprehenderit commodo proident ad minim velit velit cupidatat excepteur do. Magna laborum elit culpa qui veniam aliqua laboris enim magna aute irure aliqua quis. Cillum aliqua et anim nulla ipsum consectetur aliquip eu est. Ex aliquip consequat officia et."
    },
    {
        k: "Progress",
        m:
            "Bunch of progress messages to force us to scroll. Reprehenderit commodo proident ad minim velit velit cupidatat excepteur do. Magna laborum elit culpa qui veniam aliqua laboris enim magna aute irure aliqua quis. Cillum aliqua et anim nulla ipsum consectetur aliquip eu est. Ex aliquip consequat officia et."
    },
    {
        k: "Progress",
        m:
            "Bunch of progress messages to force us to scroll. Reprehenderit commodo proident ad minim velit velit cupidatat excepteur do. Magna laborum elit culpa qui veniam aliqua laboris enim magna aute irure aliqua quis. Cillum aliqua et anim nulla ipsum consectetur aliquip eu est. Ex aliquip consequat officia et."
    },

    {
        k: "Note",
        m: "This is a note."
    },
    {
        k: "Warning",
        m: "Enim id aliquip ut sit duis amet magna aliquip mollit occaecat."
    },
    {
        k: "Progress",
        m: "Enim excepteur esse amet proident aute dolor fugiat commodo."
    },
    {
        k: "Progress",
        m: "This one should take 1 second to complete...",
        delay: 1000,
        progress: "indefinite"
    },
    { k: "Error", m: "Well that didn't work." },
    {
        k: "Instruction",
        m: "You should get some help."
    },
    {
        id: "progress",
        k: "Progress",
        progress: "off",
        m: "unused"
    },
    {
        id: "finished"
    },
    {
        id: "show-buttons",
        k: "Progress",
        m: "unused"
    }
];

storiesOf("Progress Dialog", module)
    .add("Short, with report button if there is an error", () => {
        return React.createElement(() => {
            return (
                <ProgressDialog
                    title="A Nice Progress Dialog"
                    titleColor="white"
                    titleBackgroundColor={kBloomBlue}
                    titleIcon="Team Collection.svg"
                    webSocketContext={kContext}
                    showReportButton={"if-error"}
                    wrapInDialog={true}
                    onReadyToReceive={() =>
                        sendEvents([
                            {
                                k: "Progress",
                                m: "Doing something.",
                                progress: "indefinite"
                            },
                            {
                                k: "Progress",
                                m:
                                    "This one should take 3 seconds to complete, and the progress spinner should be showing during this time.",
                                delay: 3000,
                                progress: "indefinite"
                            },
                            {
                                k: "Error",
                                m:
                                    "This error should cause the title bar to go red and the report button to become available when the dialog is finished."
                            },
                            {
                                id: "show-buttons"
                            },
                            {
                                id: "finished"
                            }
                        ])
                    }
                />
            );
        });
    })
    .add("Long", () => {
        return React.createElement(() => {
            return (
                <ProgressDialog
                    title="A Nice Progress Dialog"
                    titleColor="black"
                    titleBackgroundColor="transparent"
                    webSocketContext={kContext}
                    showReportButton={"never"}
                    onReadyToReceive={() => sendEvents(kLongListOfAllTypes)}
                    wrapInDialog={true}
                />
            );
        });
    });
