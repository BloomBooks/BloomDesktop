import * as React from "react";
import type { SelectChangeEvent } from "@mui/material/Select";
import { css, ThemeProvider } from "@emotion/react";
import { Select, MenuItem, ListItemIcon } from "@mui/material";
import CheckIcon from "@mui/icons-material/Check";
import { useL10n } from "../../../react_components/l10nHooks";
import { toolboxMenuPopupTheme } from "../../../bloomMaterialUITheme";
import { kBloomPurple } from "../../../utils/colorUtils";
import { Span } from "../../../react_components/l10nComponents";

export const CustomCoverMenu: React.FunctionComponent<{
    isCustom: boolean;
    disableCustomCover?: boolean;
    setCustom: (value: "standard" | "custom" | "customStartOver") => void;
}> = (props) => {
    const standardLabel = useL10n("Standard", "EditTab.CustomCover.Standard");
    const customLabel = useL10n("Custom", "EditTab.CustomCover.Custom");

    const handleChange = (event: SelectChangeEvent<string>) => {
        let selection = event.target.value as
            | "standard"
            | "custom"
            | "customStartOver";
        // If custom is selected with shift+ctrl held, trigger startOver behavior
        // TypeScript thinks the argument should be a SelectChangeEvent in order to pass
        // the function as the onChange handler for a Select, but in fact it always
        // comes in as a PointerEvent which has the keyboard modifier info we need.
        const pointerEvent = event as unknown as PointerEvent;
        if (
            selection === "custom" &&
            pointerEvent.shiftKey &&
            pointerEvent.ctrlKey
        ) {
            selection = "customStartOver";
        }
        props.setCustom(selection);
    };

    const renderMenuItem = (
        value: "standard" | "custom",
        label: string,
        checked: boolean,
        disabled?: boolean,
    ) => {
        return (
            <MenuItem value={value} disabled={disabled}>
                <ListItemIcon sx={{ minWidth: 32 }}>
                    <CheckIcon
                        fontSize="small"
                        sx={{ visibility: checked ? "visible" : "hidden" }}
                    />
                </ListItemIcon>
                {label}
            </MenuItem>
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
                    onChange={handleChange}
                    displayEmpty
                    renderValue={() => ""}
                >
                    {renderMenuItem("standard", standardLabel, !props.isCustom)}
                    {renderMenuItem(
                        "custom",
                        customLabel,
                        props.isCustom,
                        props.disableCustomCover,
                    )}
                </Select>
            </div>
        </ThemeProvider>
    );
};
