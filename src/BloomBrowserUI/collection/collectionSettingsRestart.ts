import { ICollectionSettingsValues } from "./collectionSettingsTypes";

// Walks a dotted path such as "languages.language3.fontName" into the settings values.
// A path that runs into a missing or null branch yields undefined, which is what we want for
// things like language3 not existing.
function getValueAtPath(
    values: ICollectionSettingsValues,
    path: string,
): unknown {
    let current: unknown = values;
    for (const segment of path.split(".")) {
        if (current === undefined || current === null) {
            return undefined;
        }
        current = (current as Record<string, unknown>)[segment];
    }
    return current;
}

// Returns the subset of restartPaths whose value differs between initial and current. C# decides
// which paths are restart-worthy (in the GET reply) so that rule lives in one place.
export function changedRestartPaths(
    initial: ICollectionSettingsValues,
    current: ICollectionSettingsValues,
    restartPaths: string[],
): string[] {
    return restartPaths.filter((path) => {
        // Stringify so a path naming a whole object (e.g. "languages.language3") compares by
        // content; "?? null" makes an absent branch equal to an explicit null.
        return (
            JSON.stringify(getValueAtPath(initial, path) ?? null) !==
            JSON.stringify(getValueAtPath(current, path) ?? null)
        );
    });
}
