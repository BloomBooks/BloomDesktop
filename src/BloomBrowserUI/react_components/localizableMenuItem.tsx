/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { ReactNode, useEffect, useState } from "react";
import { useL10n } from "./l10nHooks";
import {
    Checkbox,
    ListItemIcon,
    ListItemText,
    MenuItem,
    TypographyProps
} from "@material-ui/core";
import NestedMenuItem from "material-ui-nested-menu-item";
import CheckBoxOutlineBlankIcon from "@material-ui/icons/CheckBoxOutlineBlank";
import CheckBoxIcon from "@material-ui/icons/CheckBox";
import { BloomApi } from "../utils/bloomApi";
import { useEnterpriseAvailable } from "./requiresBloomEnterprise";

interface BaseLocalizableMenuItemProps {
    english: string;
    l10nId: string;
}

interface LocalizableMenuItemProps extends BaseLocalizableMenuItemProps {
    onClick: React.MouseEventHandler;
    icon?: ReactNode;
    addEllipsis?: boolean;
    requiresEnterprise?: boolean;
}

interface LocalizableCheckboxMenuItemProps
    extends BaseLocalizableMenuItemProps {
    onClick: React.MouseEventHandler;
    apiEndpoint: string;
}

const kIconCheckboxAffordance = 28;
const kEnterpriseStickerAffordance = 28;
const typographyProps: TypographyProps = {
    variant: "h6"
};
const menuItemGray = "rgba(0, 0, 0, 0.64)";

export const LocalizableMenuItem: React.FunctionComponent<LocalizableMenuItemProps> = props => {
    const label = useL10n(props.english, props.l10nId);
    const enterpriseAvailable = useEnterpriseAvailable();

    const iconElement = props.icon ? (
        <ListItemIcon
            css={css`
                width: ${kIconCheckboxAffordance}px !important; // overrides MUI default that leaves way too much space
                min-width: unset !important;
            `}
        >
            {props.icon}
        </ListItemIcon>
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

    const enterpriseElement = props.requiresEnterprise ? (
        <img
            css={css`
                width: ${kEnterpriseStickerAffordance}px !important;
                margin-left: 12px;
            `}
            src="../images/bloom-enterprise-badge.svg"
            title={enterpriseAvailable ? undefined : requiresEnterpriseTooltip}
        />
    ) : (
        <div
            css={css`
                width: ${kEnterpriseStickerAffordance}px !important;
            `}
        />
    );

    const openCollectionSettings = () =>
        BloomApi.post("common/showSettingsDialog?tab=enterprise");

    const menuClickHandler = props.requiresEnterprise
        ? enterpriseAvailable
            ? props.onClick
            : openCollectionSettings
        : props.onClick;

    return (
        <MenuItem
            key={props.l10nId}
            onClick={menuClickHandler}
            dense
            css={css`
                padding: 0 6px !important; // eliminate top and bottom padding to make even denser
            `}
        >
            <React.Fragment>
                {iconElement}
                <ListItemText
                    css={css`
                        span {
                            font-weight: 400 !important; // H6 defaults to 500; too thick
                            font-size: 1.1rem !important; // actually want something between H5 and H6
                            color: ${menuItemGray} !important;
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
    );
};

export const LocalizableCheckboxMenuItem: React.FunctionComponent<LocalizableCheckboxMenuItemProps> = props => {
    const label = useL10n(props.english, props.l10nId);
    const [checked, setChecked] = useState(false);
    useEffect(() => {
        BloomApi.getBoolean(props.apiEndpoint, value => {
            setChecked(value);
        });
    }, []);

    return (
        <MenuItem
            key={props.l10nId}
            onClick={props.onClick}
            dense
            css={css`
                padding: 0 6px !important; // eliminate top and bottom padding to make even denser
            `}
        >
            <Checkbox
                icon={<CheckBoxOutlineBlankIcon htmlColor={menuItemGray} />}
                checkedIcon={<CheckBoxIcon htmlColor={menuItemGray} />}
                checked={checked}
                onChange={e => {
                    BloomApi.postBoolean(props.apiEndpoint, e.target.checked);
                }}
                css={css`
                    width: ${kIconCheckboxAffordance}px !important;
                    padding: 0 !important;
                    font-size: 1.2rem !important;
                    margin-left: -2px !important; // adjust checkbox over a bit
                    margin-right: 2px !important;
                `}
            ></Checkbox>
            <ListItemText
                css={css`
                    span {
                        font-weight: 400 !important; // H6 defaults to 500; too thick
                        font-size: 1.1rem !important; // slightly larger text
                        color: ${menuItemGray} !important;
                    }
                `}
                primaryTypographyProps={typographyProps}
            >
                {label}
            </ListItemText>
        </MenuItem>
    );
};

export const LocalizableNestedMenuItem: React.FunctionComponent<BaseLocalizableMenuItemProps> = props => {
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
                font-size: 1.1rem !important; // slightly larger text
                color: ${menuItemGray} !important;
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
