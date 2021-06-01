/* --------------- NOTE --------------------
    This is current js instead of ts because as of 4.0.0-beta,
    the typescript definitions for material-ui
    don't allow some perfectly valid ThemeOptions. I guess I could "any" the object instead...
*/

import { createMuiTheme } from "@material-ui/core/styles";

export const kBloomBlue = "#1d94a4";
export const kBloomPurple = "#96668f";
const kDialogTopBottomGray = "#F1F3F4";
const kRefreshIconColor = "#988b8b";
export const kBloomGold = "#f3aa18";
export const kErrorColor = "red";
export const kDialogPadding = "10px";
export const kLogBackgroundColor = "#fcfcfc";
export const kBloomYellow = "#FEBF00";

// the value that gets us to the 4.5 AA ratio depends on the background.
// So the "aside"/features right-panel color effects this.
//const AACompliantBloomBlue = "#177c8a";

// lots of examples: https://github.com/search?q=createMuiTheme&type=Code
const theme = createMuiTheme({
    //this spacing doesn't seem to do anything. The example at https://material-ui.com/customization/default-theme/
    // would be spacing{unit:23} but that gives an error saying to use a number
    //spacing: 23,
    palette: {
        primary: { main: kBloomBlue },
        secondary: { main: kBloomPurple },
        warning: { main: kBloomGold },
        text: { secondary: kRefreshIconColor }
    },
    typography: {
        fontSize: 12,
        fontFamily: ["NotoSans", "Roboto", "sans-serif"]
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
        MuiOutlinedInput: {
            input: {
                padding: "7px"
            }
        },
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
        }
    }
});

export default theme;
