import { createTheme, Theme } from "@mui/material/styles";
import { kBloomDisabledOpacity, kBloomDisabledText } from "./utils/colorUtils";

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
export const kUiFontStack = "NotoSans, Roboto, sans-serif";

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
