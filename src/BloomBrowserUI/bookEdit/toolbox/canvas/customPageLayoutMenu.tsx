import * as React from "react";
import { css, ThemeProvider } from "@emotion/react";
import { Button, Menu } from "@mui/material";
import ArrowDropDownIcon from "@mui/icons-material/ArrowDropDown";
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
    const [menuAnchor, setMenuAnchor] = React.useState<HTMLElement>();
    const selectedLayoutLabel = useL10n(
        props.isCustom ? "Custom Layout" : "Standard Layout",
        props.isCustom
            ? "EditTab.CustomCover.CustomLayout"
            : "EditTab.CustomCover.StandardLayout",
    );

    const handleOpenMenu = (event: React.MouseEvent<HTMLElement>) => {
        setMenuAnchor(event.currentTarget);
    };

    const handleCloseMenu = () => {
        setMenuAnchor(undefined);
    };

    const handleSelect = (
        selection: "standard" | "custom",
        event: React.MouseEvent<HTMLElement>,
    ) => {
        const keepCustomLayoutDataWhenSwitchingToStandard =
            selection === "standard" && event.shiftKey && event.ctrlKey;
        handleCloseMenu();
        props.setCustom(selection, keepCustomLayoutDataWhenSwitchingToStandard);
    };

    return (
        <ThemeProvider theme={toolboxMenuPopupTheme}>
            <div
                css={css`
                    display: flex;
                    align-items: center;
                `}
            >
                <Button
                    className="above-page-control-typography"
                    css={css`
                        color: ${kBloomPurple};
                        min-width: 0;
                        padding: 0;
                        text-transform: none;
                        font-family: inherit;
                        font-size: inherit;
                        line-height: inherit;
                        font-weight: inherit;

                        &:hover {
                            background-color: transparent;
                        }

                        .MuiButton-endIcon {
                            margin-left: 2px;
                            margin-right: 0;
                            color: ${kBloomPurple};
                        }
                    `}
                    disableRipple
                    endIcon={<ArrowDropDownIcon />}
                    onClick={handleOpenMenu}
                >
                    {selectedLayoutLabel}
                </Button>
                <Menu
                    anchorEl={menuAnchor}
                    open={!!menuAnchor}
                    onClose={handleCloseMenu}
                    anchorOrigin={{
                        vertical: "bottom",
                        horizontal: "right",
                    }}
                    transformOrigin={{
                        vertical: "top",
                        horizontal: "right",
                    }}
                >
                    <LocalizableSelectableMenuItem
                        english="Standard"
                        l10nId="EditTab.CustomCover.Standard"
                        selected={!props.isCustom}
                        onClick={(event) => handleSelect("standard", event)}
                    />
                    <LocalizableSelectableMenuItem
                        english="Custom"
                        l10nId="EditTab.CustomCover.Custom"
                        selected={props.isCustom}
                        onClick={(event) => handleSelect("custom", event)}
                        disabled={props.disableCustomPage}
                        featureName="CustomXMatterPage"
                    />
                </Menu>
            </div>
        </ThemeProvider>
    );
};
