import { css } from "@emotion/react";
import * as React from "react";
import { ConfigrGroup, ConfigrPage, ConfigrStatic } from "@sillsdev/config-r";
import { useL10n } from "../../react_components/l10nHooks";
import { get } from "../../utils/bloomApi";
import { CreateTeamCollectionDialog } from "../../teamCollection/CreateTeamCollection";
import { administratorsAreValid } from "../../teamCollection/administrators";
import { TeamCollectionSettings } from "../../teamCollection/TeamCollectionSettingsPanel";
import { kTeamCollectionsFeatureToken } from "./ExperimentalPage";
import { ISettingsPage, ISettingsPageContext } from "./settingsPage";

/**
 * The Team Collection page of the Collection Settings dialog: the TeamCollectionSettings panel,
 * whose values are edited through context.editValues since it is not made of Config-R controls.
 * Create & Restart in the Create dialog saves at once, with the folder to create the Team
 * Collection in.
 */
export function useTeamCollectionPage(
    context: ISettingsPageContext,
): ISettingsPage {
    const label = useL10n("Team Collection", "TeamCollection.TeamCollection");
    // The folder the Create dialog starts with while it is showing; undefined when it is not.
    const [createDialogFolder, setCreateDialogFolder] =
        React.useState<string>();

    const settings = context.settings;
    // Not offered while the feature is off (as saved) and this is not a Team Collection, nor at
    // all for a book downloaded from Bloom Library for editing, which must not become one.
    if (
        !settings ||
        !context.values ||
        settings.editingABlorgBook ||
        !(
            settings.isTeamCollection ||
            settings.values.experimental[kTeamCollectionsFeatureToken]
        )
    )
        return { element: undefined };
    const values = context.values.teamCollection;
    // The panel says what is wrong. Administrators saved earlier under C#'s looser rule, such as
    // admin@intranet, must be fixed too.
    const canSave =
        !settings.isTeamCollection ||
        administratorsAreValid(values.administrators);

    const element = (
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
                                context.editValues({
                                    teamCollection: {
                                        ...values,
                                        administrators,
                                    },
                                })
                            }
                            onCreateTeamCollection={() =>
                                get(
                                    "teamCollection/defaultRepoFolder",
                                    (result) =>
                                        setCreateDialogFolder(result.data),
                                )
                            }
                            // Compared as it would be saved (valuesToSave is set whenever
                            // values is), so spaces typed around the name do not count.
                            renameUnsaved={
                                context.valuesToSave!.advanced
                                    .collectionName !==
                                settings.values.advanced.collectionName
                            }
                        />
                        {createDialogFolder !== undefined && (
                            <CreateTeamCollectionDialog
                                dialogEnvironment={{
                                    dialogFrameProvidedExternally: false,
                                    initiallyOpen: true,
                                }}
                                defaultRepoFolder={createDialogFolder}
                                onFolderChosen={(repoFolderParentPath) => {
                                    setCreateDialogFolder(undefined);
                                    context.saveNow({
                                        teamCollection: {
                                            ...values,
                                            createInFolder:
                                                repoFolderParentPath,
                                        },
                                    });
                                }}
                                onClose={() => setCreateDialogFolder(undefined)}
                            />
                        )}
                    </div>
                </ConfigrStatic>
            </ConfigrGroup>
        </ConfigrPage>
    );
    return { element, canSave };
}
