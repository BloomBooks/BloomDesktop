import { postDataWithConfigAsync } from "../../utils/bloomApi";

/**
 * Bloom's answer to the `getLocalizations` that the libraries Bloom hosts take
 * (bloom-image-gallery, bloom-table; bloom-ai-image-tools posts to the same
 * endpoint from its own frame). It is handed string ids from Bloom's XLF, each
 * with its English, and returns the text to show for each in the user's UI
 * language, falling back to the English.
 *
 * Every hosted library follows the same pattern: it exports all its strings by
 * XLF id with their English, asks for them once through this, and shows none
 * of its UI until the answer arrives, so the user never sees English first.
 */
export async function getLocalizations(
    strings: Record<string, string>,
): Promise<Record<string, string>> {
    // i18n/loadStrings expects form-encoded data, not JSON
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(strings)) {
        params.append(key, value);
    }
    const response = await postDataWithConfigAsync(
        "i18n/loadStrings",
        params,
        {},
    );
    return (response?.data ?? strings) as Record<string, string>;
}
