import { css } from "@emotion/react";

import * as React from "react";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle,
} from "./BloomDialog";
import { Button, CircularProgress } from "@mui/material";

import PersonIcon from "@mui/icons-material/Person";
import {
    DialogCancelButton,
    DialogCloseButton,
} from "./commonDialogComponents";
import {
    WarningBox,
    ErrorBox,
    NoteBoxSansBorder,
    NoteBox,
    BoxWithIconAndText,
    WaitBox,
} from "../boxes";
import {
    normalDialogEnvironmentForStorybook,
    useSetupBloomDialog,
} from "./BloomDialogPlumbing";

const circularProgress = (
    <CircularProgress
        css={css`
            margin-left: auto;
            margin-top: auto;
            margin-bottom: auto;
            color: black !important;
        `}
        size={20}
        className={"circle-progress"}
        color={undefined}
    />
);

export default {
    title: "BloomDialog",
};

export const SimpleDialog = () => {
    return React.createElement(() => {
        const { showDialog, closeDialog, propsForBloomDialog } =
            useSetupBloomDialog(normalDialogEnvironmentForStorybook);
        // normally here we would assign showDialog to an exported function that
        // other parts of the UI can use to show this dialog. But that doesn't
        // really work here in story-land, so we'll just use it below in a button.
        return (
            <div>
                <BloomDialog {...propsForBloomDialog}>
                    <DialogTitle title="A Simple <BloomDialog>" />
                    <DialogMiddle
                        css={css`
                            height: 200px;
                            width: 500px;
                            p {
                                margin-top: 0;
                            }
                        `}
                    >
                        <p>
                            We should have a consistent amount of space between
                            every element and the borders of the dialog box.
                        </p>
                    </DialogMiddle>
                    <DialogBottomButtons>
                        <DialogCloseButton onClick={closeDialog} />
                    </DialogBottomButtons>
                </BloomDialog>
                <Button
                    onClick={() => showDialog()}
                    variant={"contained"}
                    color="secondary"
                >
                    {"Show Dialog"}
                </Button>
            </div>
        );
    });
};

export const DialogWithCloseIcon = () => {
    return React.createElement(() => {
        const { showDialog, closeDialog, propsForBloomDialog } =
            useSetupBloomDialog(normalDialogEnvironmentForStorybook);
        return (
            <div>
                <BloomDialog onCancel={closeDialog} {...propsForBloomDialog}>
                    <DialogTitle title="Dialog with Close icon" />
                    <DialogMiddle
                        css={css`
                            height: 200px;
                            width: 500px;
                            p {
                                margin-top: 0;
                            }
                        `}
                    >
                        <p>
                            We should have a consistent amount of space between
                            every element and the borders of the dialog box.
                        </p>
                    </DialogMiddle>
                </BloomDialog>
                <Button
                    onClick={() => showDialog()}
                    variant={"contained"}
                    color="secondary"
                >
                    {"Show Dialog"}
                </Button>
            </div>
        );
    });
};

DialogWithCloseIcon.story = {
    name: "Dialog with Close icon",
};

export const DialogWithProgressAndClose = () => {
    return React.createElement(() => {
        const { showDialog, closeDialog, propsForBloomDialog } =
            useSetupBloomDialog(normalDialogEnvironmentForStorybook);
        return (
            <div>
                <BloomDialog
                    onCancel={() => {
                        closeDialog();
                    }}
                    {...propsForBloomDialog}
                >
                    <DialogTitle title="Dialog with Progress and Close icon">
                        {circularProgress}
                    </DialogTitle>
                    <DialogMiddle
                        css={css`
                            height: 200px;
                            width: 500px;
                            p {
                                margin-top: 0;
                            }
                        `}
                    >
                        <p>
                            With both the circular progress and the Close icon,
                            the progress icon gets pushed to the middle.
                        </p>
                    </DialogMiddle>
                </BloomDialog>
                <Button
                    onClick={() => showDialog()}
                    variant={"contained"}
                    color="secondary"
                >
                    {"Show Dialog"}
                </Button>
            </div>
        );
    });
};

DialogWithProgressAndClose.story = {
    name: "Dialog with Progress and Close",
};

