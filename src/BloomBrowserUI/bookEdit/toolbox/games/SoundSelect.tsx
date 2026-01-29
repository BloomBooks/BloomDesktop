import { css, ThemeProvider } from "@emotion/react";
import * as React from "react";
import { MenuItem } from "@mui/material";
import BloomSelect from "../../../react_components/bloomSelect";
import {
    kOptionPanelBackgroundColor,
    toolboxMenuPopupTheme,
} from "../../../bloomMaterialUITheme";
import { SoundType } from "./GameTool";

export const SoundSelect: React.FunctionComponent<{
    soundType: SoundType;
    options: { label: string; id: string; divider: boolean }[];
    value: string;
    setValue: (soundType: SoundType, value: string) => void;
}> = (props) => {
    return (
        <ThemeProvider theme={toolboxMenuPopupTheme}>
            <BloomSelect
                variant="standard"
                css={css`
                    svg.MuiSvgIcon-root {
                        color: white !important;
                    }
                    ul {
                        background-color: ${kOptionPanelBackgroundColor} !important;
                    }
                    fieldset {
                        border-color: rgba(255, 255, 255, 0.5) !important;
                    }
                `}
                size="small"
                value={props.value}
                onChange={(event) => {
                    const newSoundId = event.target.value as string;
                    props.setValue(props.soundType, newSoundId);
                }}
                disabled={false}
            >
                {props.options.map((option) => (
                    <MenuItem
                        value={option.id}
                        key={option.id}
                        disabled={false}
                        divider={option.divider}
                    >
                        <div>{option.label}</div>
                    </MenuItem>
                ))}
            </BloomSelect>
        </ThemeProvider>
    );
};
