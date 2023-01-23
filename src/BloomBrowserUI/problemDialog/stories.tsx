import { lightTheme } from "../bloomMaterialUITheme";
import * as React from "react";
import { ThemeProvider } from "@mui/styles";
import { storiesOf } from "@storybook/react";
import { addDecorator } from "@storybook/react";
import { StorybookContext } from "../.storybook/StoryBookContext";
import { ProblemDialog, ProblemKind } from "./ProblemDialog";
import { NotifyDialog } from "./NotifyDialog";
import { ReportDialog } from "./ReportDialog";

// ENHANCE: Could we make this have the exact same dimensions the browser dialog would have?
addDecorator(storyFn => (
    <ThemeProvider theme={lightTheme}>
        <StorybookContext.Provider value={true}>
            {storyFn()}
        </StorybookContext.Provider>
    </ThemeProvider>
));

const message =
    "Fake error with a line break<br> and <b>bold</b> and <a href='https://google.com'>link</a>...";

storiesOf("Problem Report", module)
    .add("FatalError", () => <ReportDialog kind={ProblemKind.Fatal} />)
    .add("NonFatalError", () => <ReportDialog kind={ProblemKind.NonFatal} />)
    .add("UserProblem", () => <ReportDialog kind={ProblemKind.User} />)
    .add("NotifyUser, Non-Reportable", () => (
        <NotifyDialog
            reportLabel={null}
            secondaryLabel={null}
            message={message}
        />
    ))
    .add("NotifyUser, Reportable", () => (
        <NotifyDialog
            reportLabel="Report"
            secondaryLabel={null}
            message={message}
        />
    ))
    .add("NotifyUser, Report & Retry", () => (
        <NotifyDialog
            reportLabel="Report"
            secondaryLabel="Retry"
            message={message}
        />
    ));
storiesOf("ReportDialog", module)
    .add("ProblemDialog ProblemKind.Fatal", () => (
        <ProblemDialog level={ProblemKind.Fatal} message={message} />
    ))
    /* commented out because modern typescript can't handle this

    .add('ProblemDialog "fatal"', () => (
        // We want to prove that the string "notify" works even though the type is ProblemKind.
        // That's because this prop actually comes from C# which is only able to send strings.
        <ProblemDialog level={"fatal"} message={message} />
    ))
    .add("ProblemDialog notify", () => (
        <ProblemDialog
            // We want to prove that the string "notify" works even though the type is ProblemKind.
            // That's because this prop actually comes from C# which is only able to send strings.
            level={"notify"}
            message={message}
        />
    ))
    */
    .add("ProblemDialog notify with Report", () => (
        <ProblemDialog
            level={ProblemKind.Notify}
            message={message}
            reportLabel="Report"
        />
    ))
    .add("ProblemDialog notify with secondary button", () => (
        <ProblemDialog
            level={ProblemKind.Notify}
            message={message}
            secondaryLabel="Secondary"
        />
    ))
    .add("ProblemDialog notify with both", () => (
        <ProblemDialog
            level={ProblemKind.Notify}
            message={message}
            reportLabel="Report"
            secondaryLabel="Secondary"
        />
    ));
