import { css } from "@emotion/react";
import * as React from "react";
import { ConfigrGroup, ConfigrPage, ConfigrSelect } from "@sillsdev/config-r";
import { useL10n } from "../../react_components/l10nHooks";
import { BloomSubscriptionIndicatorIconAndText } from "../../react_components/requiresSubscription";
import { ICollectionSettingsResponse } from "../collectionSettingsTypes";
import { useGetEnterpriseBookshelves } from "../useGetEnterpriseBookshelves";

/**
 * The Bloom Library page of the Collection Settings dialog: the bookshelf that books uploaded
 * from this collection go into.
 */
export function useBloomLibraryPage(props: {
    dialogOpen: boolean;
    // Undefined until the dialog's GET collection/settings has answered.
    settings: ICollectionSettingsResponse | undefined;
}): React.ReactElement {
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
    const { validBookshelves, error } = useGetEnterpriseBookshelves(
        props.dialogOpen,
    );

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
    // (values is null for a user who may not edit the settings.)
    const savedBookshelf =
        props.settings?.values?.bloomLibrary.defaultBookshelf ?? "";
    if (
        savedBookshelf &&
        !options.some((option) => option.value === savedBookshelf)
    )
        options.push({
            value: savedBookshelf,
            label: savedBookshelf,
            description: "",
        });

    return (
        <ConfigrPage label={label} pageKey="bloomLibrary" topLevel={true}>
            <ConfigrGroup label={bookshelfLabel}>
                {/* One element around both keeps Config-R from drawing a divider between them. */}
                <div>
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
                    <div
                        css={css`
                            display: flex;
                            padding-bottom: 5px;
                            font-size: 12px;
                            font-weight: bold;
                        `}
                    >
                        <BloomSubscriptionIndicatorIconAndText
                            feature="Bookshelf"
                            css={css`
                                margin-left: auto;
                            `}
                        />
                    </div>
                </div>
            </ConfigrGroup>
        </ConfigrPage>
    );
}
