import { css } from "@emotion/react";
import * as React from "react";
import { CollectionsTabIcon } from "./CollectionsTabIcon";
import { EditTabIcon } from "./EditTabIcon";
import { PublishTabIcon } from "./PublishTabIcon";

const tabs = [
    { name: "Collections", svg: <CollectionsTabIcon />, height: 32 },
    { name: "Edit", svg: <EditTabIcon />, height: 32 },
    { name: "Publish", svg: <PublishTabIcon />, height: 27 },
];

export const TopBar: React.FunctionComponent = () => {
    return (
        <div
            css={css`
                background-color: #d65649;
                padding-top: 2px;
            `}
        >
            <BloomTabs />
        </div>
    );
};

const Tab: React.FunctionComponent<{
    tab: { name: string; svg: React.ReactNode; height: number };
    selected: boolean;
    select: () => void;
}> = (props) => {
    return (
        <li role="presentation">
            <a
                role="tab"
                aria-selected={props.selected ? "true" : "false"}
                css={css`
                    // style as big rectangular tab
                    color: white;
                    text-align: center;
                    padding: 14px 16px;
                    text-decoration: none;
                    height: 55px;

                    background-color: #575757;
                    border: solid thin black;
                    border-top-left-radius: 4px;
                    border-top-right-radius: 4px;
                    font-weight: normal;
                    &[aria-selected="true"] {
                        background-color: #2e2e2e;
                        font-weight: bold;
                    }
                    &:hover {
                        color: black;
                        background-color: white;
                    }
                    display: flex;
                    flex-direction: column;
                    justify-content: space-between;
                    gap: 9px;
                    justify-content: center;
                    align-items: center;
                    font-size: 12px;
                    font-family: "segoe ui";
                    padding: 10px 21px;
                `}
                // when clicked, add "selected" class to this tab
                onClick={props.select}
            >
                {props.tab.svg}
                {props.tab.name}
            </a>
        </li>
    );
};
export const BloomTabs: React.FunctionComponent = () => {
    const [selectedTab, setSelectedTab] = React.useState("Collection");
    return (
        <ul
            role="tablist"
            css={
                // style as tabs
                css`
                    display: flex;
                    flex-direction: row;
                    list-style-type: none;
                    margin: 0;
                    padding: 0;
                    gap: 1px;
                `
            }
        >
            {tabs.map((tab) => (
                <Tab
                    key={tab.name}
                    tab={tab}
                    selected={tab.name === selectedTab}
                    select={() => setSelectedTab(tab.name)}
                />
            ))}
        </ul>
    );
};
