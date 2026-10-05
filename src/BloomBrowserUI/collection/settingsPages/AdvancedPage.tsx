import { css } from "@emotion/react";
import * as React from "react";
import {
    ConfigrBoolean,
    ConfigrGroup,
    ConfigrInput,
    ConfigrPage,
} from "@sillsdev/config-r";
import { useL10n } from "../../react_components/l10nHooks";
import { ICollectionSettingsResponse } from "../collectionSettingsTypes";

/**
 * The Advanced page of the Collection Settings dialog (BL-16737): whether Bloom updates itself,
 * and the collection's name. It offers what the old dialog did, on its Advanced and Project
 * Information tabs.
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
            {/* Automatic updating is a setting of Bloom on this computer, not of the collection,
                and only some platforms support it. */}
            {props.settings?.autoUpdateSupported && (
                <ConfigrGroup label={programLabel}>
                    <ConfigrBoolean
                        label={autoUpdateLabel}
                        description={autoUpdateDescription}
                        path="advanced.autoUpdate"
                    />
                </ConfigrGroup>
            )}
            <ConfigrGroup label={collectionLabel}>
                {/* As in the WinForms dialog, a Team Collection may not be renamed. */}
                <div
                    css={css`
                        // Config-R draws a disabled row's description as faintly as the rest of
                        // the row, but here it is the explanation the user needs to read, so it
                        // gets MUI's ordinary secondary text color.
                        .MuiTypography-caption {
                            color: rgba(0, 0, 0, 0.6);
                        }
                    `}
                >
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
                </div>
            </ConfigrGroup>
        </ConfigrPage>
    );
}
