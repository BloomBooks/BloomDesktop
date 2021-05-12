import { createMuiTheme, Theme } from "@material-ui/core/styles";
import { ProblemKind } from "./ProblemDialog";

const kBloomBlue = "#1d94a4";
const kNonFatalColor = "#F3AA18";
export const kindParams = {
    User: {
        dialogHeaderColor: kBloomBlue,
        primaryColor: kBloomBlue,
        title: "Report a Problem",
        l10nKey: "ReportProblemDialog.UserTitle"
    },
    Fatal: {
        dialogHeaderColor: "#f44336", // bright red
        primaryColor: "#f44336", // FYI, we originally had #2F58EA (bright blue), but now we decided to have it all one color
        title: "Bloom encountered an error and needs to quit",
        l10nKey: "ReportProblemDialog.FatalTitle"
    },
    NonFatal: {
        dialogHeaderColor: kNonFatalColor,
        primaryColor: kNonFatalColor,
        title: "Bloom had a problem",
        l10nKey: "ReportProblemDialog.NonFatalTitle"
    },
    // Notify uses many of the same settings as NonFatal
    Notify: {
        dialogHeaderColor: kNonFatalColor,
        primaryColor: kNonFatalColor,
        title: "Bloom had a problem",
        l10nKey: "ReportProblemDialog.NonFatalTitle"
    }
};

export function makeTheme(kind: ProblemKind): Theme {
    // (21 Nov. '19) "<any>"" is required because we define fontFamily as type string[], but as of now
    // the Material UI typescript defn. doesn't allow that. It works, though.
    return createMuiTheme(<any>{
        palette: {
            primary: { main: kindParams[kind.toString()].primaryColor },
            error: { main: kindParams["NonFatal"].primaryColor }
        },
        typography: {
            fontSize: 12,
            fontFamily: ["NotoSans", "Roboto", "sans-serif"]
        },
        props: {
            MuiLink: {
                variant: "body1" // without this, they come out in times new roman :-)
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
                    color: "#FFFFFF",
                    backgroundColor:
                        kindParams[kind.toString()].dialogHeaderColor,
                    "& h6": { fontWeight: "bold" }
                }
            },
            MuiDialogActions: {
                root: {
                    backgroundColor: "#FFFFFF",
                    paddingRight: 20,
                    paddingBottom: 20
                }
            },
            MuiButton: {
                // Set the text colors of NotifyDialog's DialogAction buttons
                containedPrimary: {
                    color: "#FFFFFF"
                }
            }
        }
    });
}
