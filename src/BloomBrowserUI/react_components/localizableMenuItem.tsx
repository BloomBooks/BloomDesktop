/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { ReactNode, useEffect, useState } from "react";
import { useL10n } from "./l10nHooks";
import {
    Checkbox,
    ListItemIcon,
    ListItemText,
    MenuItem,
    TypographyProps
} from "@mui/material";
import NestedMenuItem from "mui-nested-menu-item";
import CheckBoxOutlineBlankIcon from "@mui/icons-material/CheckBoxOutlineBlank";
import CheckBoxIcon from "@mui/icons-material/CheckBox";
import { getBoolean, post, postBoolean } from "../utils/bloomApi";
import {
    useEnterpriseAvailable,
    useGetEnterpriseStatus
} from "./requiresBloomEnterprise";
import { kBloomDisabledOpacity } from "../utils/colorUtils";

interface IBaseLocalizableMenuItemProps {
    english: string;
    l10nId: string;
    disabled?: boolean;
    tooltipIfDisabled?: string;
}

export interface ILocalizableMenuItemProps
    extends IBaseLocalizableMenuItemProps {
    onClick: React.MouseEventHandler;
    icon?: ReactNode;
    addEllipsis?: boolean;
    requiresAnyEnterprise?: boolean;
    requiresEnterpriseSubscription?: boolean;
    dontGiveAffordanceForCheckbox?: boolean;
    enterpriseTooltipOverride?: string;
}

interface ILocalizableCheckboxMenuItemProps
    extends IBaseLocalizableMenuItemProps {
    onClick: React.MouseEventHandler;
    apiEndpoint: string;
}

const kIconCheckboxAffordance = 28;
const kEnterpriseStickerAffordance = 28;
const typographyProps: TypographyProps = {
    variant: "h6"
};
const menuItemColor = "black";

export const LocalizableMenuItem: React.FunctionComponent<ILocalizableMenuItemProps> = props => {
    const label = useL10n(props.english, props.l10nId);
    // BL-10638 In the case of an expired subscription code, 'useEnterpriseAvailable()` returns false,
    // but `useGetEnterpriseStatus()` returns "Subscription". That state of things is useful for the
    // CollectionSettingsDialog, but not here in menu items. The absence of enterpriseAvailable needs to
    // take precedence. But by rules of hooks we still need to run the hook and then modify the value.
    const enterpriseAvailable = useEnterpriseAvailable();
    let enterpriseStatus = useGetEnterpriseStatus();
    if (!enterpriseAvailable) {
        enterpriseStatus = "None";
    }

    const meetsEnterpriseRequirement = props.requiresEnterpriseSubscription
        ? enterpriseStatus === "Subscription"
        : props.requiresAnyEnterprise
        ? enterpriseAvailable
        : true;

    const iconElement = props.icon ? (
        <ListItemIcon
            css={css`
                width: ${kIconCheckboxAffordance}px !important; // overrides MUI default that leaves way too much space
                min-width: unset !important;

                // We can't use the disabled prop because it prevents the click from opening settings.
                // So we just make it look disabled (using the same setting as Mui-disabled).
                // And we only do it on the icon and text --not on the menu item-- so the enterprise icon doesn't look disabled.
                opacity: ${meetsEnterpriseRequirement
                    ? undefined
                    : kBloomDisabledOpacity};
            `}
        >
            {props.icon}
        </ListItemIcon>
    ) : props.dontGiveAffordanceForCheckbox ? (
        <div />
    ) : (
        <div
            css={css`
                width: ${kIconCheckboxAffordance}px !important;
            `}
        />
    );

    const ellipsis = props.addEllipsis ? <span>...</span> : <React.Fragment />;

    const requiresEnterpriseTooltip = useL10n(
        "To use this feature, you'll need to enable Bloom Enterprise.",
        "CollectionSettingsDialog.RequiresEnterprise_ToolTip_"
    );

    const enterpriseElement =
        props.requiresAnyEnterprise || props.requiresEnterpriseSubscription ? (
            <img
                css={css`
                    width: ${kEnterpriseStickerAffordance}px !important;
                    margin-left: 12px;
                `}
                src="/bloom/images/bloom-enterprise-badge.svg"
                title={
                    meetsEnterpriseRequirement
                        ? undefined
                        : props.enterpriseTooltipOverride ||
                          requiresEnterpriseTooltip
                }
            />
        ) : (
            <div
                css={css`
                    width: ${kEnterpriseStickerAffordance}px !important;
                `}
            />
        );

    const openCollectionSettings = () =>
        post("common/showSettingsDialog?tab=enterprise");

    const menuClickHandler = meetsEnterpriseRequirement
        ? props.onClick
        : openCollectionSettings;

    // The "div" wrapper is necessary to get the tooltip to work on a disabled MenuItem.
    return (
        <div title={props.disabled ? props.tooltipIfDisabled : undefined}>
            <MenuItem
                key={props.l10nId}
                onClick={menuClickHandler}
                dense={true}
                css={css`
                    padding: 0 6px !important; // eliminate top and bottom padding to make even denser
                    font-size: 14pt;
                `}
                disabled={props.disabled}
            >
                <React.Fragment>
                    {iconElement}
                    <ListItemText
                        css={css`
                            .MuiTypography-h6 {
                                font-weight: 400 !important; // H6 defaults to 500; too thick
                                font-family: Segoe UI, NotoSans, Roboto,
                                    sans-serif;
                                color: ${menuItemColor} !important;

                                // We can't use the disabled prop because it prevents the click from opening settings and
                                // prevents the tooltip. So we just make it look disabled (using the same setting as Mui-disabled).
                                // And we only do it on the icon and text so the enterprise icon doesn't look disabled.
                                opacity: ${meetsEnterpriseRequirement
                                    ? undefined
                                    : kBloomDisabledOpacity};
                            }
                        `}
                        primaryTypographyProps={typographyProps}
                    >
                        {label}
                        {ellipsis}
                    </ListItemText>
                    {enterpriseElement}
                </React.Fragment>
            </MenuItem>
        </div>
    );
};

