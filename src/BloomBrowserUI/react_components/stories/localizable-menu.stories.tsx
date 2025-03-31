import { css } from "@emotion/react";
import { useState } from "react";
import { Button, Divider, Menu } from "@mui/material";
import DeleteIcon from "@mui/icons-material/Delete";
import {
    LocalizableMenuItem,
    LocalizableCheckboxMenuItem,
    LocalizableNestedMenuItem
} from "../localizableMenuItem";

import { Meta, StoryObj } from "@storybook/react";

const meta: Meta = {
    title: "Localizable Widgets/Localizable Menu"
};

export default meta;
type Story = StoryObj;

const useMenuBox = (menuItems: JSX.Element[]) => {
    const [anchorEl, setAnchorEl] = useState<HTMLButtonElement | undefined>(
        undefined
    );
    return (
        <div
            css={css`
                width: 200px;
                height: 100px;
                background-color: tan;
                display: flex;
                flex-direction: row;
                justify-content: center;
                align-items: center;
            `}
        >
            <Button
                color="primary"
                onClick={event =>
                    setAnchorEl(event.target as HTMLButtonElement)
                }
                css={css`
                    background-color: lightblue !important;
                    width: 120px;
                    height: 40px;
                `}
            >
                Click Me!
            </Button>
            <Menu
                anchorEl={anchorEl}
                keepMounted
                open={Boolean(anchorEl)}
                anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
                transformOrigin={{ vertical: "top", horizontal: "left" }}
                onClose={() => {
                    setAnchorEl(undefined);
                }}
            >
                {menuItems}
            </Menu>
        </div>
    );
};

const normalMenuItem = (
    <LocalizableMenuItem
        english="Motion Book"
        l10nId="PublishTab.Android.MotionBookMode"
        icon={<DeleteIcon />}
        onClick={() => {}}
        disabled={true}
        tooltipIfDisabled="This has a tooltip!"
    />
);

const checkboxMenuItem = (
    <LocalizableCheckboxMenuItem
        english="Decodable Reader"
        l10nId="TemplateBooks.BookName.Decodable Reader"
        apiEndpoint="some/api/endpoint"
        onClick={() => {}}
    />
);

const normalMenuItemWithEllipsisAndEnterprise = (
    <LocalizableMenuItem
        english="Open or Create Another Collection"
        l10nId="CollectionTab.OpenCreateCollectionMenuItem"
        addEllipsis={true}
        featureName="foobar"
        onClick={() => {}}
    />
);

const requiresEnterpriseSubscriptionWithIcon = (
    <LocalizableMenuItem
        english="BE subscription required, has disabled icon"
        l10nId="already-localized"
        featureName="foobar"
        icon={<DeleteIcon />}
        onClick={() => {}}
    />
);

const nestedMenu = (
    <LocalizableNestedMenuItem
        english="Troubleshooting"
        l10nId="CollectionTab.ContextMenu.Troubleshooting"
    >
        {[
            normalMenuItem,
            checkboxMenuItem,
            normalMenuItemWithEllipsisAndEnterprise,
            requiresEnterpriseSubscriptionWithIcon
        ]}
    </LocalizableNestedMenuItem>
);

const divider = <Divider />;

const testMenu = [
    normalMenuItem,
    normalMenuItemWithEllipsisAndEnterprise,
    requiresEnterpriseSubscriptionWithIcon,
    checkboxMenuItem,
    divider,
    nestedMenu
];

export const TestMenuStory: Story = {
    name: "test menu",
    render: () => useMenuBox(testMenu)
};
