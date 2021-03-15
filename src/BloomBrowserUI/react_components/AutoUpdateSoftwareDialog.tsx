import * as React from "react";
import BloomButton from "./bloomButton";
import { BloomApi } from "../utils/bloomApi";
import "./AutoUpdateSoftwareDialog.less";
import theme from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import { FormControlLabel, Radio, RadioGroup } from "@material-ui/core";
import { L10nLabel } from "./L10nLabel";
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

    const handleChange = event => {
        setChosenRadio(event.target.value);
    };

    const isAutoUpdate: boolean = chosenRadio == "automatic";

    return (
        <ThemeProvider theme={theme}>
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
                            BloomApi.postData("app/autoUpdateSoftwareChoice", {
                                dialogShown: 1,
                                autoUpdate: isAutoUpdate
                            });
                            BloomApi.post("common/closeReactDialog");
                        }}
                    >
                        OK
                    </BloomButton>
                </div>
            </div>
        </ThemeProvider>
    );
};
