import theme from "../bloomMaterialUITheme";
import * as React from "react";
import { ThemeProvider } from "@material-ui/styles";
import { storiesOf } from "@storybook/react";
import { addDecorator } from "@storybook/react";
import { StorybookContext } from "../.storybook/StoryBookContext";
import { ProblemKind } from "./ProblemDialog";
import { NotifyDialog } from "./NotifyDialog";
import { ReportDialog } from "./ReportDialog";

// ENHANCE: Could we make this have the exact same dimensions the browser dialog would have?
addDecorator(storyFn => (
    <ThemeProvider theme={theme}>
        <StorybookContext.Provider value={true}>
            {storyFn()}
        </StorybookContext.Provider>
    </ThemeProvider>
));

storiesOf("Problem Report", module)
    .add("FatalError", () => <ReportDialog kind={ProblemKind.Fatal} />)
    .add("NonFatalError", () => <ReportDialog kind={ProblemKind.NonFatal} />)
    .add("UserProblem", () => <ReportDialog kind={ProblemKind.User} />)
    .add("NotifyUser, Non-Reportable", () => (
        <NotifyDialog
            reportLabel={null}
            secondaryLabel={null}
            messageParam="Fake error"
        />
    ))
    .add("NotifyUser, Reportable", () => (
        <NotifyDialog
            reportLabel="Report"
            secondaryLabel={null}
            messageParam="Fake error"
        />
    ))
    .add("NotifyUser, Report & Retry", () => (
        <NotifyDialog
            reportLabel="Report"
            secondaryLabel="Retry"
            messageParam="Fake error"
        />
    ));
