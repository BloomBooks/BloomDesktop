import { postJson } from "../../utils/bloomApi";

/**
 * Tell C# the user picked a page, in the page list or among the other pages in the page frame's
 * grid (bookGridView.ts). C# saves the page we are leaving from the snapshot it already holds (see
 * pageSnapshot.ts), and answers whether it has started opening the page. It does not open it when
 * it cannot save the page being left (see EditingModel.MergeCurrentPageThenSave), and then calls
 * onRefused, as it does when the request fails; a failure is still reported.
 */
export function postPageClicked(
    pageId: string,
    detail: string,
    onSuccess?: () => void,
    onRefused?: () => void,
): void {
    void postJson(
        "pageList/pageClicked",
        { pageId, detail },
        (response) => {
            if (response.data === false) onRefused?.();
            else onSuccess?.();
        },
        (error) => {
            onRefused?.();
            throw error;
        },
    );
}
