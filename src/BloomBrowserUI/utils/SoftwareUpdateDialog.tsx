import * as React from "react";
import BloomButton from "../react_components/bloomButton";
import { BloomApi } from "./bloomApi";
import "./SoftwareUpdateDialog.less";
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import {
    FormControlLabel,
    Paper,
    Radio,
    RadioGroup,
    Typography
} from "@material-ui/core";
import { useL10n } from "../react_components/l10nHooks";
import { useEffect, useState } from "react";

export const SoftwareUpdateDialog: React.FunctionComponent = () => {
    const [chosenRadio, setChosenRadio] = useState<"automatic" | "inform">(
        "automatic"
    );
    useEffect(() => {
        BloomApi.get("system/autoUpdateValues", result => {
            const autoUpdateValue: boolean = result.data.autoUpdate;
            //const dialogShownValue: number = result.data.dialogShown;
            setChosenRadio(autoUpdateValue ? "automatic" : "inform");
        });
    }, []);

    const dialogTitle = useL10n(
        "Software Updates",
        "SoftwareUpdateDialog.SoftwareUpdates"
    );
    const whatShouldBloomDo = useL10n(
        "What should Bloom do when a new version is available?",
        "SoftwareUpdateDialog.WhatShouldBloomDo"
    );
    const downloadInstall = useL10n(
        "Automatically download and install it",
        "SoftwareUpdateDialog.AutomaticUpdate"
    );
    const informOnly = useL10n(
        "Just let me know about the new version",
        "SoftwareUpdateDialog.LetMeKnow"
    );

    const handleChange = event => {
        setChosenRadio(event.target.value);
    };

    const autoUpdate: boolean = chosenRadio == "automatic";

    return (
        <ThemeProvider theme={theme}>
            <div id="software-update-dialog">
                <Paper className="canvas" elevation={3}>
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
                                BloomApi.postData("system/autoUpdateValues", {
                                    dialogShown: 1,
                                    autoUpdate: autoUpdate
                                });
                                BloomApi.post("common/closeReactDialog");
                            }}
                        >
                            OK
                        </BloomButton>
                    </div>
                </Paper>
            </div>
        </ThemeProvider>
    );
};
