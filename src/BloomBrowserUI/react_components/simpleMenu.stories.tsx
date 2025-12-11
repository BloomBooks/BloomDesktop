import * as React from "react";
import { SimpleMenu } from "./simpleMenu";
import { SimpleMenuItem } from "./SimpleMenuItem";

const menuItems: (SimpleMenuItem | "-")[] = [
    {
        text: "About my Avatar...",
        l10nKey: "TeamCollection.AboutAvatar",
        action: () => undefined,
    },
];

const menuBoxStyles: React.CSSProperties = {
    display: "flex",
    justifyContent: "flex-end",
    border: "1px solid red",
    padding: 20,
    backgroundColor: "black",
    width: 150,
};

export default {
    title: "Team Collection components/Menu component",
};

export const SimpleMenuTest = () => (
    <div style={menuBoxStyles}>
        <SimpleMenu
            text="..."
            l10nKey="Common.Ellipsis"
            temporarilyDisableI18nWarning={true}
            items={menuItems}
        ></SimpleMenu>
    </div>
);

SimpleMenuTest.story = {
    name: "SimpleMenu test",
};
