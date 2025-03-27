/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useEffect, useState } from "react";
import { postString } from "../utils/bloomApi";
import { lightTheme, kBloomYellow } from "../bloomMaterialUITheme";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { MenuItem, Select, Typography } from "@mui/material";
import { makeStyles } from "@mui/styles";
import { useGetEnterpriseBookshelves } from "../collection/useGetEnterpriseBookshelves";
import { BloomEnterpriseIconWithTooltip } from "./requiresSubscription";
import { useL10n } from "./l10nHooks";

// This component is the chooser for a default bookshelf, currently in the bottom right corner
// of the "Book Making" tab of the Settings dialog.

export const DefaultBookshelfControl: React.FunctionComponent = () => {
    const [projectBookshelfUrlKey, setProjectBookshelfUrlKey] = useState("");

    const {
        project, // not needed here, but we'll need it elsewhere.
        defaultBookshelfUrlKey,
        validBookshelves,
        error
    } = useGetEnterpriseBookshelves();

    useEffect(() => {
        setProjectBookshelfUrlKey(defaultBookshelfUrlKey);
    }, [defaultBookshelfUrlKey]);

    const items = validBookshelves.map(x => (
        <MenuItem key={x.value} value={x.value} title={x.tooltip}>
            {x.label}
        </MenuItem>
    ));

    const disableControl =
        !validBookshelves ||
        validBookshelves.length === 0 ||
        (validBookshelves.length === 1 && validBookshelves[0].value === "none");

    const BLBookshelfLabel = useL10n(
        "Bloom Library Bookshelf",
        "CollectionSettingsDialog.BloomLibraryBookshelf",
        undefined,
        undefined,
        undefined,
        true // don't localize for now
    );

    const errorCaseDescription = useL10n(
        "Bloom could not reach the server to get the list of bookshelves.",
        "CollectionSettingsDialog.BookMakingTab.NoBookshelvesFromServer",
        undefined,
        undefined,
        undefined,
        true // don't localize for now
    );

    const defaultCaseDescription = useL10n(
        "Projects that have Bloom Enterprise subscriptions can arrange for one or more bookshelves on the Bloom Library. All books uploaded from this collection will go into the selected bookshelf.",
        "CollectionSettingsDialog.BookMakingTab.BookshelfDescription",
        undefined,
        undefined,
        undefined,
        true // don't localize for now
    );

    const commonDescriptionCss =
        "margin-top: 1em;\nfont-size: 0.8rem !important;";

    // Here we have to use the Material-ui style system (or else go back to
    // importing a separate stylesheet) because the elements that make up
    // the pulldown part of the input are not ones we can configure with emotion,
    // since they are added by code and not part of any element we control.
    // All these styles help make the elements look like other standard windows
    // controls on the same page of the settings dialog.
    // the max-height is closer to 100% than materialUI normally allows and helps
    // the menu not have to scroll, even in a fairly small space (it is confined
    // to its own control, not the whole surface of the dialog).
    // The negative left margin compensates for the left:16px that is part of
    // where material-UI places the pull-down, and brings it into line with
    // a Windows combo. Reduced padding makes the menu and items the usual
    // Windows size.
    const useStyles = makeStyles({
        select: {
            maxHeight: "calc(100% - 20px)",
            borderRadius: 1,
            marginLeft: "-16px",
            "& ul": {
                padding: 0
            },
            "& li": {
                padding: "1px !important",
                // This was especially tricky to discover. It makes no difference
                // at larger window widths, but in the relatively narrow space we
                // leave for the parent ReactControl, some MaterialUI javascript
                // code decides to mess with the styles and increase the min-height,
                // making the items way to big for a desktop (and apparently showing
                // a different behavior in Gecko and FF, though actually the difference
                // is window width).
                minHeight: "auto"
            }
        }
    });
    const classes = useStyles(); // part of the magic of MaterialUI styles. Possibly could be inlined.
    return (
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={lightTheme}>
                <div
                    // 10pt seems to be the size this dialog uses, so we push it fairly strongly,
                    // in more than one place, for consistency. The larger fonts that Material-UI
                    // normally uses are probably aimed at making touch-sized targets.
                    css={css`
                        font-size: 10pt;
                    `}
                >
                    <div
                        css={css`
                            display: flex;
                        `}
                    >
                        <Typography
                            css={css`
                                font-weight: 700 !important;
                            `}
                        >
                            {BLBookshelfLabel}
                        </Typography>
                        <BloomEnterpriseIconWithTooltip featureName="bookshelf" />
                    </div>
                    <Select
                        // Using a MaterialUI Select here, though we have to fight it fairly hard
                        // to get an appearance that matches the rest of the dialog. Possibly there
                        // would have been a better choice for now, but it wasn't obvious, and we already
                        // have this package. And we will probably want some normal Material-UI
                        // UI when this whole dialog moves to HTML, so it's a step in the right
                        // direction. The various incantations here are the result of google searches
                        // and experiment to get as close as possible to the Windows appearance.
                        // I think most stuff is quite close, but have not been able to get the
                        // Windows-style arrows; these don't seem to be configurable (except perhaps by
                        // some complex overlay) in an HTML input element.)
                        css={css`
                            min-width: 320px;
                            background-color: white;
                            border: 1px solid #bbb;
                            font-size: 10pt;
                            padding-left: 7px; // match what winforms is doing
                            &:before {
                                content: none !important; // 'important' gets rid of dotted line under Select
                            }
                            &:after {
                                content: none;
                            }
                        `}
                        variant="standard"
                        value={projectBookshelfUrlKey}
                        MenuProps={{
                            classes: { paper: classes.select },
                            anchorOrigin: {
                                vertical: "bottom",
                                horizontal: "left"
                            },
                            transformOrigin: {
                                vertical: "top",
                                horizontal: "left"
                            }
                        }}
                        // If we can't get the options from contentful, or there are none, disable.
                        disabled={disableControl}
                        onChange={event => {
                            const newShelf = event.target.value as string;
                            setProjectBookshelfUrlKey(newShelf);
                            postString("settings/bookShelfData", newShelf);
                        }}
                    >
                        {items}
                    </Select>
                    {error ? (
                        // We display this message if either of the contentful queries fail.
                        <Typography
                            css={css`
                                color: ${kBloomYellow};
                                ${commonDescriptionCss}
                            `}
                        >
                            {errorCaseDescription}
                        </Typography>
                    ) : (
                        // The normal case.
                        <Typography
                            css={css`
                                color: black;
                                ${commonDescriptionCss}
                            `}
                        >
                            {defaultCaseDescription}
                        </Typography>
                    )}
                </div>
            </ThemeProvider>
        </StyledEngineProvider>
    );
};

export default DefaultBookshelfControl;
