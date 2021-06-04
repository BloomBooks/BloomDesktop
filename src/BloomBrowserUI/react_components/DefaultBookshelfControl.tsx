/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { useState } from "react";
import { BloomApi } from "../utils/bloomApi";
import { Div } from "./l10nComponents";
import theme, { kBloomYellow } from "../bloomMaterialUITheme";
import { ThemeProvider } from "@material-ui/styles";
import BloomSelect from "./bloomSelect";
import { makeStyles, MenuItem, Select } from "@material-ui/core";
import XRegExp = require("xregexp/types");
import { useContentful } from "../contentful/UseContentful";

const windowsSelectColor = "rgb(0,120,215)";

// This component is the chooser for a default bookshelf, currently in the bottom right corner
// of the "Book Making" tab of the Settings dialog. Eventually the whole tab and whole dialog
// should move to HTML, but currently this is the root of a ReactDialog.

export const DefaultBookshelfControl: React.FunctionComponent = props => {
    // Things get tricky because we have to run two queries here to get the
    // data we need, and the second depends on the results of the first.
    // Doing this under the rules of hooks is difficult. The first query is
    // to our local server, and obtains the project's branding name and any
    // current default bookshelf. The second uses the project branding info
    // to retrieve the actual contentful Enterprise Subscription
    // complete with references to the collections we want.
    // Besides keeping straight which of these queries has and has not completed,
    // we must handle special cases when we cannot retrieve data from contentful
    // and when we the user has no enterprise subscription and can't use this
    // feature.

    // The defaultShelf retrieved from the settings/bookShelfData API, or 'none'
    // if defaultShelf is falsy. Using 'none' as the value here in this control
    // allows us to show a label 'None' when nothing is selected; passing it as
    // the value when that option is chosen allows us to get around a restricion
    // in our API which does not allow an empty string as the value of a
    // required string value.
    const [defaultBookshelf, setDefaultBookshelf] = useState("");
    // The project or branding retrieved from the settings/bookShelfData API.
    const [project, setProject] = useState("");
    // First query: get the values of the two states above.
    React.useEffect(() => {
        BloomApi.get("settings/bookShelfData", data => {
            const pn = data.data.brandingProjectName;
            setProject(pn === "Default" ? "" : pn);
            setDefaultBookshelf(data.data.defaultBookshelf || "none");
        });
    }, []);

    // Second query to get the contentful data
    const { loading, result, error } = useContentful(
        project
            ? {
                  content_type: "enterpriseSubscription",
                  select: "fields.collections",
                  include: 2, // depth: we want the bookshelf collection objects as part of this query
                  "fields.id": `${project}`
              }
            : undefined // no project means we don't want useContentful to do a query
    );

    let bookshelves = [{ value: "none", label: "None" }]; // todo: should be localizable, eventually?
    if (!project) {
        // If we don't (yet) have a project, we want a completely empty list of options
        // to leave the combo blank.
        bookshelves = [];
    } else if (!loading && result && result.length > 0 && !error) {
        var collections: any[] = result[0].fields.collections;
        // If all is well and we've completed the contentful query, we got an object that
        // has a list of collections connected to this branding, and will
        // now generate the list of menu items (prepending the 'None' we already made).
        bookshelves = bookshelves.concat(
            collections.map<{ value: string; label: string }>((c: any) => ({
                value: c.fields.urlKey,
                label: c.fields.label
            }))
        );
    } else if (defaultBookshelf) {
        // This will usually be overwritten soon, but if we can't get to contentful
        // to get the actual list of possibilities we will leave it here.
        // Note that as we don't yet have any better label, we use defaultShelf.
        bookshelves.push({ value: defaultBookshelf, label: defaultBookshelf });
    }
    const items = bookshelves.map(x => (
        <MenuItem key={x.value} value={x.value}>
            {x.label}
        </MenuItem>
    ));

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
            border: `1px solid ${windowsSelectColor}`,
            borderRadius: 1,
            marginLeft: "-16px",
            "& ul": {
                padding: 0
            },
            "& li": {
                padding: "1px !important",
                // It's annoying we have to set this again, but Material UI has a
                // setting at this level which will override anything inherited
                // if we don't.
                fontFamily: "Segoe UI",
                // This was especially tricky to discover. It makes no difference
                // at larger window widths, but in the relatively narrow space we
                // leave for the parent ReactControl, some MaterialUI javascript
                // code decides to mess with the styles and increase the min-height,
                // making the items way to big for a desktop (and apparently showing
                // a different behavior in Gecko and FF, though actually the difference
                // is window width).
                minHeight: "auto"
            },
            "& li:hover": {
                backgroundColor: `${windowsSelectColor} !important`,
                color: "white"
            }
        }
    });
    const classes = useStyles(); // part of the magic of MaterialUI styles. Possibly could be inlined.
    return (
        <ThemeProvider theme={theme}>
            <div
                // Not sure whether we need to specify a different font for Linux, which probably doesn't
                // have Segoe UI, or whether the default is OK. 10pt seems to be the size this dialog uses,
                // so we push it fairly strongly, in more than one place, for consistency. The larger fonts
                // MaterialUI normally uses are probably aimed at making touch-sized targets.
                css={css`
                    font-family: "Segoe UI";
                    font-size: 10pt;
                `}
            >
                <Div
                    css={css`
                        ${project
                            ? "font-family: 'Segoe UI Semibold'"
                            : "color: grey"}
                    `}
                    l10nKey="CollectionSettingsDialog.BloomLibraryBookshelf"
                    temporarilyDisableI18nWarning={true}
                >
                    Bloom Library Bookshelf
                </Div>
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
                        min-width: 200px;
                        background-color: #e1e1e1;
                        border: 1px solid #bbb;
                        font-size: 10pt;
                        &:before {
                            content: none;
                        }
                        &:after {
                            content: none;
                        }
                        &:hover {
                            background-color: #e5f1fb;
                            border-color: ${windowsSelectColor};
                        }
                        .MuiSelect-root {
                            padding: 3px 3px 3px 2px;
                        }
                    `}
                    value={defaultBookshelf}
                    MenuProps={{
                        classes: { paper: classes.select },
                        anchorOrigin: {
                            vertical: "bottom",
                            horizontal: "left"
                        },
                        transformOrigin: {
                            vertical: "top",
                            horizontal: "left"
                        },
                        getContentAnchorEl: null
                    }}
                    // If we can't get the options from contentful, or there are none, disable.
                    disabled={!result || result.length == 0}
                    onChange={event => {
                        const newShelf = event.target.value as string;
                        setDefaultBookshelf(newShelf);
                        BloomApi.postString("settings/bookShelfData", newShelf);
                    }}
                >
                    {items}
                </Select>
                {error ? (
                    // We display this message if either of the contentful queries fail.
                    <Div
                        css={css`
                            color: ${kBloomYellow};
                        `}
                        l10nKey="CollectionSettingsDialog.NoBookshelvesFromServer"
                        temporarilyDisableI18nWarning={true}
                    >
                        Bloom could not reach server to get the list of
                        bookshelves
                    </Div>
                ) : (
                    // The normal case.
                    <Div
                        css={css`
                            color: grey;
                        `}
                        l10nKey="CollectionSettingsDialog.DefaultBookshelfDescription"
                        temporarilyDisableI18nWarning={true}
                    >
                        Projects that have Bloom Enterprise subscriptions can
                        arrange for one or more bookshelves on the Bloom
                        Library. All books uploaded from this collection will go
                        into the selected bookshelf.
                    </Div>
                )}
            </div>
        </ThemeProvider>
    );
};
