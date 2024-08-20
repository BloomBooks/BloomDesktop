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
        <Table>
            <TableHead>
                <TableRow>
                    <TableCell sx={{ border: 1 }}>
                        <Span l10nKey="EditTab.FormatDialog.Style">Style</Span>
                    </TableCell>
                    <TableCell sx={{ border: 1 }}>
                        <Span l10nKey="EditTab.TextBoxProperties.LanguageTab">
                            Language
                        </Span>
                    </TableCell>
                    <TableCell sx={{ border: 1 }}>
                        <Span l10nKey="EditTab.FormatDialog.Font">Font</Span>
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
                    <TableRow key={row.style + row.languageName}>
                        <TableCell
                            component="th"
                            scope="row"
                            sx={{ border: 1 }}
                        >
                            {row.styleName}
                        </TableCell>
                        <TableCell sx={{ border: 1 }}>
                            {row.languageName}
                        </TableCell>
                        <TableCell sx={{ border: 1 }}>{row.fontName}</TableCell>
                        <TableCell sx={{ border: 1 }}>
                            <Link
                                href={"#" + row.pageId}
                                onClick={() =>
                                    closeDialogAndJumpToPage(row.pageId)
                                }
                            >
                                {row.pageDescription}
                            </Link>
                        </TableCell>
                    </TableRow>
                ))}
            </TableBody>
        </Table>
    );
};
