const kTestRoot = "testRoot";

// Gets the element (or creates it if necessary) that represents the root div for unit tests to insert any HTML needed for their test.
//
// Note: The HTML and even the body already has lots of stuff attached to it from Vitest etc.
// So you don't want to completely reinitialize either of those.
// Just have a wrapper div inside the body instead.
export function getTestRoot() {
    let elem = document.getElementById(kTestRoot);
    if (elem) {
        return elem;
    }

    elem = document.createElement("div");
    elem.id = kTestRoot;

    const body = document.getElementsByTagName("body").item(0)!;

    body.appendChild(elem);
    return elem;
}

// Cleans up the test root div to a blank state, which unit tests can call if they want to ensure they have a blank HTML slate.
// Otherwise, non-deterministic unit test behavior could happen depending on which tests run first, how well they clean themselves up,
// whether any other tests clean up the HTML, etc.
// FYI - This article recommends it's easier to debug if each test ensures it starts with a blank slate, rather than that each test cleans up properly
//       https://www.martinfowler.com/articles/nonDeterminism.html
export function cleanTestRoot() {
    const elem = document.getElementById(kTestRoot);
    if (elem) {
        elem.innerHTML = "";
    }
}

// Removes the test root completely from the DOM
export function removeTestRoot() {
    const elem = document.getElementById(kTestRoot);
    if (elem) {
        elem.remove();
    }
}

// Makes sure that the specified HTML element ids do not exist in the document.
// Throws an exception if one does.
export function ensureIdsDontExist(ids: string[]) {
    ids.forEach((id) => {
        const elem = document.getElementById(id);
        if (elem) {
            throw new Error(
                `ID ${id} not expected to exist but was found. Element = ${elem.outerHTML}`,
            );
        }
    });
}

export function getStringDifference(actual: string, expected: string) {
    const index = findFirstDiffPos(actual, expected);
    if (index < 0) {
        return "Strings are equal.";
    } else {
        const contextActual = getStringContext(actual, index);
        const contextExpected = getStringContext(expected, index);
        return `Strings differ at index ${index}.\nActual\n${contextActual.diffMarker}\n${contextActual.context}\n\nExpected\n${contextExpected.diffMarker}\n${contextExpected.context}`;
    }
}

function findFirstDiffPos(a: string, b: string) {
    if (a === b) return -1;
    let i = 0;
    while (a[i] === b[i]) i++;
    return i;
}

/**
 * Provides the context of {str} around index ${index}
 * The {diffMarker} field will contain a string with an arrow pointing to the specified index
 * {diffMarker} should be printed above context
 */
export function getStringContext(str: string, index: number) {
    const startIndex = index - 5;
    const endIndex = str.length;

    if (startIndex < 0) {
        return {
            context: str.slice(0, endIndex),
            diffMarker: " ".repeat(index - 1) + String.fromCharCode(9660),
        };
    } else {
        return {
            context: "[...]" + str.slice(startIndex, endIndex),
            diffMarker:
                " ".repeat(index - startIndex + 5) + String.fromCharCode(9660),
        };
    }
}
