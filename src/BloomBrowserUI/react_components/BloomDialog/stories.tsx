/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { storiesOf } from "@storybook/react";
import {
    BloomDialog,
    DialogBottom,
    DialogMiddle,
    DialogTitle
} from "./BloomDialog";
import { Button, CircularProgress } from "@material-ui/core";

storiesOf("Bloom Dialog", module)
    .add("Simple Dialog", () => {
        return React.createElement(() => {
            const [open, setOpen] = React.useState(false);
            return (
                <div>
                    {open ? (
                        <BloomDialog omitOuterFrame={false} open={open}>
                            <DialogTitle title="A Simple Progress Dialog" />
                            <DialogMiddle
                                css={css`
                                    height: 100px;
                                    width: 500px;
                                    p {
                                        margin-top: 0;
                                    }
                                `}
                            >
                                <p>
                                    We should have a consistent amount of space
                                    between every element and the borders of the
                                    dialog box. This will overflow which should
                                    lead to a vertical scroll bar.
                                </p>
                                <p>
                                    Ea non consequat irure et elit enim laboris
                                    fugiat ipsum. Lorem ipsum velit ut duis ex
                                    magna aliquip quis. Magna incididunt ullamco
                                    qui in aliquip. Est anim nisi aute cupidatat
                                    elit voluptate ut aute quis esse excepteur.
                                    Deserunt irure eiusmod occaecat nisi est
                                    exercitation. Reprehenderit excepteur
                                    excepteur cupidatat nisi esse nisi. Nostrud
                                    excepteur irure incididunt nisi velit
                                    voluptate velit proident.
                                </p>
                            </DialogMiddle>
                            <DialogBottom>
                                <Button
                                    variant={"outlined"}
                                    color={"primary"}
                                    css={css`
                                        float: right;
                                    `}
                                    onClick={() => setOpen(false)}
                                >
                                    Close Me
                                </Button>
                            </DialogBottom>
                        </BloomDialog>
                    ) : (
                        <Button
                            onClick={() => setOpen(true)}
                            variant={"contained"}
                        >
                            {"Show Dialog"}
                        </Button>
                    )}
                </div>
            );
        });
    })
    .add("Dialog with icon and spinner", () => {
        return React.createElement(() => {
            return (
                <BloomDialog omitOuterFrame={false} open={true}>
                    <DialogTitle
                        icon="Check In.svg"
                        backgroundColor="#ffffad"
                        color="black"
                        title="A Nice Progress Dialog"
                    >
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
                    </DialogTitle>
                    <DialogMiddle
                        css={css`
                            height: 100px;
                            width: 500px;
                            p {
                                margin-top: 0;
                            }
                        `}
                    >
                        <p>
                            All three elements in the title bar should be
                            vertically aligned. We should have a consistent
                            amount of space between every element and the
                            borders of the dialog box. This will overflow which
                            should lead to a vertical scroll bar.
                        </p>
                        <p>
                            Ea non consequat irure et elit enim laboris fugiat
                            ipsum. Lorem ipsum velit ut duis ex magna aliquip
                            quis. Magna incididunt ullamco qui in aliquip. Est
                            anim nisi aute cupidatat elit voluptate ut aute quis
                            esse excepteur. Deserunt irure eiusmod occaecat nisi
                            est exercitation. Reprehenderit excepteur excepteur
                            cupidatat nisi esse nisi. Nostrud excepteur irure
                            incididunt nisi velit voluptate velit proident.
                        </p>
                    </DialogMiddle>
                    <DialogBottom>
                        <Button
                            color={"primary"}
                            css={css`
                                float: left;
                            `}
                        >
                            Secondary
                        </Button>
                        <Button
                            variant={"contained"}
                            color={"primary"}
                            css={css`
                                float: right;
                            `}
                        >
                            Foo
                        </Button>
                    </DialogBottom>
                </BloomDialog>
            );
        });
    });
