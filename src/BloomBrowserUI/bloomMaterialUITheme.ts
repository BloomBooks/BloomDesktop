import { createTheme, Theme } from "@mui/material/styles";
import "./bloomWebFonts.less";
import {
    kBloomDisabledOpacity,
    kBloomDisabledText,
    kBloomToolboxWhite
} from "./utils/colorUtils";

export const kBloomBlue = "#1d94a4";
export const kBloomBlueTextBackground = "#19818f"; // darker for better contrast
export const kBloomBlue50Transparent = "#8ecad280";
export const kBloomPurple = "#96668f";
const kDialogTopBottomGray = "#F1F3F4";
const kGreyOnDarkColor = "#988b8b";
export const kBloomGold = "#f3aa18";
export const kErrorColor = "red";
export const kDialogPadding = "10px";
export const kLogBackgroundColor = "#fcfcfc";
export const kBannerGray = "#ebebeb";
export const kMainPanelBackgroundColor = "white";
export const kOptionPanelBackgroundColor = kDialogTopBottomGray;
export const kBloomYellow = "#FEBF00";
export const kPanelBackground = "#2e2e2e";
export const kDarkestBackground = "#1a1a1a";
export const kDisabledControlGray = "#bbb";
export const kMutedTextGray = "gray";
export const kVerticalSpacingBetweenDialogSections = "20px";
export const kBorderRadiusForSpecialBlocks = "3px";
export const kBloomBuff = "#d2d2d2";
export const kWarningColor = "#d65649";
// css we want to apply to each MuiSelect to get the look we like.
export const kSelectCss = `
    background-color: white;
    width: 100%;
    &.MuiOutlinedInput-root {
        border-radius: 0 !important;

        .MuiOutlinedInput-notchedOutline {
            border-width: 1px !important;
            border-color: ${kBloomBlue} !important; // it usually is anyway, but not before MUI decides to focus it.
        }
    }
    .MuiSelect-select {padding: 7px 11px;}`;

// Should match @UIFontStack in bloomWebFonts.less
export const kUiFontStack = "Roboto, NotoSans, sans-serif";

export const kDefaultLanguageFontStack = "Andika, sans-serif";

// the value that gets us to the 4.5 AA ratio depends on the background.
// So the "aside"/features right-panel color effects this.
//const AACompliantBloomBlue = "#177c8a";

declare module "@mui/styles" {
    // eslint-disable-next-line @typescript-eslint/no-empty-interface
    interface DefaultTheme extends Theme {}
}

// lots of examples: https://github.com/search?q=createMuiTheme&type=Code
export const lightTheme = createTheme({
    //this spacing doesn't seem to do anything. The example at https://material-ui.com/customization/default-theme/
    // would be spacing{unit:23} but that gives an error saying to use a number
    //spacing: 23,
    palette: {
        primary: { main: kBloomBlue },
        secondary: { main: kBloomPurple },
        warning: { main: kBloomGold },
        text: { disabled: kBloomDisabledText },
        action: {
            disabled: kBloomDisabledText,
            disabledOpacity: kBloomDisabledOpacity
        }
    },
    typography: {
        fontSize: 12,
        fontFamily: kUiFontStack,
        h6: {
            fontSize: "1rem"
        }
    },
    components: {
        MuiLink: {
            variants: [
                {
                    props: { variant: "body1" },
                    style: {
                        variantMapping: {
                            h6: "h1"
                        }
                    }
                }
            ]
        },

        MuiTooltip: {
            styleOverrides: {
                tooltip: {
                    backgroundColor: kBloomBlueTextBackground,
                    fontSize: "12px",
                    fontWeight: "normal",
                    padding: "10px",
                    a: {
                        color: "white",
                        textDecorationColor: "white"
                    }
                },
                arrow: {
                    color: kBloomBlueTextBackground
                }
            }
        },
        MuiDialogTitle: {
            styleOverrides: {
                root: {
                    backgroundColor: kDialogTopBottomGray,
                    "& h6": { fontWeight: "bold" }
                }
            }
        },
        MuiDialogActions: {
            styleOverrides: {
                root: {
                    backgroundColor: kDialogTopBottomGray
                }
            }
        },
        MuiCheckbox: {
            styleOverrides: {
                root: {
                    // for some reason,  in Material-UI 4.0 without this, we instead get unchecked boxes having the color of secondary text!!!!
                    color: kBloomBlue,
                    // In Material-UI 4.0, these just FLAT OUT DON'T WORK, despite the documentation, which I read to say that, if we didn't
                    // specify a `color` above, would then let us specify the color you get for primary and secondary. See https://github.com/mui-org/material-ui/issues/13895
                    colorPrimary: "green", //kBloomBlue,
                    colorSecondary: "pink" //kBloomPurple
                }
            }
        }
    }
});