export const DialogWithTheKitchenSink = () => {
    return React.createElement(() => {
        const { showDialog, closeDialog, propsForBloomDialog } =
            useSetupBloomDialog(normalDialogEnvironmentForStorybook);
        return (
            <BloomDialog onCancel={closeDialog} {...propsForBloomDialog}>
                <DialogTitle
                    icon="Check In.svg"
                    backgroundColor="#ffffad"
                    color="black"
                    title="A Nice Progress Dialog"
                >
                    {circularProgress}
                </DialogTitle>
                <DialogMiddle
                    css={css`
                        height: 300px;
                        width: 500px;
                        p {
                            margin-top: 0;
                        }
                    `}
                >
                    <p>
                        All three elements in the title bar should be vertically
                        aligned. We should have a consistent amount of space
                        between every element and the borders of the dialog box.
                        This will overflow which should lead to a vertical
                        scroll bar.
                    </p>
                    <WarningBox>
                        WarningBox | Don't step on Superman's cape.
                    </WarningBox>
                    <p>
                        I notice at the moment that the spacing between these
                        paragraphs and special boxes is messed up.
                    </p>
                    <NoteBoxSansBorder>
                        NoteBoxSansBorder | The Broncos will have a winning
                        season some year. (wishful thinking...)
                    </NoteBoxSansBorder>
                    <NoteBox>NoteBox</NoteBox>
                    <WaitBox>WaitBox | Tick tock tick tock.</WaitBox>
                    <BoxWithIconAndText
                        hasBorder={true}
                        color="red"
                        backgroundColor="lavenderblush"
                        borderColor="blue"
                        icon={<PersonIcon />}
                    >
                        BoxWithIconAndText | This one is fully custom.
                    </BoxWithIconAndText>
                    <p>
                        Est anim nisi aute cupidatat elit voluptate ut aute quis
                        esse excepteur. Deserunt irure eiusmod occaecat nisi est
                        exercitation.
                    </p>
                    <ErrorBox>ErrorBox | Abandon all hope.</ErrorBox>
                    <p>
                        Ea non consequat irure et elit enim laboris fugiat
                        ipsum. Lorem ipsum velit ut duis ex magna aliquip quis.
                        Magna incididunt ullamco qui in aliquip. Est anim nisi
                        aute cupidatat elit voluptate ut aute quis esse
                        excepteur. Deserunt irure eiusmod occaecat nisi est
                        exercitation. Reprehenderit excepteur excepteur
                        cupidatat nisi esse nisi. Nostrud excepteur irure
                        incididunt nisi velit voluptate velit proident.
                    </p>{" "}
                    <p>
                        Ea non consequat irure et elit enim laboris fugiat
                        ipsum. Lorem ipsum velit ut duis ex magna aliquip quis.
                        Magna incididunt ullamco qui in aliquip. Est anim nisi
                        aute cupidatat elit voluptate ut aute quis esse
                        excepteur. Deserunt irure eiusmod occaecat nisi est
                        exercitation. Reprehenderit excepteur excepteur
                        cupidatat nisi esse nisi. Nostrud excepteur irure
                        incididunt nisi velit voluptate velit proident.
                    </p>
                </DialogMiddle>
                <DialogBottomButtons>
                    <DialogBottomLeftButtons>
                        <Button color="primary">Something on the left</Button>
                    </DialogBottomLeftButtons>
                    <Button variant="contained" color="primary">
                        Just Do It
                    </Button>
                    <DialogCancelButton />
                </DialogBottomButtons>
            </BloomDialog>
        );
    });
};

DialogWithTheKitchenSink.story = {
    name: "Dialog with the kitchen sink",
};

export const TestDragResize = () => (
    <BloomDialog onClose={() => undefined} open={true}>
        <DialogTitle title="Drag Me" />
        <DialogMiddle>
            <p>Blah</p>
            <p>Blah</p>
            <p>
                Lorem ipsum dolor sit amet, consectetur adipiscing elit.
                Curabitur in felis feugiat est pellentesque bibendum. Maecenas
                non sem a augue vulputate ultricies. In hac habitasse platea
                dictumst. Quisque augue quam, facilisis in laoreet ac,
                consectetur luctus lectus. Cras eu condimentum sem.
            </p>
            <p>Blah</p>
        </DialogMiddle>
        <DialogBottomButtons>
            <DialogCancelButton onClick_DEPRECATED={() => undefined} />
        </DialogBottomButtons>
    </BloomDialog>
);

TestDragResize.story = {
    name: "Test drag & resize",
};
