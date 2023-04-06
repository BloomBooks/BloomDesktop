import * as React from "react";
import { get, post, postData } from "../utils/bloomApi";
import { RadioGroup } from "@mui/material";
import { L10nLabel } from "./L10nLabel";
import { useEffect, useState } from "react";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle
} from "./BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "./BloomDialog/BloomDialogPlumbing";
import { useL10n } from "./l10nHooks";
import { MuiRadio } from "./muiRadio";
import { DialogOkButton } from "./BloomDialog/commonDialogComponents";

export const AutoUpdateSoftwareDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    const { propsForBloomDialog } = useSetupBloomDialog(
        props.dialogEnvironment
    );

    const [chosenRadio, setChosenRadio] = useState<"automatic" | "inform">(
        "automatic"
    );
    useEffect(() => {
        get("app/autoUpdateSoftwareChoice", result => {
            const autoUpdateValue: boolean = result.data.autoUpdate;
            setChosenRadio(autoUpdateValue ? "automatic" : "inform");
        });
    }, []);

    const handleChange = event => {
        setChosenRadio(event.target.value);
    };

    const isAutoUpdate: boolean = chosenRadio === "automatic";

    return (
        <BloomDialog {...propsForBloomDialog}>
            <DialogTitle
                title={useL10n(
                    "Software Updates",
                    "AutoUpdateSoftwareDialog.SoftwareUpdates"
                )}
            />
            <DialogMiddle>
                <L10nLabel
                    className="main-question"
                    english="What should Bloom do when a new version is available?"
                    l10nKey="AutoUpdateSoftwareDialog.WhatShouldBloomDo"
                />
                <RadioGroup
                    aria-label="software update choices"
                    name="choices"
                    value={chosenRadio}
                    onChange={handleChange}
                >
                    <MuiRadio
                        value="automatic"
                        label={"Automatically download and install it"}
                        l10nKey={"AutoUpdateSoftwareDialog.AutomaticUpdate"}
                    ></MuiRadio>
                    <MuiRadio
                        value="inform"
                        label={"Just let me know about the new version"}
                        l10nKey={"AutoUpdateSoftwareDialog.LetMeKnow"}
                    ></MuiRadio>
                </RadioGroup>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogOkButton
                    default={true}
                    onClick={() => {
                        postData("app/autoUpdateSoftwareChoice", {
                            dialogShown: 1,
                            autoUpdate: isAutoUpdate
                        });
                        post("common/closeReactDialog");
                    }}
                ></DialogOkButton>
            </DialogBottomButtons>
        </BloomDialog>
    );
};

WireUpForWinforms(AutoUpdateSoftwareDialog);
