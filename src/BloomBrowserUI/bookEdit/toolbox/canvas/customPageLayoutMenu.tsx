import * as React from "react";
import { css, ThemeProvider } from "@emotion/react";
import { Select } from "@mui/material";
import CheckIcon from "@mui/icons-material/Check";
import { toolboxMenuPopupTheme } from "../../../bloomMaterialUITheme";
import { kBloomPurple } from "../../../utils/colorUtils";
import { Span } from "../../../react_components/l10nComponents";
import { LocalizableMenuItem } from "../../../react_components/localizableMenuItem";

const kCustomXMatterPageFeatureName = "CustomXMatterPage";

export const CustomPageLayoutMenu: React.FunctionComponent<{
    isCustom: boolean;
    disableCustomPage?: boolean;
    setCustom: (value: "standard" | "custom" | "customStartOver") => void;
}> = (props) => {
    const renderMenuItem = (
        value: "standard" | "custom",
        english: string,
        l10nId: string,
        checked: boolean,
        disabled?: boolean,
        featureName?: string,
    ) => {
        const onClick = (event: React.MouseEvent) => {
            let selection: "standard" | "custom" | "customStartOver" = value;
            if (
                value === "custom" &&
                !props.isCustom &&
                event.shiftKey &&
                event.ctrlKey
            ) {
                selection = "customStartOver";
            }
            props.setCustom(selection);
        };

        return (
            <LocalizableMenuItem
                english={english}
                l10nId={l10nId}
                onClick={onClick}
                disabled={disabled}
                featureName={featureName}
                icon={
                    <CheckIcon
                        fontSize="small"
                        css={css`
                            visibility: ${checked ? "visible" : "hidden"};
                        `}
                    />
                }
            />
        );
    };

    return (
        <ThemeProvider theme={toolboxMenuPopupTheme}>
            <div
                css={css`
                    display: flex;
                    color: ${kBloomPurple};
                `}
            >
                <Span l10nKey="EditTab.CustomCover.CoverLayout">
                    Cover Layout:
                </Span>
                <Select
                    css={css`
                        color: ${kBloomPurple};
                        margin-left: 4px;
                        min-width: 24px;
                        .MuiSelect-icon {
                            color: ${kBloomPurple};
                        }
                        .MuiSelect-select {
                            padding: 2px 0 2px 4px;
                        }
                        &.Mui-focused .MuiOutlinedInput-notchedOutline {
                            border: none;
                        }
                        .MuiOutlinedInput-notchedOutline {
                            border: none;
                        }
                        &:hover .MuiOutlinedInput-notchedOutline {
                            border: none;
                        }
                    `}
                    size="small"
                    value={props.isCustom ? "custom" : "standard"}
                    displayEmpty
                    renderValue={() => ""}
                >
                    {renderMenuItem(
                        "standard",
                        "Standard",
                        "EditTab.CustomCover.Standard",
                        !props.isCustom,
                    )}
                    {renderMenuItem(
                        "custom",
                        "Custom",
                        "EditTab.CustomCover.Custom",
                        props.isCustom,
                        props.disableCustomPage,
                        kCustomXMatterPageFeatureName,
                    )}
                </Select>
            </div>
        </ThemeProvider>
    );
};
