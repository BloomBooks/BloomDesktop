import { css } from "@emotion/react";

import * as React from "react";
import { useEffect, useState } from "react";
import { get, postString } from "../utils/bloomApi";
import { useL10n } from "./l10nHooks";
import { MenuItem, Select, Typography } from "@mui/material";

interface PageNumberStyle {
    localizedStyle: string;
    styleKey: string;
}

const PageNumberStyleControl: React.FunctionComponent = () => {
    const pageNumberingLabel = useL10n(
        "Page Numbering Style",
        "CollectionSettingsDialog.BookMakingTab.PageNumberingStyle.PageNumberingStyleLabel",
    );

    // Default of empty string fed into Mui Select's value prop works, where 'undefined' doesn't.
    // Something about 'controlled' vs. 'uncontrolled' Select state.
    // Other than the initial empty string, 'selectedStyle' always matches the 'styleKey' of a
    // numbering style. This is important because it means that when 'handleSelectChange' (below) passes
    // the event.target.value to C#, we don't have to worry about going to find the unlocalized version
    // to store in the CollectionSettings file.
    const [selectedStyle, setSelectedStyle] = useState<string>("");

    const handleSelectChange = (event) => {
        const newSelectedStyle = event.target.value as string;
        setSelectedStyle(newSelectedStyle);
        postString("settings/numberingStyle", newSelectedStyle);
    };

    const [numberingStyles, setNumberingStyles] = useState<
        PageNumberStyle[] | undefined
    >(undefined);

    useEffect(() => {
        // Gets all available numbering styles and the current style
        get("settings/numberingStyle", (result) => {
            setSelectedStyle(result.data.currentPageNumberStyle as string);
            setNumberingStyles(
                result.data.numberingStyleData as PageNumberStyle[],
            );
        });
    }, []);

    const getMenuItemsFromPageNumberData = (): JSX.Element[] => {
        if (!numberingStyles) return Array(<React.Fragment />);
        return numberingStyles.map((style, index) => {
            return (
                <MenuItem
                    key={index}
                    value={style.styleKey}
                    dense
                    selected={selectedStyle === style.styleKey}
                    css={css`
                        padding-top: 4px !important;
                        padding-bottom: 4px !important;
                    `}
                >
                    <Typography>{style.localizedStyle}</Typography>
                </MenuItem>
            );
        });
    };

    return (
        <div>
            <Typography
                css={css`
                    font-weight: 700 !important;
                `}
            >
                {pageNumberingLabel}
            </Typography>
            <Select
                css={css`
                    min-width: 180px;
                    background-color: white;
                    border: 1px solid #bbb;
                    padding-left: 7px;
                    div {
                        padding: 4px 0 4px;
                    }
                    &:before {
                        content: none !important;
                    }
                    &:after {
                        content: none !important;
                    }
                `}
                variant="standard"
                value={selectedStyle} // Must equal the 'styleKey' of a PageNumberStyle
                MenuProps={{
                    anchorOrigin: {
                        vertical: "bottom",
                        horizontal: "left",
                    },
                    transformOrigin: {
                        vertical: "top",
                        horizontal: "left",
                    },
                }}
                onChange={(event) => {
                    handleSelectChange(event);
                }}
            >
                {getMenuItemsFromPageNumberData()}
            </Select>
        </div>
    );
};

export default PageNumberStyleControl;
