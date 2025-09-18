import { EntryCollection } from "contentful";
import { useState, useEffect } from "react";
//import { useIntl } from "react-intl";
import { getContentfulClient } from "./ContentfulContext";

const defaultContentfulLocale = "en-US";

// Pass the specified query to contentful. Initially will typically return
// {loading:true, results:[], error:false}. When we have data,
// it will return {loading:false, result: the data, error:false}.
// As a special case, useful when we must call the function by rules of hooks
// but don't actually want it to query contentful, if query is falsy
// we return {loading:false, result:[], error:false} without sending anything to contentful.
// If something goes wrong with the query (this currently includes both things like
// syntax errors in the query and repeated network failures), we eventually get
// {loading:false, result:[], error:true}.
// Note: BloomLibrary2 has a version of this code that caches the results.
// BloomEditor doesn't need that yet.
export function useContentful(query: any): {
    loading: boolean;
    result: any[];
    error: boolean;
} {
    const [results, setResults] = useState<{
        queryString: string;
        result: any[] | undefined;
        error: boolean;
    }>({ queryString: "", result: [], error: false });

    const locale =
        /* useGetContentfulBestLocale() || */ defaultContentfulLocale;

    const queryString = JSON.stringify(query);
    useEffect(() => {
        if (!query) {
            // arguably we could setResult, but it's better not to trigger
            // a state change that would cause another render.
            return;
        }

        getContentfulClient()
            .getEntries({
                include: 10, // depth
                locale: defaultContentfulLocale,
                ...query,
                // The max is 1000, the default is 100.
                // (as of 1/2021, we have 95 collections, but that will continue to grow...)
                limit: 1000,
            })
            .then((entries: EntryCollection<unknown>) => {
                setResults({
                    queryString,
                    result: entries.items,
                    error: false,
                });
            })
            .catch((err: Error) => {
                console.error(JSON.stringify(err));
                setResults({ queryString, result: [], error: true });
            });

        // We want to depend on query, but not in a way that causes a
        // new http request just because the client's render creates
        // a new object with the same content on each call.
    }, [queryString, locale]);

    if (!query) {
        // now we're past the hooks, we can take our early exit.
        return { loading: false, result: [], error: false };
    }

    if (!results || !results.result || results.queryString !== queryString) {
        return { loading: true, result: [], error: false };
    }
    return { loading: false, result: results.result, error: results.error };
}

// BloomLibrary2 has a version of this file that contains code for using a
// localized version of Contentful. We can grab it if we need it.

interface IBookshelf {
    urlKey: string;
    label: string;
}

// currently unused, untested: started writing while trying to factor the querying logic out of the DefaultBookshelfControl
export function useContentfulBookShelvesForSubscription(
    subscriptionId: string,
): { loading: boolean; bookshelves: IBookshelf[]; error: boolean } {
    const { loading, result, error } = useContentful(
        subscriptionId
            ? {
                  content_type: "enterpriseSubscription",
                  select: "fields.collections",
                  include: 2, // depth: we want the bookshelf collection objects as part of this query
                  "fields.id": `${subscriptionId}`,
              }
            : undefined, // no project means we don't want useContentful to do a query
    );

    if (!subscriptionId) {
        return { loading: false, bookshelves: [], error: false };
    }
    if (loading || error || !result) {
        return { loading, bookshelves: [], error };
    }
    return {
        loading,
        error,
        bookshelves: result.map((c: any) => ({
            urlKey: c.fields.urlKey,
            label: c.fields.label as string,
        })),
    };
}

// If urlKey is defined, get the corresponding label for the collection.
// If it's undefined, return the provided default value.
export function useGetLabelForCollection(
    urlKey: string,
    defaultResult: string,
): string {
    const { result } = useContentful(
        urlKey
            ? {
                  content_type: "collection",
                  select: "fields.label",
                  include: 2, // depth: we want the bookshelf collection objects as part of this query
                  "fields.urlKey": `${urlKey}`,
              }
            : undefined,
    );
    return result && result.length > 0 ? result[0].fields.label : defaultResult;
}
