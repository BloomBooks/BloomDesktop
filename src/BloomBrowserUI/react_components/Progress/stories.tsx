import * as React from "react";
import { storiesOf } from "@storybook/react";
import { ProgressDialog } from "./ProgressDialog";
import WebSocketManager, {
    IBloomWebSocketProgressEvent
} from "../../utils/WebSocketManager";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import { ProgressBox } from "./progressBox";
import { normalDialogEnvironmentForStorybook } from "../BloomDialog/BloomDialogPlumbing";

interface IStoryMessage {
    id?: string;
    k?: "Error" | "Warning" | "Progress" | "Note" | "Instruction";
    m?: string;
    delay?: number;
    progress?: "indefinite" | "off";
}

const noFrameProps = {
    dialogEnvironment: {
        dialogFrameProvidedExternally: true,
        initiallyOpen: true
    }
};

storiesOf("Progress Box", module).add(
    "Raw ProgressBox with preloaded log",
    () => {
        return React.createElement(() => {
            return (
                <ProgressBox
                    preloadedProgressEvents={[
                        {
                            id: "message",
                            clientContext: "unused",
                            message: "This is a preloaded log message",
                            progressKind: "Progress"
                        },
                        {
                            id: "message",
                            clientContext: "unused",
                            message:
                                "This is a message about an error in the past",
                            progressKind: "Error"
                        }
                    ]}
                />
            );
        });
    }
);

function sendEvents(events: Array<IStoryMessage>) {
    sendNextEvent([...events]);
}
// I don't know why we run into trouble occasion
function sendNextEvent(events: Array<IStoryMessage>) {
    // I don't know why we run into trouble occasionally without this
    if (!events || events.length === 0) {
        return;
    }
    const e = events.shift();
    WebSocketManager.mockSend<IBloomWebSocketProgressEvent>("progress", {
        clientContext: "progress",
        id: e!.id || "message",
        progressKind: e!.k,
        message: e!.m
    });

    if (events.length)
        window.setTimeout(() => {
            sendNextEvent(events);
        }, /*e!.delay ??*/ 100);
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
        const [isOpen, setIsOpen] = React.useState(true);
        return React.createElement(() => {
            return (
                <ProgressDialog
                    title="Short, with report button eventually"
                    titleColor="white"
                    titleBackgroundColor={kBloomBlue}
                    titleIcon="Team Collection.svg"
                    showReportButton={"if-error"}
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
                    open={isOpen}
                    onClose={() => {
                        setIsOpen(false);
                    }}
                    dialogEnvironment={normalDialogEnvironmentForStorybook}
                />
            );
        });
    })
    .add("Long", () => {
        const [isOpen, setIsOpen] = React.useState(true);
        return React.createElement(() => {
            return (
                <ProgressDialog
                    key="long"
                    title="A Long Progress Dialog"
                    titleColor="black"
                    titleBackgroundColor="transparent"
                    showReportButton={"never"}
                    onReadyToReceive={() => sendEvents(kLongListOfAllTypes)}
                    open={isOpen}
                    onClose={() => {
                        setIsOpen(false);
                    }}
                    dialogEnvironment={normalDialogEnvironmentForStorybook}
                />
            );
        });
    })
    .add("Not wrapped in a dialog", () => {
        const [isOpen, setIsOpen] = React.useState(true);
        return React.createElement(() => {
            return (
                <ProgressDialog
                    title="Not wrapped in a material dialog (i.e. as when wrapped by winform dialog)"
                    titleColor="white"
                    titleBackgroundColor="green"
                    showReportButton={"never"}
                    open={isOpen}
                    onClose={() => {
                        setIsOpen(false);
                    }}
                    onReadyToReceive={() =>
                        sendEvents([
                            {
                                k: "Progress",
                                m:
                                    "This one is not wrapped in a material dialog, in order to test expanding out to whatever width is available, like we need when wrapping in a winforms dialog. 1 of 3 messages.",
                                progress: "indefinite"
                            },
                            {
                                k: "Progress",
                                m: "2 of 3",
                                progress: "indefinite"
                            },
                            {
                                k: "Progress",
                                m: "3 of 3",
                                progress: "indefinite"
                            },
                            {
                                id: "show-buttons"
                            },
                            {
                                id: "finished"
                            }
                        ])
                    }
                    {...noFrameProps}
                />
            );
        });
    })
    .add("Not wrapped in a dialog, long vertically", () => {
        const [isOpen, setIsOpen] = React.useState(true);
        return React.createElement(() => {
            return (
                <ProgressDialog
                    title="Not wrapped in a material dialog (i.e. as when wrapped by winform dialog)"
                    titleColor="white"
                    titleBackgroundColor="green"
                    showReportButton={"never"}
                    open={isOpen}
                    onClose={() => {
                        setIsOpen(false);
                    }}
                    onReadyToReceive={() => sendEvents(kLongListOfAllTypes)}
                    {...noFrameProps}
                />
            );
        });
    });
