import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useState } from "react";
import { get, postString } from "../utils/bloomApi";
import { useL10n } from "./l10nHooks";
import { List, ListItem, ListItemText, Typography } from "@mui/material";
import { makeStyles } from "@mui/styles";

interface IXmatterInfo {
    displayName: string;
    description: string;
    internalName: string;
}

const useStyles = makeStyles(() => ({
    root: {
        width: "100%",
        height: 140,
        maxWidth: 320,
    },
}));

const XmatterChooserControl: React.FunctionComponent = () => {
    const [selectedXmatter, setSelectedXmatter] = useState<string | undefined>(
        undefined,
    );
    const [xmatterData, setXmatterData] = useState<IXmatterInfo[] | undefined>(
        undefined,
    );

    const handleListItemClick = (event) => {
        const typographyElement = event.target;
        const newXmatterDisplay = typographyElement.innerText as string;
        const newXmatter = findByDisplayName(newXmatterDisplay);
        setSelectedXmatter(newXmatter?.internalName);
        postString(
            "settings/xmatter",
            newXmatter ? newXmatter.internalName : "",
        );
    };

    const findByDisplayName = (
        displayName: string,
    ): IXmatterInfo | undefined => {
        if (!xmatterData) return undefined;
        return xmatterData!.find((xm) => xm.displayName === displayName);
    };

    useEffect(() => {
        get("settings/xmatter", (result) => {
            setSelectedXmatter(result.data.currentXmatter as string);
            setXmatterData(result.data.xmatterOfferings as IXmatterInfo[]);
        });
    }, []);

    const xMatterPackLabel = useL10n(
        "Front/Back Matter Pack",
        "CollectionSettingsDialog.BookMakingTab.Front/BackMatterPack",
    );

    const makeXmatterList = (): JSX.Element[] => {
        if (!xmatterData) return [<React.Fragment key={0} />];
        return xmatterData.map((item, index) => (
            <ListItem
                key={index}
                button
                selected={selectedXmatter === item.internalName}
                autoFocus={selectedXmatter === item.internalName}
                onClick={(event) => handleListItemClick(event)}
                css={css`
                    div {
                        margin-top: 0;
                        margin-bottom: 0;
                    }
                    span {
                        font-size: 0.8rem;
                    }
                `}
            >
                <ListItemText primary={item.displayName} />
            </ListItem>
        ));
    };

    const getSelectedIndex = (): number => {
        return xmatterData!.findIndex(
            (xmatter) => xmatter.internalName === selectedXmatter,
        );
    };

    const description = xmatterData
        ? xmatterData[getSelectedIndex()].description
        : "";

    return (
        <div
            css={css`
                height: 220px; // Don't change height of control as descriptions change.
            `}
        >
            <div className={useStyles().root}>
                <Typography
                    css={css`
                        font-weight: 700 !important;
                    `}
                >
                    {xMatterPackLabel}
                </Typography>
                <List
                    css={css`
                        height: 106px;
                        border: 1px solid black;
                        padding: 0 !important;
                        overflow-y: auto;
                        background-color: white;
                    `}
                    component="nav"
                    aria-label="xmatter chooser list"
                    dense
                >
                    {makeXmatterList()}
                </List>
                <Typography
                    css={css`
                        margin-top: 10px !important;
                        font-size: 0.8rem !important;
                    `}
                >
                    {description}
                </Typography>
            </div>
        </div>
    );
};

export default XmatterChooserControl;
