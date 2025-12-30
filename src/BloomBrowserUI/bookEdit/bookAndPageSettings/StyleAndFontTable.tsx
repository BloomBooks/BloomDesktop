import { css } from "@emotion/react";

import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import { Span } from "../../react_components/l10nComponents";
import { postString, useApiObject } from "../../utils/bloomApi";
import Link from "@mui/material/Link";
import { lightTheme } from "../../bloomMaterialUITheme";
import { ThemeProvider } from "@mui/material/styles";
import { default as Warning } from "@mui/icons-material/Warning";
import { IFontMetaData } from "../StyleEditor/fontSelectComponent";
import { useL10n } from "../../react_components/l10nHooks";
import { useMemo } from "react";

// This interface must be kept in sync with the StyleAndFont class in BookSettingsApi.cs.
export interface IStyleAndFont {
    style: string;
    styleName: string;
    languageName: string;
    languageTag: string;
    fontName: string;
    pageId: string;
    pageDescription: string;
}
export const StyleAndFontTable: React.FunctionComponent<{
    closeDialog: () => void;
}> = (props) => {
    const rowsSource: IStyleAndFont[] = useApiObject<IStyleAndFont[]>(
        "stylesAndFonts/getDataRows",
        [],
    );
    // getDataRows tries to be very comprehensive about fonts used by anything in the document.
    // However, we don't need to tell the user here about fonts that are menioned in style
    // sheets but NOT otherwise used in the document, or even a default font set for a language
    // that doesn't yet have any content in the book. In fact, the heading for this table claims
    // that we tell the user where the font is used in the book. So if data using the font wasn't
    // actually found on some page, we can't do that, so we will leave it out.
    const rows = useMemo(
        () => rowsSource.filter((row) => row.pageId),
        [rowsSource],
    );

    function closeDialogAndJumpToPage(pageId: string) {
        props.closeDialog();
        postString("editView/jumpToPage", pageId);
    }

    return (
        <ThemeProvider theme={lightTheme}>
            <Table>
                <TableHead>
                    <TableRow>
                        <TableCell sx={{ border: 1 }}>
                            <Span l10nKey="EditTab.FormatDialog.Style">
                                Style
                            </Span>
                        </TableCell>
                        <TableCell sx={{ border: 1 }}>
                            <Span l10nKey="EditTab.TextBoxProperties.LanguageTab">
                                Language
                            </Span>
                        </TableCell>
                        <TableCell sx={{ border: 1 }}>
                            <Span l10nKey="EditTab.FormatDialog.Font">
                                Font
                            </Span>
                        </TableCell>
                        <TableCell sx={{ border: 1 }}>
                            <Span l10nKey="BookSettings.Fonts.FirstPage">
                                First Page
                            </Span>
                        </TableCell>
                    </TableRow>
                </TableHead>
                <TableBody>
                    {rows.map((row) => (
                        <StyleAndFontRow
                            // lint wants it to have a key. But that won't be passed to the component,
                            // which wants to use it as the key for the row. So we pass it as a separate prop also.
                            // I'm not actually clear whether we need to set a key on the TR as well as on
                            // the component, or if not, which one is actually needed. But it doesn't
                            // cost much to do both.
                            key={row.styleName + row.languageName}
                            keyArg={row.styleName + row.languageName}
                            row={row}
                            closeDialogAndJumpToPage={closeDialogAndJumpToPage}
                        />
                    ))}
                </TableBody>
            </Table>
        </ThemeProvider>
    );
};

const StyleAndFontRow: React.FunctionComponent<{
    keyArg: string;
    row: IStyleAndFont;
    closeDialogAndJumpToPage: (pageId: string) => void;
}> = (props) => {
    const fontNotInstalledMessage = useL10n(
        "Font is not installed on this computer",
        "BookSettings.Fonts.FontNotInstalled",
    );
    const fontMetaData: IFontMetaData | undefined = useFontMetaData(
        props.row.fontName,
    );
    return (
        <TableRow key={props.keyArg}>
            <TableCell component="th" scope="row" sx={{ border: 1 }}>
                {props.row.styleName}
            </TableCell>
            <TableCell sx={{ border: 1 }}>{props.row.languageName}</TableCell>
            <TableCell sx={{ border: 1 }}>
                {fontMetaData ? (
                    <FontInfoFromMetadata fontMetaData={fontMetaData} />
                ) : (
                    <FontInfo
                        name={props.row.fontName}
                        warningMessage={fontNotInstalledMessage}
                    />
                )}
            </TableCell>
            <TableCell sx={{ border: 1 }}>
                <Link
                    href={"#" + props.row.pageId}
                    onClick={() =>
                        props.closeDialogAndJumpToPage(props.row.pageId)
                    }
                >
                    {props.row.pageDescription}
                </Link>
            </TableCell>
        </TableRow>
    );
};

// a react hook for getting IFontMetaData for a font
export function useFontMetaData(fontName: string): IFontMetaData | undefined {
    const fontMetaData: IFontMetaData[] = useApiObject<IFontMetaData[]>(
        "fonts/metadata",
        [],
    );
    return fontMetaData.find((font) => font.name === fontName);
}

// Displays font info based on font metadata.
// Includes warnings and additional copyright/license info if needed.
const FontInfoFromMetadata: React.FunctionComponent<{
    fontMetaData: IFontMetaData | undefined;
}> = ({ fontMetaData }) => {
    if (!fontMetaData) return null;

    let warningMessage: string | undefined = undefined;
    let additionalInfo: React.ReactElement | undefined = undefined;
    if (fontMetaData.determinedSuitability !== "ok") {
        warningMessage = `${fontMetaData.determinedSuitability} / ${fontMetaData.determinedSuitabilityNotes}`;
        additionalInfo = (
            <div
                css={css`
                    padding-top: 5px;
                `}
            >
                <div>{fontMetaData.copyright}</div>
                <div>{fontMetaData.license}</div>
                <div>{fontMetaData.manufacturer}</div>
            </div>
        );
    }

    return (
        <FontInfo
            name={fontMetaData.name}
            warningMessage={warningMessage}
            additionalInfo={additionalInfo}
        />
    );
};

// Displays the font name with an optional warning message and additional info.
// For example, can be used to display problems with suitability of a font for publishing.
const FontInfo: React.FunctionComponent<{
    name: string;
    warningMessage?: string;
    additionalInfo?: React.ReactElement;
}> = (props) => {
    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
            `}
        >
            <div
            // Experiment with showing the font name in the font itself. I'm not sure I like it, though.
            // css={css`
            //     font-family: ${props.name}, ${kUiFontStack};
            // `}
            >
                {props.name}
            </div>
            {props.warningMessage && (
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
                        padding-top: 3px;
                    `}
                >
                    <div
                        css={css`
                            display: flex;
                            flex-direction: row;
                            align-items: center;
                        `}
                    >
                        <Warning
                            sx={{
                                color: (theme) => theme.palette.error.main,
                                fontSize: "1.1rem", // Default was too big
                                paddingRight: "3px",
                            }}
                        />
                        {props.warningMessage}
                    </div>
                    {props.additionalInfo}
                </div>
            )}
        </div>
    );
};
