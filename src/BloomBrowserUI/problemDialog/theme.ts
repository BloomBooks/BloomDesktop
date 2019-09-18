import { createMuiTheme, TextField, Button, Theme } from "@material-ui/core";
import { ProblemKind } from "./ProblemDialog";

const kBloomBlue = "#1d94a4";
const kNonFatalColor = "#F3AA18";
export const kindParams = {
    User: {
        dialogHeaderColor: kBloomBlue,
        primaryColor: kBloomBlue,
        title: "Report a Problem"
    },
    Fatal: {
        dialogHeaderColor: "#f44336",
        primaryColor: "#2F58EA",
        title: "Bloom encountered an error and needs to quit"
    },
    NonFatal: {
        dialogHeaderColor: kNonFatalColor,
        primaryColor: kNonFatalColor,
        title: "Bloom had problem"
    }
};

export function makeTheme(kind: ProblemKind): Theme {
    return createMuiTheme({
        palette: {
            primary: { main: kindParams[kind.toString()].primaryColor }
        },
        typography: {
            fontSize: 12
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
            }
        }
    });
}
