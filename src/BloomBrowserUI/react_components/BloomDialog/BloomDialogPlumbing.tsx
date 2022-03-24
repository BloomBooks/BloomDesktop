import * as React from "react";
import { BloomApi } from "../../utils/bloomApi";
import { useEffect, useState } from "react";
import { useWebSocketListener } from "../../utils/WebSocketManager";

export interface IBloomDialogEnvironmentParams {
    // true if the caller is wrapping in a winforms dialog already
    dialogFrameProvidedExternally: boolean;
    // storybook stories will usually set this to true so we don't have to do anything to see the dialog
    initiallyOpen: boolean;
}

export const normalDialogEnvironmentForStorybook = {
    dialogFrameProvidedExternally: false,
    initiallyOpen: true
};

// Storybook stories use this to open dialogs that take their settings via parameters to the open function, rather than props.
// Example
//      <StorybookDialogWrapper id="SomeDialog" params={{name:"John", email:"example@gmail.com"}}>
//          <SomeDialog/>
//      </StorybookDialogWrapper>
export const StorybookDialogWrapper: React.FunctionComponent<{
    id: string;
    params: object;
}> = props => {
    useEffect(() => {
        // I'm not certain this delay is needed, but I do want to make sure the dialog
        // function runs first so it can wire up its even receiver.
        window.setTimeout(
            () =>
                document.dispatchEvent(
                    new CustomEvent("LaunchDialog", {
                        detail: { id: props.id, ...props.params }
                    })
                ),
            100
        );
    }, [props.id, props.params]);
    return <React.Fragment>{props.children}</React.Fragment>;
};

// Dialogs use this hook to wire up to launch events, both from the c# server (via websockets) and storybook (via window events)
export function useEventLaunchedBloomDialog(idForLaunchingFromServer: string) {
    const dialogEnvironment = useSetupBloomDialog({
        initiallyOpen: false,
        dialogFrameProvidedExternally: false
    });
    // for c# server
    useWebSocketListener("LaunchDialog", event => {
        if (event.id === idForLaunchingFromServer) {
            dialogEnvironment.showDialog(event);
        }
    });
    // for storybook
    document.addEventListener("LaunchDialog", (event: any) => {
        if (event.detail.id === idForLaunchingFromServer) {
            dialogEnvironment.showDialog(event.detail);
        }
    });
    return dialogEnvironment;
}

// Dialogs using <BloomDialog> that are still shown via a winforms dialog call this hook and use what it returns to manage the dialog.
// See the uses of it in the code for examples.
export function useSetupBloomDialog(
    dialogEnvironment?: IBloomDialogEnvironmentParams
) {
    const [currentlyOpen, setOpen] = useState(
        // we default to closed
        dialogEnvironment ? dialogEnvironment.initiallyOpen : false
    );

    // the websocket event can have properties like props, comes from the opening of the dialog from c# (see useSetupBloomDialogFromServer())
    const [event, setEvent] = useState<any>({});
    function showDialog(event?: any) {
        setEvent(event);
        setOpen(true);
    }
    function closeDialog() {
        if (dialogEnvironment?.dialogFrameProvidedExternally)
            BloomApi.post("common/closeReactDialog");
        else setOpen(false);
    }
    return {
        openingEvent: event,
        showDialog,
        closeDialog,
        propsForBloomDialog: {
            open: currentlyOpen,
            onClose: closeDialog,
            dialogFrameProvidedExternally:
                dialogEnvironment?.dialogFrameProvidedExternally || false
        }
    };
}
