/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { useEffect, useState } from "react";
import { makeStyles, ThemeProvider } from "@material-ui/styles";
import { lightTheme } from "../../bloomMaterialUITheme";
import {
    FormControl,
    MenuItem,
    Popover,
    PopoverOrigin,
    TextField
} from "@material-ui/core";
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
    fontMetadata?: IFontMetaData[];
    currentFontName: string;
    anchorPopoverLeft?: boolean;
    key?: number; // only needed if there are multiple font selects in a location (like CollectionSettings)
    onChangeFont?: (fontname: string) => void;
}

// This seems to be the only way to affect the css of the popped up list, since it's a completely
// separate html element from this component.
const useStyles = makeStyles(() => ({
    menuPaper: {
        maxHeight: 225,
        padding: 3
    }
}));

const FontSelectComponent: React.FunctionComponent<FontSelectProps> = props => {
    const classes = useStyles();

    const selectMenuProps = {
        classes: { paper: classes.menuPaper }
    };

    const getFontDataFromName = (fontName: string) => {
        if (!props.fontMetadata) return undefined;
        return props.fontMetadata!.find(f => f.name === fontName);
    };

    const [fontChoice, setFontChoice] = useState<IFontMetaData | undefined>(
        undefined
    );

    // If the font metadata isn't initially available, reload the 'fontChoice' when it "arrives".
    useEffect(() => {
        const fontData = getFontDataFromName(props.currentFontName);
        setFontChoice(fontData);
    }, [props.fontMetadata]);

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
        if (!props.fontMetadata) return Array(<React.Fragment />);
        return props.fontMetadata.map((font, index) => {
            return (
                <MenuItem
                    key={index}
                    value={font.name}
                    dense
                    disabled={false} // do not use, because it disables the popover too
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

    // Match the border color of the other selects in the Format dialog.
    const matchingBorderColor = "border-color: #808080 !important;";

    const handleFontChange = event => {
        const fontName: string = event.target.value;
        setFontChoice(getFontDataFromName(event.target.value));
        if (props.onChangeFont) {
            props.onChangeFont(fontName);
        }
    };

    const finalKey = props.key ? props.key.toString() : "";
    // In some cases, we may want the popover to locate itself over the main select
    // instead of off to the right.
    const transformOrigin: PopoverOrigin = props.anchorPopoverLeft
        ? {
              vertical: "top",
              horizontal: "right"
          }
        : {
              vertical: "top",
              horizontal: "left"
          };

    const textValue = fontChoice ? fontChoice.name : props.currentFontName;

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
                    max-width: 220px !important;
                    margin-right: 12px !important;
                    margin-top: 3px !important;
                    div {
                        border-radius: 0;
                    }
                    fieldset {
                        ${matchingBorderColor}
                    }
                    .font-display-bar svg {
                        padding-right: 15px !important; // make room for dropdown arrow
                    }
                `}
            >
                <TextField
                    id={`font-select${finalKey}`}
                    value={textValue}
                    select
                    size="small"
                    variant="outlined"
                    onChange={handleFontChange}
                    SelectProps={{
                        MenuProps: selectMenuProps
                    }}
                    css={css`
                        #font-select${finalKey} {
                            display: flex;
                            flex: 1;
                            flex-direction: row;
                            justify-content: space-between;
                            background-color: #fdfdfd;
                            // try to match the font size input
                            padding: 0 12px 0 8px !important;
                            margin: 1px 0 !important;
                        }
                    `}
                >
                    {getMenuItemsFromFontMetaData()}
                </TextField>
                <Popover
                    open={isPopoverOpen}
                    anchorEl={popoverAnchorElement}
                    // Popver puts its top-left corner in the center of the round suitability icon.
                    anchorOrigin={{
                        vertical: "center",
                        horizontal: "center"
                    }}
                    transformOrigin={transformOrigin}
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
