import { css } from "@emotion/react";
import * as React from "react";
import { ConfigrGroup, ConfigrPage, ConfigrStatic } from "@sillsdev/config-r";
import { useL10n } from "../../react_components/l10nHooks";
import { CreateTeamCollectionDialog } from "../../teamCollection/CreateTeamCollection";
import { administratorsAreValid } from "../../teamCollection/administrators";
import { TeamCollectionSettings } from "../../teamCollection/TeamCollectionSettings";
import {
    ICollectionSettingsResponse,
    ICollectionSettingsTeamCollection,
} from "../collectionSettingsTypes";
import { kTeamCollectionsFeatureToken } from "./ExperimentalPage";
import { ISettingsPage, ISettingsPageContext } from "./settingsPage";

/**
 * Whether the Team Collection page is offered. It is not while the Team Collections feature is
 * off (as saved) and this is not a Team Collection, nor at all in a collection holding a book
 * downloaded from Bloom Library for editing, which must not become one.
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
 * Whether the Team Collection values may be saved: in a Team Collection, the administrators must be
 * acceptable. (Ones saved before under C#'s looser rule, such as admin@intranet, must be fixed.)
 */
function teamCollectionValuesAreValid(
    settings: ICollectionSettingsResponse,
    values: ICollectionSettingsTeamCollection,
): boolean {
    return (
        !settings.isTeamCollection ||
        administratorsAreValid(values.administrators)
    );
}

/**
 * The Team Collection page of the Collection Settings dialog. TeamCollectionSettings is not made
 * of Config-R controls, so its values are edited through context.editValues. Creating a Team
 * Collection does not wait for the dialog's OK: the Create dialog's Create & Restart saves at
 * once, with the folder to create it in. Not offered in every collection (see
 * teamCollectionPageIsShown).
 */
export function useTeamCollectionPage(
    context: ISettingsPageContext,
): ISettingsPage {
    const label = useL10n("Team Collection", "TeamCollection.TeamCollection");
    const invalidAdministratorsMessage = useL10n(
        "Please enter one or more valid administrator email addresses, separated by commas or spaces.",
        "TeamCollection.InvalidAdminEmails",
    );
    const saveRenameFirstMessage = useL10n(
        "You have changed the collection's name. Save that first (Bloom will restart); then you can create a Team Collection with the new name.",
        "CollectionSettingsDialog.TeamCollectionPage.SaveRenameFirst",
        undefined,
        undefined,
        undefined,
        true,
    );
    const [creating, setCreating] = React.useState(false);

    if (!teamCollectionPageIsShown(context.settings))
        return { element: undefined };
    const values = context.values.teamCollection;
    const canSave = teamCollectionValuesAreValid(context.settings, values);
    // A Team Collection cannot be created while the collection name has an unsaved edit: it would
    // be made under the old name (the rename only happens as Bloom restarts), and the Create
    // dialog asks the user to approve the old name.
    const collectionNameEdited =
        context.valuesToSave.advanced.collectionName !==
        context.settings.values.advanced.collectionName;

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
                            administratorsError={
                                canSave
                                    ? undefined
                                    : invalidAdministratorsMessage
                            }
                            onCreateTeamCollection={() => setCreating(true)}
                            createDisabledReason={
                                collectionNameEdited
                                    ? saveRenameFirstMessage
                                    : undefined
                            }
                        />
                        {creating && (
                            <CreateTeamCollectionDialog
                                defaultRepoFolder={
                                    context.settings.defaultTeamCollectionFolder
                                }
                                // Create is disabled while the name has an unsaved edit, so
                                // this is the name the collection has.
                                collectionName={
                                    context.settings.values.advanced
                                        .collectionName
                                }
                                onFolderChosen={(repoFolderParentPath) => {
                                    setCreating(false);
                                    context.saveNow({
                                        teamCollection: {
                                            ...values,
                                            createInFolder:
                                                repoFolderParentPath,
                                        },
                                    });
                                }}
                                onClose={() => setCreating(false)}
                            />
                        )}
                    </div>
                </ConfigrStatic>
            </ConfigrGroup>
        </ConfigrPage>
    );
    return { element, canSave };
}
