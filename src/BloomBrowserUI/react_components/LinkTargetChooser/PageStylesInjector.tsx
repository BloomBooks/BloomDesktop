import * as React from "react";
import { useEffect, useState } from "react";

// Gets the contents of the stylesheets needed to display the html of each page in a
// thumbnail. Returns undefined until they have loaded for this book. The caller should not
// lay out any thumbnails before then: they are a different size without these styles, so
// thumbnails drawn early jump when the styles land, and that can push the selected page back
// out of view after it has been scrolled to.
export const usePageStyles = (bookId?: string): string | undefined => {
    const [loaded, setLoaded] = useState<{ bookId: string; styles: string }>();

    useEffect(() => {
        if (!bookId) {
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
                    `/bloom/api/collections/bookFile?book-id=${bookId}&file=appearance.css`,
                ),
            ];

            const styleContents = await Promise.all(stylePromises);

            if (!canceled) {
                setLoaded({ bookId, styles: styleContents.join("\n") });
            }
        };

        loadStyles();

        return () => {
            canceled = true;
        };
    }, [bookId]);

    return loaded && loaded.bookId === bookId ? loaded.styles : undefined;
};

// Because our embedded browser doesn't support @scope yet, we get the actual contents
// of the stylesheets (see usePageStyles) and push them into a style tag.
export const PageStylesInjector: React.FunctionComponent<{
    styles: string;
}> = (props) => (
    <style
        dangerouslySetInnerHTML={{
            __html: `.page-chooser-scope { ${props.styles} }`,
        }}
    />
);

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
