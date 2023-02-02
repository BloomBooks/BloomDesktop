import { createTheme, adaptV4Theme, Theme } from "@mui/material/styles";

export const kBloomBlue = "#1d94a4";
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
export const lightTheme = createTheme(
    adaptV4Theme({
        //this spacing doesn't seem to do anything. The example at https://material-ui.com/customization/default-theme/
        // would be spacing{unit:23} but that gives an error saying to use a number
        //spacing: 23,
        palette: {
            primary: { main: kBloomBlue },
            secondary: { main: kBloomPurple },
            warning: { main: kBloomGold }
        },
        typography: {
            fontSize: 12,
            fontFamily: kUiFontStack
        },
        props: {
            MuiLink: {
                variant: "body1" // without this, they come out in times new roman :-)
            },
            MuiTypography: {
                variantMapping: {
                    h6: "h1"
                }
            }
        },
        overrides: {
            MuiDialogTitle: {
                root: {
                    backgroundColor: kDialogTopBottomGray,
                    "& h6": { fontWeight: "bold" }
                }
            },
            MuiDialogActions: {
                root: {
                    backgroundColor: kDialogTopBottomGray
                }
            },
            MuiTypography: {
                h6: {
                    fontSize: "1rem"
                }
            },
            MuiCheckbox: {
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
    })
);

// Starting with the lightTheme, make any changes.
export const darkTheme = createTheme(
    lightTheme,
    adaptV4Theme({
        palette: {
            text: {
                // the only place I *know* this is currently used is the refresh button in the BloomPub publish preview panel
                secondary: kGreyOnDarkColor
            }
        }
    })
);
