import { css } from "@emotion/react";
import * as React from "react";
import { ConfigrGroup, ConfigrPage, ConfigrStatic } from "@sillsdev/config-r";
import { useL10n } from "../../react_components/l10nHooks";
import { get } from "../../utils/bloomApi";
import { CreateTeamCollectionDialog } from "../../teamCollection/CreateTeamCollection";
import { administratorsAreValid } from "../../teamCollection/administrators";
import { TeamCollectionSettings } from "../../teamCollection/TeamCollectionSettingsPanel";
import {
    ICollectionSettingsResponse,
    ICollectionSettingsTeamCollection,
} from "../collectionSettingsTypes";
import { kTeamCollectionsFeatureToken } from "./ExperimentalPage";

/**
 * Whether the Team Collection page is offered. As in the WinForms dialog, it is not while the
 * Team Collections feature is off (as saved) and this is not a Team Collection, nor at all in a
 * collection holding a book downloaded from Bloom Library for editing, which must not become one.
 */
export function teamCollectionPageIsShown(
    settings: ICollectionSettingsResponse,
): boolean {
    if (settings.editingABlorgBook) return false;
    return (
        settings.isTeamCollection ||
        settings.values.experimental[kTeamCollectionsFeatureToken]
    );
}

/**
 * Whether the Team Collection values may be saved: in a Team Collection, administrators the user
 * has edited must be acceptable. Ones left as they were are not checked, because our check is a
 * little stricter than the C# one that let them in (it wants a dot in the domain).
 */
export function teamCollectionValuesAreValid(
    settings: ICollectionSettingsResponse,
    values: ICollectionSettingsTeamCollection,
): boolean {
    return (
        !settings.isTeamCollection ||
        values.administrators ===
            settings.values.teamCollection.administrators ||
        administratorsAreValid(values.administrators)
    );
}

/**
 * The Team Collection page of the Collection Settings dialog: the panel of the WinForms dialog's
 * Team Collection tab. Its values are not Config-R values, because the panel is not made of
 * Config-R controls; the dialog keeps them and puts them into the values it saves. Creating a
 * Team Collection waits for the dialog's OK, so its Cancel abandons that too. Undefined when the
 * page is not offered (see teamCollectionPageIsShown).
 */
export function useTeamCollectionPage(props: {
    // Undefined until the dialog's GET collection/settings has answered.
    settings: ICollectionSettingsResponse | undefined;
    // The values as the user is editing them; undefined until the settings have loaded.
    values: ICollectionSettingsTeamCollection | undefined;
    onChange: (values: ICollectionSettingsTeamCollection) => void;
}): React.ReactElement | undefined {
    const label = useL10n("Team Collection", "TeamCollection.TeamCollection");
    const invalidAdministratorsMessage = useL10n(
        "Please enter one or more valid administrator email addresses, separated by commas or spaces.",
        "TeamCollection.InvalidAdminEmails",
    );
    // The folder the Create dialog starts with while it is showing; undefined when it is not.
    const [createDialogFolder, setCreateDialogFolder] =
        React.useState<string>();

    if (
        !props.settings ||
        !props.values ||
        !teamCollectionPageIsShown(props.settings)
    )
        return undefined;
    const values = props.values;

    return (
        <ConfigrPage label={label} pageKey="teamCollection" topLevel={true}>
            <ConfigrGroup>
                {/* ConfigrStatic takes the props Config-R gives a group's children, which a
                    plain div would pass on to the DOM. */}
                <ConfigrStatic>
                    <div
                        css={css`
                            // Config-R pads its own rows like this.
                            padding: 8px 16px;
                        `}
                    >
                        <TeamCollectionSettings
                            administrators={values.administrators}
                            onAdministratorsChange={(administrators) =>
                                props.onChange({ ...values, administrators })
                            }
                            administratorsError={
                                teamCollectionValuesAreValid(
                                    props.settings,
                                    values,
                                )
                                    ? undefined
                                    : invalidAdministratorsMessage
                            }
                            onCreateTeamCollection={() =>
                                get(
                                    "teamCollection/defaultRepoFolder",
                                    (result) =>
                                        setCreateDialogFolder(result.data),
                                )
                            }
                            pendingCreateFolder={
                                values.createInFolder || undefined
                            }
                        />
                        {createDialogFolder !== undefined && (
                            <CreateTeamCollectionDialog
                                dialogEnvironment={{
                                    dialogFrameProvidedExternally: false,
                                    initiallyOpen: true,
                                }}
                                defaultRepoFolder={createDialogFolder}
                                onFolderChosen={(createInFolder) =>
                                    props.onChange({
                                        ...values,
                                        createInFolder,
                                    })
                                }
                                onClose={() => setCreateDialogFolder(undefined)}
                            />
                        )}
                    </div>
                </ConfigrStatic>
            </ConfigrGroup>
        </ConfigrPage>
    );
}
