import * as React from "react";
import BloomButton from "./bloomButton";
import { BloomApi } from "../utils/bloomApi";
import "./AutoUpdateSoftwareDialog.less";
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import {
    Dialog,
    FormControlLabel,
    Radio,
    RadioGroup,
    Typography
} from "@material-ui/core";
import { useL10n } from "./l10nHooks";
import { useEffect, useState } from "react";

export const AutoUpdateSoftwareDialog: React.FunctionComponent = () => {
    const [chosenRadio, setChosenRadio] = useState<"automatic" | "inform">(
        "automatic"
    );
    useEffect(() => {
        BloomApi.get("app/autoUpdateSoftwareChoice", result => {
            const autoUpdateValue: boolean = result.data.autoUpdate;
            setChosenRadio(autoUpdateValue ? "automatic" : "inform");
        });
    }, []);

    const dialogTitle = useL10n(
        "Software Updates",
        "AutoUpdateSoftwareDialog.SoftwareUpdates"
    );
    const whatShouldBloomDo = useL10n(
        "What should Bloom do when a new version is available?",
        "AutoUpdateSoftwareDialog.WhatShouldBloomDo"
    );
    const downloadInstall = useL10n(
        "Automatically download and install it",
        "AutoUpdateSoftwareDialog.AutomaticUpdate"
    );
    const informOnly = useL10n(
        "Just let me know about the new version",
        "AutoUpdateSoftwareDialog.LetMeKnow"
    );

    const handleChange = event => {
        setChosenRadio(event.target.value);
    };

    const isAutoUpdate: boolean = chosenRadio == "automatic";

    return (
        <ThemeProvider theme={theme}>
            <Dialog
                className="auto-update-dialog"
                open={true}
                fullWidth={true}
                maxWidth="lg"
            >
                <div className="dialog-contents">
                    <Typography variant="h4" className="dialog-title">
                        {dialogTitle}
                    </Typography>
                    <Typography variant="h5" className="main-question">
                        {whatShouldBloomDo}
                    </Typography>
                    <RadioGroup
                        aria-label="software update choices"
                        name="choices"
                        value={chosenRadio}
                        onChange={handleChange}
                    >
                        <FormControlLabel
                            value="automatic"
                            control={<Radio color="primary" />}
                            label={
                                <Typography variant="h5">
                                    {downloadInstall}
                                </Typography>
                            }
                        />
                        <FormControlLabel
                            className="inform-label"
                            value="inform"
                            control={<Radio color="primary" />}
                            label={
                                <Typography variant="h5">
                                    {informOnly}
                                </Typography>
                            }
                        />
                    </RadioGroup>
                    <div className="spacer" />
                    <div className="align-right-bottom">
                        <BloomButton
                            className="ok-button"
                            l10nKey="Common.OK"
                            enabled={true}
                            hasText={true}
                            onClick={() => {
                                BloomApi.postData(
                                    "app/autoUpdateSoftwareChoice",
                                    {
                                        dialogShown: 1,
                                        autoUpdate: isAutoUpdate
                                    }
                                );
                                BloomApi.post("common/closeReactDialog");
                            }}
                        >
                            OK
                        </BloomButton>
                    </div>
                </div>
            </Dialog>
        </ThemeProvider>
    );
};
