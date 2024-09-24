/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import React = require("react");
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
}> = props => {
    const rows: IStyleAndFont[] = useApiObject<IStyleAndFont[]>(
        "stylesAndFonts/getDataRows",
        []
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
                    {rows.map(row => (
                        <StyleAndFontRow
                            key={row.styleName + row.languageName}
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
    key: string;
    row: IStyleAndFont;
    closeDialogAndJumpToPage: (pageId: string) => void;
}> = ({ key, row, closeDialogAndJumpToPage }) => {
    const fontMetaData: IFontMetaData | undefined = useFontMetaData(
        row.fontName
    );
    return (
        <TableRow key={key}>
            <TableCell component="th" scope="row" sx={{ border: 1 }}>
                {row.styleName}
            </TableCell>
            <TableCell sx={{ border: 1 }}>{row.languageName}</TableCell>
            <TableCell sx={{ border: 1 }}>
                <FontLicenseInfo fontMetaData={fontMetaData} />
            </TableCell>
            <TableCell sx={{ border: 1 }}>
                <Link
                    href={"#" + row.pageId}
                    onClick={() => closeDialogAndJumpToPage(row.pageId)}
                >
                    {row.pageDescription}
                </Link>
            </TableCell>
        </TableRow>
    );
};

// a react hook for getting IFontMetaData for a font
export function useFontMetaData(fontName: string): IFontMetaData | undefined {
    const fontMetaData: IFontMetaData[] = useApiObject<IFontMetaData[]>(
        "fonts/metadata",
        []
    );
    return fontMetaData.find(font => font.name === fontName);
}

// a react functionalcomponent for displaying the license info for a font, based on the suitability
// info in the font metadata.  shows a warning or error icon before a label of the suitability
const FontLicenseInfo: React.FunctionComponent<{
    fontMetaData: IFontMetaData | undefined;
}> = ({ fontMetaData }) => {
    if (!fontMetaData) return null;
    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
            `}
        >
            <div>{fontMetaData.name}</div>
            {fontMetaData.determinedSuitability !== "ok" && (
                <div
                    css={css`
                        display: flex;
                        flex-direction: column;
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
                                color: theme => theme.palette.error.main
                            }}
                        />
                        {`${fontMetaData.determinedSuitability} / ${fontMetaData.determinedSuitabilityNotes}`}
                    </div>
                    <div>{fontMetaData.copyright}</div>
                    <div>{fontMetaData.license}</div>
                    <div>{fontMetaData.manufacturer}</div>
                </div>
            )}
        </div>
    );
};
