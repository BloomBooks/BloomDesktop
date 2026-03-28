import * as React from "react";
import type { SelectChangeEvent } from "@mui/material/Select";
import { css, ThemeProvider } from "@emotion/react";
import { Select } from "@mui/material";
import { toolboxMenuPopupTheme } from "../../../bloomMaterialUITheme";
import { kBloomPurple } from "../../../utils/colorUtils";
import { useL10n } from "../../../react_components/l10nHooks";
import { LocalizableSelectableMenuItem } from "../../../react_components/localizableMenuItem";

export const CustomPageLayoutMenu: React.FunctionComponent<{
    isCustom: boolean;
    disableCustomPage?: boolean;
    setCustom: (
        value: "standard" | "custom",
        keepCustomLayoutDataWhenSwitchingToStandard: boolean,
    ) => void;
}> = (props) => {
    const selectedLayoutLabel = useL10n(
        props.isCustom ? "Custom Layout" : "Standard Layout",
        props.isCustom
            ? "EditTab.CustomCover.CustomLayout"
            : "EditTab.CustomCover.StandardLayout",
    );

    const handleChange = (event: SelectChangeEvent<string>) => {
        const selection = event.target.value as "standard" | "custom";
        // TypeScript thinks the argument should be a SelectChangeEvent in order to pass
        // the function as the onChange handler for a Select, but in fact it always
        // comes in as a PointerEvent which has the keyboard modifier info we need.
        const nativeEvent = (event as unknown as { nativeEvent?: PointerEvent })
            .nativeEvent;
        const pointerEvent = nativeEvent ?? (event as unknown as PointerEvent);
        const keepCustomLayoutDataWhenSwitchingToStandard =
            selection === "standard" &&
            "shiftKey" in pointerEvent &&
            "ctrlKey" in pointerEvent &&
            pointerEvent.shiftKey &&
            pointerEvent.ctrlKey;
        props.setCustom(selection, keepCustomLayoutDataWhenSwitchingToStandard);
    };

    return (
        <ThemeProvider theme={toolboxMenuPopupTheme}>
            <div
                css={css`
                    display: flex;
                    margin-left: auto;
                    color: ${kBloomPurple};
                `}
            >
                {selectedLayoutLabel}
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
                    onChange={handleChange}
                    displayEmpty
                    renderValue={() => ""}
                >
                    <LocalizableSelectableMenuItem
                        english="Standard"
                        l10nId="EditTab.CustomCover.Standard"
                        selected={!props.isCustom}
                        value="standard"
                    />
                    <LocalizableSelectableMenuItem
                        english="Custom"
                        l10nId="EditTab.CustomCover.Custom"
                        selected={props.isCustom}
                        value="custom"
                        disabled={props.disableCustomPage}
                        featureName="CustomXMatterPage"
                    />
                </Select>
            </div>
        </ThemeProvider>
    );
};
