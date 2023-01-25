import * as React from "react";
import BloomButton from "./bloomButton";
import { get, post, postData } from "../utils/bloomApi";
import "./AutoUpdateSoftwareDialog.less";
import { lightTheme } from "../bloomMaterialUITheme";
import {
    ThemeProvider,
    Theme,
    StyledEngineProvider
} from "@mui/material/styles";
import { FormControlLabel, Radio, RadioGroup } from "@mui/material";
import { L10nLabel } from "./L10nLabel";
import { useEffect, useState } from "react";
import { WireUpForWinforms } from "../utils/WireUpWinform";

export const AutoUpdateSoftwareDialog: React.FunctionComponent = () => {
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
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={lightTheme}>
                <div className="auto-update-dialog">
                    <L10nLabel
                        variant="h4"
                        className="dialog-title"
                        english="Software Updates"
                        l10nKey="AutoUpdateSoftwareDialog.SoftwareUpdates"
                    />
                    <L10nLabel
                        variant="h5"
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
                        <FormControlLabel
                            value="automatic"
                            control={<Radio color="primary" />}
                            label={
                                <L10nLabel
                                    variant="h5"
                                    english="Automatically download and install it"
                                    l10nKey="AutoUpdateSoftwareDialog.AutomaticUpdate"
                                />
                            }
                        />
                        <FormControlLabel
                            className="inform-label"
                            value="inform"
                            control={<Radio color="primary" />}
                            label={
                                <L10nLabel
                                    variant="h5"
                                    english="Just let me know about the new version"
                                    l10nKey="AutoUpdateSoftwareDialog.LetMeKnow"
                                />
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
                                postData("app/autoUpdateSoftwareChoice", {
                                    dialogShown: 1,
                                    autoUpdate: isAutoUpdate
                                });
                                post("common/closeReactDialog");
                            }}
                        >
                            OK
                        </BloomButton>
                    </div>
                </div>
            </ThemeProvider>
        </StyledEngineProvider>
    );
};

WireUpForWinforms(AutoUpdateSoftwareDialog);
