import { css } from "@emotion/react";
import * as React from "react";
import { useEffect, useState } from "react";
import { ThemeProvider } from "@mui/material/styles";
import { lightTheme } from "../../bloomMaterialUITheme";
import {
    MenuItem,
    Popover,
    PopoverOrigin,
    SelectChangeEvent,
} from "@mui/material";
import FontDisplayBar from "../../react_components/fontDisplayBar";
import FontInformationPane from "../../react_components/fontInformationPane";
import WinFormsStyleSelect from "../../react_components/winFormsStyleSelect";

export interface IFontMetaData {
    name: string;
    version?: string;
    license?: string;
    licenseURL?: string;
    copyright?: string;
    manufacturer?: string;
    manufacturerURL?: string;
    fsType?: string;
    variants?: string[];
    designer?: string;
    designerURL?: string;
    trademark?: string;
    // These values are also listed in FontMetadata in src/BloomExe/FontProcessing/FontMetadata.cs.
    determinedSuitability: "ok" | "unknown" | "unsuitable" | "invalid";
    determinedSuitabilityNotes?: string;
}

interface FontSelectProps {
    fontMetadata?: IFontMetaData[];
    currentFontName: string;
    // As we move toward a single browser for Bloom, the chance that multiple font selects
    // will be in the DOM at a time increases. So we'll just require a number to use as a key.
    languageNumber: number;
    onChangeFont?: (fontname: string) => void;
    // Use this if you need to modify the style of popup menus by increasing z-index
    // (e.g., to make the popup be in front of the bloom font dialog)
    popoverZindex?: string;
}

const FontSelectComponent: React.FunctionComponent<FontSelectProps> = (
    props,
) => {
    const getFontDataFromName = (fontName: string) => {
        if (!props.fontMetadata) return undefined;
        return props.fontMetadata.find((f) => f.name === fontName);
    };

    const [fontChoice, setFontChoice] = useState<IFontMetaData | undefined>(
        undefined,
    );

    // If the font metadata isn't initially available, reload the 'fontChoice' when it "arrives".
    useEffect(() => {
        const fontData = getFontDataFromName(props.currentFontName);
        setFontChoice(fontData);
    }, [props.fontMetadata]);

    // The references to "popover" from here down to 'isPopoverOpen' refer to the font information pane
    // that shows when hovering over the suitability icon near the end of the FontDisplayBar,
    // NOT the select dropdown.
    const [popoverFont, setPopoverFont] = useState<IFontMetaData | undefined>(
        undefined,
    );
    const [popoverAnchorElement, setPopoverAnchorElement] = useState<
        HTMLElement | undefined
    >(undefined);
    const handlePopoverOpen = (
        hoverTarget: HTMLElement,
        metadata: IFontMetaData,
    ) => {
        setPopoverAnchorElement(hoverTarget as HTMLElement);
        setPopoverFont(metadata);
    };
    const handlePopoverClose = () => {
        setPopoverAnchorElement(undefined);
        setPopoverFont(undefined);
    };
    const isPopoverOpen = Boolean(popoverAnchorElement);

    const getMenuItemsFromFontMetaData = (): JSX.Element[] => {
        if (!props.fontMetadata) return Array(<React.Fragment />);
        return props.fontMetadata.map((font, index) => {
            return (
                <MenuItem
                    key={index}
                    value={font.name}
                    dense
                    css={css`
                        padding-top: 0 !important;
                        padding-bottom: 0 !important;
                    `}
                >
                    <FontDisplayBar
                        fontMetadata={font}
                        inDropdownList={font !== fontChoice}
                        isPopoverOpen
                        onHover={handlePopoverOpen}
                    />
                </MenuItem>
            );
        });
    };

    const handleFontChange = (event: SelectChangeEvent) => {
        const fontName: string = event.target.value;
        setFontChoice(getFontDataFromName(event.target.value));
        if (props.onChangeFont) {
            props.onChangeFont(fontName);
        }
    };

    const finalKey = `font-${props.languageNumber.toString()}`;

    const transformOrigin: PopoverOrigin = {
        vertical: "top",
        horizontal: "left",
    };

    const textValue = fontChoice ? fontChoice.name : props.currentFontName;

    return (
        <ThemeProvider theme={lightTheme}>
            <WinFormsStyleSelect
                idKey={finalKey}
                currentValue={textValue}
                onChangeHandler={handleFontChange}
                popoverZindex={props.popoverZindex}
            >
                {getMenuItemsFromFontMetaData()}
            </WinFormsStyleSelect>
            {/* This is the font information popup that gives information about the hovered font. */}
            <Popover
                open={isPopoverOpen}
                anchorEl={popoverAnchorElement}
                // Popver puts its top-left corner in the center of the round suitability icon.
                anchorOrigin={{
                    vertical: "center",
                    horizontal: "center",
                }}
                transformOrigin={transformOrigin}
                disableRestoreFocus
                onClick={handlePopoverClose}
            >
                <FontInformationPane metadata={popoverFont} />
            </Popover>
        </ThemeProvider>
    );
};

export default FontSelectComponent;
