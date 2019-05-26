export function isLinux(): boolean {
    const searchString = window.location.search;
    const i = searchString.indexOf("isLinux=");
    if (i >= 0) {
        return searchString.substr(i + "isLinux=".length, 4) === "true";
    }
    return false;
}
