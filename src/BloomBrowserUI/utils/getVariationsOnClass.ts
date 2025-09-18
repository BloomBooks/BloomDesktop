// Looking at all the CSS rules in the document, this function finds the
// ones where a selector looks for a class that starts with a given prefix.
// It finds the unique continuations of that prefix.
// For example, if it is passed "game-theme-", it will find rules with
// selectors like ".game-theme-red", ".game-theme-blue", and ".game-theme-green"
// and produce ["blue", "green", "red"].
// The results are passed to the handleResults function.
// If the document is not yet completely loaded, it will attach a one-time event
// handler and call handleResults again after the document is ready.
export function getVariationsOnClass(
    prefix: string,
    doc: Document,
    handleResults: (variations: string[]) => void,
): void {
    const results = new Set<string>();

    // This function does most of the work. It gets called immediately and possibly again
    // when the document is fully loaded.
    const searchStyleSheets = () => {
        try {
            const stylesheets = Array.from(doc.styleSheets);
            for (const sheet of stylesheets) {
                try {
                    // This could throw an error for cross-origin stylesheets, but we don't expect any.
                    const rules = Array.from(sheet.cssRules) as CSSStyleRule[];

                    for (const rule of rules) {
                        if (rule.selectorText) {
                            const re = new RegExp(`\\.${prefix}([\\w-]+)`, "g");
                            let match;
                            while ((match = re.exec(rule.selectorText))) {
                                results.add(match[1]); // add the theme name without the prefix
                            }
                        }
                    }
                } catch (e) {
                    console.warn(
                        "Could not access stylesheet rules:",
                        sheet.href,
                    );
                    console.warn(e);
                }
            }
        } catch (e) {
            console.error("Error processing stylesheets:", e);
        }
        // go with whatever we managed to get
        handleResults((Array.from(results) as string[]).sort());
    };

    // In case the page is not fully loaded yet, try again when it is, to make sure we don't miss any.
    if (doc.readyState !== "complete") {
        doc.defaultView?.addEventListener(
            "load",
            searchStyleSheets,
            { once: true }, // remove the listener after it runs once
        );
    }
    // If the document was already complete, we'll get the results now. If not, we'll get them when the load
    // completes. If, by some bizarre race condition, the load completed between when we checked the
    // status and when we added the event handler, then it must be complete now, so we'll get them now.
    // We may possibly get the results twice. In my testing, the document was always
    // complete, so checking it and possibly calling from the event handler is just a precaution and should
    // seldom slow things down at all.
    searchStyleSheets();
}
