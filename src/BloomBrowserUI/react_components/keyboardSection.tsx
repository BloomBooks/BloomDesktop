import { css } from "@emotion/react";

import * as React from "react";
import { useState } from "react";
import { ListSubheader, MenuItem, SelectChangeEvent } from "@mui/material";
import { Typography } from "@mui/material";
import { get, postData } from "../utils/bloomApi";
import { useL10n } from "./l10nHooks";
import { useMountEffect } from "../utils/useMountEffect";
import WinFormsStyleSelect from "./winFormsStyleSelect";

interface IAutomaticResolvesTo {
    kind: "system" | "kmw" | "none";
    displayName: string;
}

interface IInstalledKeyboard {
    id: string;
    name: string;
}

interface ICloudKeyboard {
    id: string;
    name: string;
    downloads: number;
}

// Shape of GET settings/keyboardsForLanguage?languageNumber=<n> (plan item 6).
// NOTE: `languageTag` is not part of the plan's original contract; this
// component needs it to build the "kmw:<id>@<tag>" raw setting string for a
// cloud-keyboard selection (see cloudValue below), so the C# side must add it
// alongside the fields the plan already lists.
interface IKeyboardsForLanguage {
    current: string;
    languageTag: string;
    automaticResolvesTo: IAutomaticResolvesTo;
    installed: IInstalledKeyboard[];
    cloud: ICloudKeyboard[];
}

// The raw setting string forms, per Design/Keyboards/keyboard-setting-plan.md.
const kAutomaticValue = "";
const kCloudUnavailableValue = "__cloud_unavailable__";

function systemValue(id: string): string {
    return `system:${id}`;
}

function cloudValue(id: string, languageTag: string): string {
    return `kmw:${id}@${languageTag}`;
}

const secondaryTextCss = css`
    font-size: 0.8em;
    color: #666;
`;

// Renders once per language row (1-3) in the Book Making tab, under the
// corresponding SingleFontSection. Offers Automatic (default), then any OS
// input methods installed for this language, then Keyman cloud keyboards
// (server-ordered by popularity).
const KeyboardSection: React.FunctionComponent<{
    languageNumber: number;
    languageName: string;
}> = (props) => {
    const [data, setData] = useState<IKeyboardsForLanguage | undefined>(
        undefined,
    );
    const [current, setCurrent] = useState<string>(kAutomaticValue);

    useMountEffect(() => {
        get(
            `settings/keyboardsForLanguage?languageNumber=${props.languageNumber}`,
            (result) => {
                const keyboardData: IKeyboardsForLanguage = result.data;
                setData(keyboardData);
                setCurrent(keyboardData.current);
            },
        );
    });

    const keyboardForLabel = useL10n(
        "Keyboard for {0}",
        "CollectionSettingsDialog.BookMakingTab.KeyboardFor",
        "{0} is a language name.",
        props.languageName,
    );

    const automaticLabel = useL10n(
        "Automatic",
        "CollectionSettingsDialog.BookMakingTab.Keyboard.Automatic",
    );

    const automaticResolvesToLabel = useL10n(
        "Currently: {0}",
        "CollectionSettingsDialog.BookMakingTab.Keyboard.AutomaticResolvesTo",
        "{0} is the name of the keyboard or input method that Automatic currently resolves to on this machine.",
        data?.automaticResolvesTo.displayName,
    );

    const installedGroupLabel = useL10n(
        "Installed input methods",
        "CollectionSettingsDialog.BookMakingTab.Keyboard.InstalledGroup",
    );

    const cloudGroupLabel = useL10n(
        "Keyman (online) keyboards",
        "CollectionSettingsDialog.BookMakingTab.Keyboard.CloudGroup",
    );

    const cloudUnavailableLabel = useL10n(
        "Not available offline",
        "CollectionSettingsDialog.BookMakingTab.Keyboard.CloudUnavailable",
        "Shown in place of the online Keyman keyboard list when Bloom cannot reach the Keyman server.",
    );

    // A template, not localized per-item: useL10n cannot be called a variable
    // number of times (once per cloud keyboard), so we localize the template
    // once here and substitute the count ourselves for each item below.
    const downloadCountTemplate = useL10n(
        "{0} downloads",
        "CollectionSettingsDialog.BookMakingTab.Keyboard.DownloadCount",
        "{0} is a number of times a Keyman keyboard has been downloaded.",
    );

    const handleChange = (event: SelectChangeEvent) => {
        const newValue = event.target.value;
        setCurrent(newValue);
        postData("settings/setKeyboardForLanguage", {
            languageNumber: props.languageNumber,
            keyboard: newValue,
        });
    };

    // Wait for the initial fetch, matching how FontScriptSettingsControl
    // gates SingleFontSection on its font data being available.
    if (!data) return null;

    const menuItems: JSX.Element[] = [
        <MenuItem key="automatic" value={kAutomaticValue}>
            <div
                css={css`
                    display: flex;
                    flex-direction: column;
                `}
            >
                <span>{automaticLabel}</span>
                <span css={secondaryTextCss}>{automaticResolvesToLabel}</span>
            </div>
        </MenuItem>,
    ];

    if (data.installed.length > 0) {
        menuItems.push(
            <ListSubheader key="installed-header">
                {installedGroupLabel}
            </ListSubheader>,
        );
        data.installed.forEach((keyboard) => {
            menuItems.push(
                <MenuItem
                    key={`installed-${keyboard.id}`}
                    value={systemValue(keyboard.id)}
                >
                    {keyboard.name}
                </MenuItem>,
            );
        });
    }

    menuItems.push(
        <ListSubheader key="cloud-header">{cloudGroupLabel}</ListSubheader>,
    );
    if (data.cloud.length > 0) {
        data.cloud.forEach((keyboard) => {
            menuItems.push(
                <MenuItem
                    key={`cloud-${keyboard.id}`}
                    value={cloudValue(keyboard.id, data.languageTag)}
                >
                    <div
                        css={css`
                            display: flex;
                            flex-direction: column;
                        `}
                    >
                        <span>{keyboard.name}</span>
                        <span css={secondaryTextCss}>
                            {downloadCountTemplate.replace(
                                "{0}",
                                keyboard.downloads.toLocaleString(),
                            )}
                        </span>
                    </div>
                </MenuItem>,
            );
        });
    } else {
        menuItems.push(
            <MenuItem
                key="cloud-unavailable"
                value={kCloudUnavailableValue}
                disabled
            >
                {cloudUnavailableLabel}
            </MenuItem>,
        );
    }

    return (
        <React.Fragment>
            <Typography
                css={css`
                    font-weight: 700 !important;
                `}
            >
                {keyboardForLabel}
            </Typography>
            <WinFormsStyleSelect
                idKey={`keyboard-${props.languageNumber}`}
                currentValue={current}
                onChangeHandler={handleChange}
            >
                {menuItems}
            </WinFormsStyleSelect>
        </React.Fragment>
    );
};

export default KeyboardSection;
