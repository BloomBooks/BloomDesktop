import { useState, useEffect } from "react";
import { get } from "../utils/bloomApi";
import { useContentful } from "../contentful/UseContentful";

export interface IBookshelf {
    value: string;
    label: string;
    tooltip: string;
}

export function useGetEnterpriseBookshelves(): {
    project: string;
    defaultBookshelfUrlKey: string;
    validBookshelves: IBookshelf[];
    error: boolean;
} {
    // Things get tricky because we have to run two queries here to get the
    // data we need, and the second depends on the results of the first.
    // Doing this under the rules of hooks is difficult. The first query is
    // to our local server, and obtains the project's branding name and any
    // current default bookshelf. The second uses the project branding info
    // to retrieve the actual contentful Enterprise Subscription
    // complete with references to the collections we want.
    // Besides keeping straight which of these queries has and has not completed,
    // we must handle special cases when we cannot retrieve data from contentful
    // and when the user has no enterprise subscription and can't use this
    // feature.

    // The defaultShelf retrieved from the settings/bookShelfData API, or 'none'
    // if defaultShelf is falsy. Using 'none' as the value here in this control
    // allows us to show a label 'None' when nothing is selected; passing it as
    // the value when that option is chosen allows us to get around a restricion
    // in our API which does not allow an empty string as the value of a
    // required string value.
    const [defaultBookshelfUrlKey, setDefaultBookshelfUrlKey] = useState("");
    // The project or branding retrieved from the settings/bookShelfData API.
    const [project, setProject] = useState("");
    // First query: get the values of the two states above.
    useEffect(() => {
        get("settings/bookShelfData", data => {
            const pn = data.data.brandingProjectName;
            setProject(pn === "Default" ? "" : pn);
            setDefaultBookshelfUrlKey(
                data.data.defaultBookshelfUrlKey || "none"
            );
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

    let bookshelves: IBookshelf[] = [
        { value: "none", label: "None", tooltip: "" }
    ];
    if (!project) {
        // If we don't (yet) have a project, we want a completely empty list of options
        // to leave the combo blank.
        bookshelves = [];
    } else if (!loading && result && result.length > 0 && !error) {
        if (result[0].fields && result[0].fields.collections) {
            // The test above is needed because, apparently, if the enterpriseSubscription has no collections,
            // we don't get fields.collections as an empty array; we get nothing at all for collections,
            // and since that's the only field of the ES that we asked for, the result has 'fields' undefined.
            // So trying to get result[0].fields.collections will crash.
            const collections: any[] = result[0].fields.collections;
            // If all is well and we've completed the contentful query, we got an object that
            // has a list of collections connected to this branding, and will
            // now generate the list of menu items (prepending the 'None' we already made).
            bookshelves = bookshelves.concat(
                collections.map<{
                    value: string;
                    label: string;
                    tooltip: string;
                }>((c: any) => ({
                    value: c.fields.urlKey,
                    // Note: the "label" here is the "url" key in Contentful. We
                    //use to use the "label" field for label, but the label may
                    //be optimized for how it will display, in context, on
                    //Blorg. E.g. "books with a,b,c,d and e". But the person
                    //choosing a bookshelf in Bloom Editor may need to also
                    //select based on language, grade, etc. Example ABC
                    //Philippines project. These bits of info are to be found in
                    //the url key, so we switched to using it. If people
                    //struggle with this, we could introduce a 3rd field just
                    //for this choosing, like "selector label".
                    label: c.fields.urlKey,
                    tooltip: c.fields.label
                }))
            );
        }
    } else if (defaultBookshelfUrlKey && defaultBookshelfUrlKey !== "none") {
        // 'bookshelves' already has "none", so don't add another "none".
        // This will usually be overwritten soon, but if we can't get to contentful
        // to get the actual list of possibilities we will leave it here.
        // Note that as we don't yet have any better label, we use defaultBookshelfUrlKey.
        bookshelves.push({
            value: defaultBookshelfUrlKey,
            label: defaultBookshelfUrlKey,
            tooltip: ""
        });
    }

    return {
        project: project,
        defaultBookshelfUrlKey: defaultBookshelfUrlKey,
        validBookshelves: bookshelves,
        error: error
    };
}
