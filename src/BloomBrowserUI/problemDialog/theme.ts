import { createTheme, Theme, adaptV4Theme } from "@mui/material/styles";
import { kBloomBlue } from "../utils/colorUtils";
import { ProblemKind } from "./ProblemDialog";
import { kUiFontStack } from "../bloomMaterialUITheme";

const kNonFatalColor = "#F3AA18";
export const kindParams = {
    user: {
        dialogHeaderColor: kBloomBlue,
        primaryColor: kBloomBlue,
        title: "Report a Problem",
        l10nKey: "ReportProblemDialog.UserTitle",
    },
    fatal: {
        dialogHeaderColor: "#f44336", // bright red
        primaryColor: "#f44336", // FYI, we originally had #2F58EA (bright blue), but now we decided to have it all one color
        title: "Bloom encountered an error and needs to quit",
        l10nKey: "ReportProblemDialog.FatalTitle",
    },
    nonfatal: {
        dialogHeaderColor: kNonFatalColor,
        primaryColor: kNonFatalColor,
        title: "Bloom had a problem",
        l10nKey: "ReportProblemDialog.NonFatalTitle",
    },
    // Notify uses many of the same settings as NonFatal
    notify: {
        dialogHeaderColor: kNonFatalColor,
        primaryColor: kNonFatalColor,
        title: "Bloom had a problem",
        l10nKey: "ReportProblemDialog.NonFatalTitle",
    },
};

export function makeTheme(kind: ProblemKind): Theme {
    return createTheme(
        adaptV4Theme({
            palette: {
                primary: { main: kindParams[kind.toString()].primaryColor },
                error: { main: kindParams["nonfatal"].primaryColor },
            },
            typography: {
                fontSize: 12,
                fontFamily: kUiFontStack,
            },
            props: {
                MuiLink: {
                    variant: "body1", // without this, they come out in times new roman :-)
                },
            },
            overrides: {
                MuiOutlinedInput: {
                    input: {
                        padding: "7px",
                    },
                },
                MuiDialogTitle: {
                    root: {
                        color: "#FFFFFF",
                        backgroundColor:
                            kindParams[kind.toString()].dialogHeaderColor,
                        "& h6": { fontWeight: "bold" },
                    },
                },
                MuiDialogActions: {
                    root: {
                        backgroundColor: "#FFFFFF",
                        paddingRight: 20,
                        paddingBottom: 20,
                    },
                },
                MuiButton: {
                    // Set the text colors of NotifyDialog's DialogAction buttons
                    containedPrimary: {
                        color: "#FFFFFF",
                    },
                },
            },
        }),
    );
}
