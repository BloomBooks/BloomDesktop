import { css } from "@emotion/react";

import * as React from "react";
import { get } from "../utils/bloomApi";
import { lightTheme } from "../bloomMaterialUITheme";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { IFontMetaData } from "../bookEdit/StyleEditor/fontSelectComponent";
import { useEffect, useState } from "react";
import SingleFontSection from "../react_components/singleFontSection";

// This component is the chooser for the collection's fonts and script settings, on the left side
// of the "Book Making" tab of the Settings dialog. Eventually the whole tab and whole dialog
// should move to HTML, but currently this is the root of a ReactDialog.
export const FontScriptSettingsControl: React.FunctionComponent = () => {
    const [fontMetadata, setFontMetadata] = useState<
        IFontMetaData[] | undefined
    >(undefined);
    const [language1Name, setLanguage1Name] = useState<string | undefined>(
        undefined,
    );
    const [language2Name, setLanguage2Name] = useState<string | undefined>(
        undefined,
    );
    const [language3Name, setLanguage3Name] = useState<string | undefined>(
        undefined,
    );
    const [language1Font, setLanguage1Font] = useState<string | undefined>(
        undefined,
    );
    const [language2Font, setLanguage2Font] = useState<string | undefined>(
        undefined,
    );
    const [language3Font, setLanguage3Font] = useState<string | undefined>(
        undefined,
    );

    useEffect(() => {
        get("settings/currentFontData", (result) => {
            // fontData should be 2 or 3 sets of (language display name and current font name)
            const fontData = result.data;
            // Language 1 data
            setLanguage1Name(fontData[0].languageName);
            setLanguage1Font(fontData[0].fontName);
            // Language 2 data
            setLanguage2Name(fontData[1].languageName);
            setLanguage2Font(fontData[1].fontName);
            // Language 3 data - possibly undefined
            setLanguage3Name(fontData[2]?.languageName);
            setLanguage3Font(fontData[2]?.fontName);
        });
    }, []);

    useEffect(() => {
        get("fonts/metadata", (result) => {
            const fontMetadata: IFontMetaData[] = result.data;
            setFontMetadata(fontMetadata);
        });
    }, []);

    return (
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={lightTheme}>
                <div
                    css={css`
                        font-size: 10pt;
                        display: flex;
                        flex: 1;
                        flex-direction: column;
                        min-height: 275px; // Don't change height of control as 3rd Lang. comes and goes.
                    `}
                >
                    {/* Language 1 section */}
                    {language1Name && language1Font && (
                        <SingleFontSection
                            languageNumber={1}
                            languageName={language1Name}
                            currentFontName={language1Font}
                            fontMetadata={fontMetadata}
                        />
                    )}
                    {/* Language 2 section */}
                    {language2Name && language2Font && (
                        <SingleFontSection
                            languageNumber={2}
                            languageName={language2Name}
                            currentFontName={language2Font}
                            fontMetadata={fontMetadata}
                        />
                    )}
                    {/* Language 3 section */}
                    {language3Name && language3Font && (
                        <SingleFontSection
                            languageNumber={3}
                            languageName={language3Name}
                            currentFontName={language3Font}
                            fontMetadata={fontMetadata}
                        />
                    )}
                </div>
            </ThemeProvider>
        </StyledEngineProvider>
    );
};

export default FontScriptSettingsControl;