// Starting with the lightTheme, make any changes.
export const darkTheme = createTheme(lightTheme, {
    palette: {
        text: {
            // the only place I *know* this is currently used is the refresh button in the BloomPub publish preview panel
            secondary: kGreyOnDarkColor
        }
    }
});

const toolboxTextColor = "#d2d2d2";
const kToolboxDisabledOpacity = 0.5;

export const toolboxTheme = createTheme({
    palette: {
        primary: { main: kBloomBlue },
        secondary: { main: kBloomPurple },
        warning: { main: kBloomGold },
        text: { primary: toolboxTextColor, disabled: kBloomDisabledText },
        action: {
            //disabled: kBloomDisabledText,
            //disabledOpacity: kToolboxDisabledOpacity
        }
    },
    typography: {
        fontSize: 11, // text is smaller in the toolbox
        fontFamily: kUiFontStack
    },
    components: {
        MuiLink: {
            variants: [
                {
                    props: { variant: "body1" },
                    style: {
                        variantMapping: {
                            h6: "h1"
                        }
                    }
                }
            ]
        },

        MuiTooltip: {
            styleOverrides: {
                tooltip: {
                    placement: "bottom",
                    backgroundColor: kBloomBlueTextBackground,
                    fontSize: "12px",
                    fontWeight: "normal",

                    // This forces all tooltips to be this width, even short ones like "Delete".
                    // I tried changing it to maxWidth, which improves the size of short ones,
                    // but then the arrows come out in the wrong place and/or the tooltips extend
                    // outside the toolbox and cause it to scroll horizontally. It's less bad to leave
                    // it like this.  I have no idea why the placement of the tooltip itself and
                    // its arrow is so much worse when the width is not fixed.
                    width: "165px", // width of the toolbox (which is 185px) minus a bit of padding
                    a: {
                        color: "white",
                        textDecorationColor: "white"
                    }
                },
                popper: {
                    zIndex: 200000
                },
                arrow: {
                    color: kBloomBlueTextBackground
                }
            }
        },

        MuiFormGroup: {
            styleOverrides: {
                root: {
                    // getting the spacing I want from radio groups
                    "&[role=radiogroup]": { gap: "5px" }
                }
            }
        },

        MuiFormControlLabel: {
            styleOverrides: {
                label: {
                    fontSize: "10px",
                    "&.Mui-disabled": {
                        opacity: kToolboxDisabledOpacity,
                        color: kBloomToolboxWhite
                    }
                },
                root: {
                    alignItems: "flex-start"
                }
            }
        },

        MuiRadio: {
            styleOverrides: {
                root: {
                    // make radio buttons closer together
                    paddingBottom: "0px",
                    paddingTop: "0px",
                    color: kBloomToolboxWhite,
                    "&.Mui-disabled": {
                        borderColor: kBloomToolboxWhite,
                        color: kBloomToolboxWhite,
                        opacity: kToolboxDisabledOpacity
                    }
                }
            }
        },

        MuiTypography: {
            styleOverrides: {
                h2: {
                    fontSize: "11px",

                    marginBottom: "5px"
                }
            }
        },

        MuiSwitch: {
            styleOverrides: {
                root: {
                    // for some reason, without this tweak,
                    // the switch sticks out to the left over the left of its container
                    marginLeft: "6px"
                },
                track: {
                    backgroundColor: kBloomBlue
                },
                thumb: {
                    backgroundColor: kBloomToolboxWhite
                }
            }
        },

        MuiButton: {
            styleOverrides: {
                root: {
                    // fill the width of the slim toolbox, this makes multiple
                    // buttons in the Talking book tool "Advanced" section look good next to each other
                    width: "100%",
                    textTransform: "none", // Material buttons are all caps by default
                    color: toolboxTextColor,
                    "&, &:hover": {
                        borderWidth: "2px"
                    },
                    // set the color of the icon in the button to red
                    "& .MuiButton-startIcon": {
                        color: kBloomBlue
                    }
                },
                outlined: {
                    justifyContent: "flex-start",
                    "&.Mui-disabled": {
                        borderColor: kBloomToolboxWhite,
                        color: kBloomToolboxWhite,
                        opacity: kToolboxDisabledOpacity,

                        ".MuiButton-startIcon": {
                            color: kBloomToolboxWhite
                        }
                    }
                }
            }
        },
        // Because of our dark background in the toolbox, disabled items need to be lighter
        MuiDivider: {
            styleOverrides: {
                root: {
                    backgroundColor: "#d4d4d480",
                    marginTop: "5px",
                    marginBottom: "5px"
                }
            }
        }
    }
});
