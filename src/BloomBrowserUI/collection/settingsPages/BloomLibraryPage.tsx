import { ConfigrGroup, ConfigrPage, ConfigrSelect } from "@sillsdev/config-r";
import { useL10n } from "../../react_components/l10nHooks";
import { useGetEnterpriseBookshelves } from "../useGetEnterpriseBookshelves";
import { RowWithSubscriptionBadge } from "./RowWithSubscriptionBadge";
import { ISettingsPage, ISettingsPageContext } from "./settingsPage";

/**
 * The Bloom Library page of the Collection Settings dialog: the bookshelf that books uploaded
 * from this collection go into.
 */
export function useBloomLibraryPage(
    context: ISettingsPageContext,
): ISettingsPage {
    const label = useL10n(
        "Bloom Library",
        "CollectionSettingsDialog.BloomLibraryPage",
    );
    const bookshelfLabel = useL10n(
        "Bookshelf",
        "CollectionSettingsDialog.BloomLibraryPage.Bookshelf",
    );
    const noBookshelfLabel = useL10n("None", "Common.None");
    const bookshelfDescription = useL10n(
        "Projects that have Bloom Enterprise subscriptions can arrange for one or more bookshelves on the Bloom Library. All books uploaded from this collection will go into the selected bookshelf.",
        "CollectionSettingsDialog.BloomLibraryPage.BookshelfDescription",
    );
    const noBookshelvesFromServer = useL10n(
        "Bloom could not reach the server to get the list of bookshelves.",
        "CollectionSettingsDialog.BookMakingTab.NoBookshelvesFromServer",
    );

    // Asked when the dialog opens, not when Config-R mounts this page (it mounts only the page
    // showing), so the list is usually in before anyone gets here.
    const { validBookshelves, error } = useGetEnterpriseBookshelves();

    // The collection stores "no bookshelf" as "", which Config-R's select shows by the label of
    // the option whose value is "". The hook calls that option "none"; we use our own.
    const options = [
        { value: "", label: noBookshelfLabel },
        ...validBookshelves
            .filter((shelf) => shelf.value !== "none")
            .map((shelf) => ({
                value: shelf.value,
                label: shelf.label,
                description: shelf.tooltip,
            })),
    ];
    // Keep the saved bookshelf choosable even when it is not in the list (Bloom Library could not
    // be reached, or the subscription no longer offers it), so that seeing it does not change it.
    const savedBookshelf =
        context.settings.values.bloomLibrary.defaultBookshelf;
    if (
        savedBookshelf &&
        !options.some((option) => option.value === savedBookshelf)
    )
        options.push({
            value: savedBookshelf,
            label: savedBookshelf,
            description: "",
        });

    const element = (
        <ConfigrPage label={label} pageKey="bloomLibrary" topLevel={true}>
            <ConfigrGroup label={bookshelfLabel}>
                <RowWithSubscriptionBadge subscriptionFeature="Bookshelf">
                    <ConfigrSelect
                        label={bookshelfLabel}
                        path="bloomLibrary.defaultBookshelf"
                        options={options}
                        // Nothing to choose without a subscription that has bookshelves.
                        disabled={options.length < 2}
                        description={
                            error
                                ? noBookshelvesFromServer
                                : bookshelfDescription
                        }
                    />
                </RowWithSubscriptionBadge>
            </ConfigrGroup>
        </ConfigrPage>
    );
    return { element };
}
