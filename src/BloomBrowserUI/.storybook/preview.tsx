//import { withA11y } from "@storybook/addon-a11y";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { StorybookContext } from "./StoryBookContext";
import * as React from "react";
import { lightTheme } from "../bloomMaterialUITheme";

export default {
    decorators: [
        storyFn => (
            <StyledEngineProvider injectFirst>
                <ThemeProvider theme={lightTheme}>
                    <StorybookContext.Provider value={true}>
                        <div id="reactRoot"> {storyFn()}</div>
                    </StorybookContext.Provider>
                </ThemeProvider>
            </StyledEngineProvider>
        )
        // was needed by publish/stories.tsx, apparently no longer exists in storybook 8, waiting to see what breaks...
        //        withA11y
    ]
};
