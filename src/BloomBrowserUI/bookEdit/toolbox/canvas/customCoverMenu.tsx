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
    setCustom: (value: "auto" | "custom" | "customStartOver") => void;
}> = (props) => {
    const autoLabel = useL10n("Auto", "EditTab.CustomCover.Auto");
    const customLabel = useL10n("Custom", "EditTab.CustomCover.Custom");

    const handleChange = (event: SelectChangeEvent<string>) => {
        const selection = event.target.value as
            | "auto"
            | "custom"
            | "customStartOver";
        props.setCustom(selection);
    };

    const renderMenuItem = (
        value: "auto" | "custom" | "customStartOver",
        label: string,
        checked: boolean,
    ) => {
        return (
            <MenuItem value={value}>
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
                    value={props.isCustom ? "custom" : "auto"}
                    onChange={handleChange}
                    displayEmpty
                    renderValue={() => ""}
                >
                    {renderMenuItem("auto", autoLabel, !props.isCustom)}
                    {renderMenuItem("custom", customLabel, props.isCustom)}
                    {renderMenuItem(
                        "customStartOver",
                        "new custom layout",
                        false,
                    )}
                </Select>
            </div>
        </ThemeProvider>
    );
};
