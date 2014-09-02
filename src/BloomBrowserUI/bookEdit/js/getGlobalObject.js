/**
 * Finds the GlobalObject on the main document
 */
function getGlobalObject() {

    if (typeof globalObject === 'object') {
        return globalObject;
    }
    else if (typeof window.parent.globalObject === 'object') {
        return window.parent.globalObject;
    }

    // not found
    return null
}
