import { EntryCollection } from "contentful";
import { useState, useEffect } from "react";
//import { useIntl } from "react-intl";
import { getContentfulClient } from "./ContentfulContext";

const defaultContentfulLocale = "en-US";

// Basically a map of queryString (created from query) to the raw Contentful query result.
// This may be the entire set of collections or a single banner definition or any other
// query result from Contentful.
// Bloom probably doesn't much need this (yet, at least) but I copied most of this code
// from BloomLibrary2 and it's easiest to keep it as little changed as possible,
// especially w.r.t. things we might want one day.
const contentfulCache: any = {};

// Pass the specified query to contentful. Initially will typically return
// {loading:true} unless the result is already cached. When we have data,
// it will return {loading:false, result: the data}.
// As a special case, useful when we must call the function by rules of hooks
// but don't actually want it to query contentful, if query is falsy
// we return {loading:false result:[]} without sending anything to contentful.
export function useContentful(
    query: any
): { loading: boolean; result: any[] | undefined; error: boolean } {
    const [results, setResults] = useState<{
        queryString: string;
        result: any[] | undefined;
        error: boolean;
    }>({ queryString: "", result: undefined, error: false });

    const locale = /* useGetContentfulBestLocale() || */ defaultContentfulLocale;

    const queryString = JSON.stringify(query);
    useEffect(() => {
        if (!query) {
            // arguably we could setResult, but it's better not to trigger
            // a state change that would cause another render.
            return;
        }
        ///if (locale === "loading") return;

        if (contentfulCache[queryString]) {
            setResults({
                queryString,
                result: contentfulCache[queryString],
                error: false
            });
            return;
        }

        // getContentfulEntries(locale).catch(() => {
        //     // This (running a second time with the default locale) is just a safety net.
        //     // Without this, if the hardcoded locale list in useGetContentfulLocales
        //     // has a locale which isn't set up at the top level in Contentful, users would get errors.
        //     // (Note, there is no error if we simply request a resource which hasn't been translated into a set-up locale.)
        //     // You could argue that removing locale set-up from contentful without removing it from the
        //     // list in useGetContentfulLocales is our mistake and we would want it to fail fast.
        //     // But you have to be running your browser in that locale to notice the errors, so I thought this
        //     // safety net was appropriate.
        //     getContentfulEntries(defaultContentfulLocale).catch(
        //         (err: Error) => {
        //             console.error(JSON.stringify(err));
        //             dummyToThrowStuffToErrorBoundaryInsideAHook(() => {
        //                 throw err;
        //             });
        //         }
        //     );
        // });

        getContentfulEntries(defaultContentfulLocale).catch((err: Error) => {
            console.error(JSON.stringify(err));
            setResults({ queryString, result: [], error: true });
        });

        function getContentfulEntries(localeForEntries: string) {
            return getContentfulClient()
                .getEntries({
                    include: 10, // depth
                    locale: localeForEntries,
                    ...query,
                    // The max is 1000, the default is 100.
                    // (as of 1/2021, we have 95 collections, but that will continue to grow...)
                    limit: 1000
                })
                .then((entries: EntryCollection<unknown>) => {
                    contentfulCache[queryString] = entries.items;
                    setResults({
                        queryString,
                        result: entries.items,
                        error: false
                    });
                });
        }

        // We want to depend on query, but not in a way that causes a
        // new http request just because the client's render creates
        // a new object with the same content on each call.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [queryString, locale]);

    if (!query) {
        // now we're past the hooks, we can take our early exit.
        return { loading: false, result: [], error: false };
    }

    if (!results || !results.result || results.queryString !== queryString) {
        return { loading: true, result: undefined, error: false };
    }

    return { loading: false, result: results.result, error: results.error };
}

// function useGetContentfulBestLocale(): string | undefined {
//     const currentLocale = useIntl().locale;

//     const contentfulLocales = useGetContentfulLocales();
//     if (contentfulLocales) {
//         if (contentfulLocales.includes(currentLocale)) {
//             return currentLocale;
//         } else {
//             // We didn't find the full locale tag. Try just the part before the dash if there is one.
//             const iDash = currentLocale.indexOf("-");
//             if (iDash > 0) {
//                 const langTagOnly = currentLocale.substring(0, iDash);
//                 if (contentfulLocales.includes(langTagOnly)) return langTagOnly;
//             }
//             return undefined;
//         }
//     }
//     return "loading";
// }

// let localesCache: string[] | undefined;
// function useGetContentfulLocales(): string[] | undefined {
//     return ["en-US", "es", "fr"];

//     // The code below here was used to dynamically get the list of locales from Contentful.
//     // But it was a 150-200ms bottleneck before the site loaded.
//     // I'm keeping the code in case we ever want to go back to it, since it was a bit tricky to get right.

//     // see https://github.com/facebook/react/issues/14981
//     // eslint-disable-next-line no-unreachable
//     const [
//         ,
//         /*unused*/ dummyToThrowStuffToErrorBoundaryInsideAHook
//     ] = useState();
//     const [tempLocalesCache, setLocalesCache] = useState<string[] | undefined>(
//         undefined
//     );
//     if (localesCache) return localesCache;

//     getContentfulClient()
//         .getLocales()
//         .then(locales => {
//             //console.log(JSON.stringify(locales, null, 4));
//             localesCache = locales.items.map(locale => locale.code);
//             // this is needed to wake up react somehow and cause things to re-render
//             setLocalesCache(localesCache);
//         })
//         .catch((err: Error) => {
//             console.error(JSON.stringify(err));
//             dummyToThrowStuffToErrorBoundaryInsideAHook(() => {
//                 throw err;
//             });
//         });

//     // Eventually, the "then" above will populate localesCache, cause a refresh,
//     // this function will run again, and we will return the populated
//     // localesCache. Until then, just return undefined which we treat as the
//     // "loading" state.
//     // eslint-disable-next-line no-unreachable
//     return tempLocalesCache;
// }
