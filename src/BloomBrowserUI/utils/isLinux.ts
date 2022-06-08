export function isLinux(): boolean {
    const searchString = window.location.search;
    const i = searchString.indexOf("isLinux=");
    if (i >= 0) {
        const start = i + "isLinux=".length;
        return (
            searchString.substring(start, start + 4).toLowerCase() === "true"
        );
    }
    return window.navigator.userAgent.indexOf("Linux") >= 0;
}
