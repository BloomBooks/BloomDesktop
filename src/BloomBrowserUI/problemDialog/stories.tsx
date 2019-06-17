import theme from "../bloomMaterialUITheme";
import * as React from "react";
import { ThemeProvider } from "@material-ui/styles";
import { storiesOf } from "@storybook/react";
import { addDecorator } from "@storybook/react";
import { StorybookContext } from "../.storybook/StoryBookContext";
import { ProblemDialog, ProblemKind } from "./ProblemDialog";

addDecorator(storyFn => (
    <ThemeProvider theme={theme}>
        <StorybookContext.Provider value={true}>
            {storyFn()}
        </StorybookContext.Provider>
    </ThemeProvider>
));

storiesOf("Problem Report", module)
    .add("NonFatalError", () => <ProblemDialog kind={ProblemKind.NonFatal} />)
    .add("FatalError", () => <ProblemDialog kind={ProblemKind.Fatal} />)
    .add("UserProblem", () => <ProblemDialog kind={ProblemKind.User} />);
