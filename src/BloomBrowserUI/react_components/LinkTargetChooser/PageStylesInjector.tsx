import * as React from "react";
import { useEffect, useState } from "react";

// gets the contents of the stylesheets needed to display the
// html of each page in a thumbnail.
// Because our embedded browser doesn't support @scope yet, we are getting
// the actual contents and then pushing them into a style tag
export const PageStylesInjector: React.FunctionComponent<{
    bookId?: string;
}> = (props) => {
    const [scopedStyles, setScopedStyles] = useState<string>("");

    useEffect(() => {
        if (!props.bookId) {
            setScopedStyles("");
            return;
        }

        let canceled = false;
        const staticStylesheetUrls = [
            "/bloom/bookEdit/pageThumbnailList/pageThumbnailList.css",
            "/bloom/bookLayout/basePage.css",
            "/bloom/bookLayout/previewMode.css",
        ];
        const loadStyles = async () => {
            const stylePromises = [
                ...staticStylesheetUrls.map(fetchStylesheetContent),
                fetchStylesheetContent(
                    `/bloom/api/collections/bookFile?book-id=${props.bookId}&file=appearance.css`,
                ),
            ];

            const styleContents = await Promise.all(stylePromises);

            if (!canceled) {
                setScopedStyles(styleContents.join("\n"));
            }
        };

        loadStyles();

        return () => {
            canceled = true;
        };
    }, [props.bookId]);

    if (!scopedStyles) return null;

    return (
        <style
            dangerouslySetInnerHTML={{
                __html: `.page-chooser-scope { ${scopedStyles} }`,
            }}
        />
    );
};

const fetchStylesheetContent = async (url: string): Promise<string> => {
    try {
        const response = await fetch(url);
        if (!response.ok) {
            console.warn(`Failed to load stylesheet: ${url}`);
            return "";
        }
        return await response.text();
    } catch (error) {
        console.warn(`Error fetching stylesheet ${url}:`, error);
        return "";
    }
};