export const LocalizableCheckboxMenuItem: React.FunctionComponent<ILocalizableCheckboxMenuItemProps> = props => {
    const label = useL10n(props.english, props.l10nId);
    const [checked, setChecked] = useState(false);
    useEffect(() => {
        getBoolean(props.apiEndpoint, value => {
            setChecked(value);
        });
    }, []);

    // The "div" wrapper is necessary to get the tooltip to work on a disabled item.
    return (
        <div title={props.disabled ? props.tooltipIfDisabled : undefined}>
            <MenuItem
                key={props.l10nId}
                onClick={props.onClick}
                dense={true}
                css={css`
                    padding: 0 6px !important; // eliminate top and bottom padding to make even denser
                    font-size: 14pt;
                `}
                disabled={props.disabled}
            >
                <Checkbox
                    icon={
                        <CheckBoxOutlineBlankIcon htmlColor={menuItemColor} />
                    }
                    checkedIcon={<CheckBoxIcon htmlColor={menuItemColor} />}
                    checked={checked}
                    onChange={e => {
                        postBoolean(props.apiEndpoint, e.target.checked);
                        setChecked(e.target.checked);
                    }}
                    css={css`
                        width: ${kIconCheckboxAffordance}px !important;
                        padding: 0 !important;
                        font-size: 1.1rem !important;
                        margin-left: -2px !important; // adjust checkbox over a bit
                        margin-right: 2px !important;
                    `}
                />
                <ListItemText
                    css={css`
                        .MuiTypography-h6 {
                            font-weight: 400 !important; // H6 defaults to 500; too thick
                            font-family: Segoe UI, NotoSans, Roboto, sans-serif;
                            color: ${menuItemColor} !important;
                        }
                    `}
                    primaryTypographyProps={typographyProps}
                >
                    {label}
                </ListItemText>
            </MenuItem>
        </div>
    );
};

export const LocalizableNestedMenuItem: React.FunctionComponent<IBaseLocalizableMenuItemProps> = props => {
    const label = useL10n(props.english, props.l10nId);
    if (!props.children) {
        return <React.Fragment />;
    }
    return (
        // Can't find any doc on parentMenuOpen. Examples set it to the same value
        // as the open prop of the parent menu. But it seems to work fine just set
        // to true. (If omitted, however, the child menu does not appear when the
        // parent is hovered over.)
        <NestedMenuItem
            // Unfortunately, I can't figure out how to pass the same TypographyProps to this 3rd-party
            // NestedMenuItem (it doesn't seem to use MUI Typography internally). And I can't get
            // emotion to work on the MUI Menu item. So we have 2 ways to accomplish the same thing.
            // Not ideal, but it works.
            css={css`
                margin-left: ${kIconCheckboxAffordance}px !important;
                font-weight: 400 !important;
                font-family: Segoe UI, NotoSans, Roboto, sans-serif !important;
                font-size: 1rem !important;
                color: ${menuItemColor} !important;
                padding: 4px 6px 0 6px !important; // adjust for denser layout
                justify-content: space-between !important; // move sub-menu arrow to right
            `}
            key={props.l10nId}
            label={label}
            parentMenuOpen={true}
        >
            {props.children}
        </NestedMenuItem>
    );
};
