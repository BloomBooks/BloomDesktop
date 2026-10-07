import * as React from "react";
import {
    ConfigrBoolean,
    ConfigrGroup,
    ConfigrInput,
    ConfigrPage,
} from "@sillsdev/config-r";
import { useL10n } from "../../react_components/l10nHooks";
import {
    ICollectionSettingsResponse,
    ICollectionSettingsValues,
} from "../collectionSettingsTypes";

/**
 * The values as the Advanced page needs them saved. Config-R does not trim what is typed, and C#
 * saves exactly what we post, so a name typed with a trailing space would otherwise count as a
 * rename (and Windows will not make a folder whose name ends in a space). Only an edited name is
 * trimmed: an untouched one goes back exactly as it was, so a collection whose folder name already
 * starts with a space is not renamed by saving some other setting, yet a user can still delete
 * that space.
 */
export function advancedValuesToSave(
    values: ICollectionSettingsValues,
    loadedValues: ICollectionSettingsValues,
): ICollectionSettingsValues {
    const name = values.advanced.collectionName;
    return {
        ...values,
        advanced: {
            ...values.advanced,
            collectionName:
                name === loadedValues.advanced.collectionName
                    ? name
                    : name.trim(),
        },
    };
}

/**
 * The Advanced page of the Collection Settings dialog: whether Bloom updates itself, and the
 * collection's name.
 */
export function useAdvancedPage(props: {
    // Undefined until the dialog's GET collection/settings has answered.
    settings: ICollectionSettingsResponse | undefined;
}): React.ReactElement {
    const label = useL10n("Advanced", "Common.Advanced");
    const programLabel = useL10n(
        "Program",
        "CollectionSettingsDialog.AdvancedTab.Program",
    );
    const autoUpdateLabel = useL10n(
        "Automatically Update Bloom",
        "CollectionSettingsDialog.AdvancedTab.AutoUpdate",
    );
    const autoUpdateDescription = useL10n(
        "When a new version of Bloom is available, Bloom will get it for you.",
        "CollectionSettingsDialog.AdvancedTab.AutoUpdate.Description",
    );
    const collectionLabel = useL10n(
        "Collection",
        "CollectionSettingsDialog.AdvancedTab.Collection",
    );
    const collectionNameLabel = useL10n(
        "Collection Name",
        "NewCollectionWizard.CollectionName",
    );
    const noRenameTeamCollectionMessage = useL10n(
        "The collection name cannot be changed because this is a Team Collection. Contact the Bloom team for more information.",
        "CollectionSettingsDialog.AdvancedTab.NoRenameTeamCollection",
    );

    const isTeamCollection = props.settings?.isTeamCollection === true;

    return (
        <ConfigrPage label={label} pageKey="advanced" topLevel={true}>
            {/* Automatic updating is a setting of Bloom on this computer, not of the collection. */}
            <ConfigrGroup label={programLabel}>
                <ConfigrBoolean
                    label={autoUpdateLabel}
                    description={autoUpdateDescription}
                    path="advanced.autoUpdate"
                />
            </ConfigrGroup>
            <ConfigrGroup label={collectionLabel}>
                {/* A Team Collection may not be renamed. */}
                <ConfigrInput
                    label={collectionNameLabel}
                    path="advanced.collectionName"
                    disabled={isTeamCollection}
                    description={
                        isTeamCollection
                            ? noRenameTeamCollectionMessage
                            : undefined
                    }
                />
            </ConfigrGroup>
        </ConfigrPage>
    );
}
