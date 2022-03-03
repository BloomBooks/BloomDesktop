/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { useState } from "react";
import { ThemeProvider } from "@material-ui/styles";
import { lightTheme } from "../../bloomMaterialUITheme";
import { FormControl, MenuItem, Popover, TextField } from "@material-ui/core";
import FontDisplayBar from "../../react_components/fontDisplayBar";
import FontInformationPane from "../../react_components/fontInformationPane";

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
    determinedSuitability: "ok" | "unknown" | "unsuitable";
    determinedSuitabilityNotes?: string;
}

interface FontSelectProps {
    fontMetadata: IFontMetaData[];
    currentFontName: string;
    onChangeFont?: (fontname: string) => void;
}

const FontSelectComponent: React.FunctionComponent<FontSelectProps> = props => {
    const getFontDataFromName = (fontName: string) => {
        return props.fontMetadata.find(f => f.name === fontName);
    };
    const [fontChoice, setFontChoice] = useState(
        getFontDataFromName(props.currentFontName)
    );

    const [popoverFont, setPopoverFont] = useState<IFontMetaData | undefined>(
        undefined
    );

    const [popoverAnchorElement, setPopoverAnchorElement] = useState<
        HTMLElement | undefined
    >(undefined);

    const handlePopoverOpen = (
        hoverTarget: HTMLElement,
        metadata: IFontMetaData
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
        return props.fontMetadata.map((font, index) => {
            return (
                <MenuItem
                    key={index}
                    value={font.name}
                    disabled={false} // do not use, because it disables the popover too
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

    // Match the border color of the other selects in the Format dialog.
    const matchingBorderColor = "border-color: #808080;";

    const handleFontChange = event => {
        const fontName: string = event.target.value;
        setFontChoice(getFontDataFromName(event.target.value));
        if (props.onChangeFont) {
            props.onChangeFont(fontName);
        }
    };

    return (
        <ThemeProvider theme={lightTheme}>
            <FormControl
                variant="outlined"
                margin="dense"
                error={fontChoice?.determinedSuitability === "unsuitable"}
                css={css`
                    // Some of the following "!important"s are needed when the Style tab is present,
                    // oddly enough!
                    min-width: 180px !important;
                    margin-right: 12px !important;
                    .MuiOutlinedInput-root {
                        border-radius: 0;
                    }
                    fieldset {
                        ${matchingBorderColor}
                    }
                `}
            >
                <TextField
                    id="font-select"
                    value={fontChoice?.name}
                    select
                    size="small"
                    variant="outlined"
                    onChange={handleFontChange}
                    css={css`
                        #font-select {
                            display: flex;
                            flex: 1;
                            flex-direction: row;
                            justify-content: space-between;
                            padding: 3px 12px 2px 8px; // try to match the font size input
                        }
                    `}
                >
                    {getMenuItemsFromFontMetaData()}
                </TextField>
                <Popover
                    id="mouse-over-popover"
                    open={isPopoverOpen}
                    anchorEl={popoverAnchorElement}
                    // Popver puts its top-left corner in the center of the round suitability icon.
                    anchorOrigin={{
                        vertical: "center",
                        horizontal: "center"
                    }}
                    transformOrigin={{
                        vertical: "top",
                        horizontal: "left"
                    }}
                    disableRestoreFocus
                    onClick={handlePopoverClose}
                >
                    <FontInformationPane metadata={popoverFont} />
                </Popover>
            </FormControl>
        </ThemeProvider>
    );
};

export default FontSelectComponent;
