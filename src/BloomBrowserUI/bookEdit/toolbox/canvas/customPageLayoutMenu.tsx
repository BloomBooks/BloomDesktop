import * as React from "react";
import { css, ThemeProvider } from "@emotion/react";
import { Button, Link, Menu } from "@mui/material";
import ArrowDropDownIcon from "@mui/icons-material/ArrowDropDown";
import { toolboxMenuPopupTheme } from "../../../bloomMaterialUITheme";
import { kBloomPurple } from "../../../utils/colorUtils";
import { BloomTooltip } from "../../../react_components/BloomToolTip";
import { useL10n } from "../../../react_components/l10nHooks";
import { LocalizableSelectableMenuItem } from "../../../react_components/localizableMenuItem";
import { useGetFeatureStatus } from "../../../react_components/featureStatus";
import { getWorkspaceBundleExports } from "../../js/workspaceFrames";

export const CustomPageLayoutMenu: React.FunctionComponent<{
    isCustom: boolean;
    disableCustomPage?: boolean;
    setCustom: (
        value: "standard" | "custom",
        keepCustomLayoutDataWhenSwitchingToStandard: boolean,
    ) => void;
}> = (props) => {
    const [menuAnchor, setMenuAnchor] = React.useState<HTMLElement>();
    const customLayoutFeatureStatus = useGetFeatureStatus("CustomXMatterPage");
    const blockedBySubscription =
        customLayoutFeatureStatus !== undefined &&
        !customLayoutFeatureStatus.enabled;
    const blockedByLegacyTheme =
        !!props.disableCustomPage && !blockedBySubscription;
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

    const handleOpenThemeAndLayoutSettings = () => {
        handleCloseMenu();
        getWorkspaceBundleExports().showBookSettingsDialog("themeAndLayout");
    };

    const handleSelect = (
        selection: "standard" | "custom",
        event: React.MouseEvent<Element>,
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
                    {blockedByLegacyTheme ? (
                        <BloomTooltip
                            open={!!menuAnchor}
                            placement="right"
                            enableClickInTooltip={true}
                            tip={
                                <LegacyThemeCustomLayoutTooltip
                                    onOpenPageThemeSettings={
                                        handleOpenThemeAndLayoutSettings
                                    }
                                />
                            }
                        >
                            <LocalizableSelectableMenuItem
                                english="Custom"
                                l10nId="EditTab.CustomCover.Custom"
                                selected={props.isCustom}
                                onClick={(event) =>
                                    handleSelect("custom", event)
                                }
                                disabled={true}
                                featureName="CustomXMatterPage"
                            />
                        </BloomTooltip>
                    ) : (
                        <LocalizableSelectableMenuItem
                            english="Custom"
                            l10nId="EditTab.CustomCover.Custom"
                            selected={props.isCustom}
                            onClick={(event) => handleSelect("custom", event)}
                            disabled={false}
                            featureName="CustomXMatterPage"
                        />
                    )}
                </Menu>
            </div>
        </ThemeProvider>
    );
};

// show a tooltip with a link to the Theme and Layout page of Book Settings when the user tries to select Custom Layout
// but their current page theme doesn't support it
const LegacyThemeCustomLayoutTooltip: React.FunctionComponent<{
    onOpenPageThemeSettings: () => void;
}> = (props) => {
    const tooltipMessage = useL10n(
        "This feature requires a newer [Page Theme].",
        "EditTab.CustomCover.Custom.DisabledForLegacyTheme.Message",
    );

    const linkStart = tooltipMessage.indexOf("[");
    const linkEnd = tooltipMessage.indexOf(
        "]",
        linkStart >= 0 ? linkStart + 1 : 0,
    );

    const beforeLink =
        linkStart >= 0 ? tooltipMessage.substring(0, linkStart) : "";
    const linkText =
        linkStart >= 0 && linkEnd > linkStart
            ? tooltipMessage.substring(linkStart + 1, linkEnd)
            : tooltipMessage;
    const afterLink =
        linkStart >= 0 && linkEnd > linkStart
            ? tooltipMessage.substring(linkEnd + 1)
            : "";

    return (
        <div
            css={css`
                font-size: 12px;
                line-height: 1.4;
            `}
        >
            {beforeLink}
            <Link
                component="button"
                type="button"
                underline="always"
                css={css`
                    color: white;
                    text-decoration-color: white;
                `}
                onClick={(event) => {
                    event.preventDefault();
                    event.stopPropagation();
                    props.onOpenPageThemeSettings();
                }}
            >
                {linkText}
            </Link>
            {afterLink}
        </div>
    );
};
