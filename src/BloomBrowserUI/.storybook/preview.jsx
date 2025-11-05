import React from "react";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { lightTheme } from "../bloomMaterialUITheme";

// Inline StorybookContext to avoid TypeScript import issues
const StorybookContext = React.createContext(false);

const preview = {
    decorators: [
        (storyFn) => (
            <StyledEngineProvider injectFirst>
                <ThemeProvider theme={lightTheme}>
                    <StorybookContext.Provider value={true}>
                        <div id="reactRoot"> {storyFn()}</div>
                    </StorybookContext.Provider>
                </ThemeProvider>
            </StyledEngineProvider>
        ),
    ],
};

export default preview;
