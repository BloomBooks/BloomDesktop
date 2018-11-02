// Utility functions related to URLs

export class UrlUtils {
    // input may have ? followed by params.
    // input may have # followed by fragment (after params, if at all).
    // strip them off.
    // Does not encode or decode; assumes any ? or # that is not a delimiter is %encoded
    // Currently intended for relative URLs; hence, does not handle leading elements before path.
    public static extractPathComponent(combined: string) {
        if (!combined) {
            return null;
        }
        // (I) tried  return new URL(combined).pathname;, but URL is 'unavailable' according to debugger.)

        const paramIndex = combined.indexOf("?");
        if (paramIndex >= 0) {
            return combined.substring(0, paramIndex); // also removes fragment if any
        }

        const hashIndex = combined.indexOf("#");
        if (hashIndex >= 0) {
            return combined.substring(0, hashIndex);
        }

        return combined;
    }
}
