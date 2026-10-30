import { css } from "@emotion/react";

import * as React from "react";
import {
    get,
    post,
    postJson,
    postString,
    useApiStringState,
} from "../utils/bloomApi";
import { P } from "../react_components/l10nComponents";
import { RequiresSubscriptionOverlayWrapper } from "../react_components/requiresSubscription";
import "./TeamCollectionSettingsPanel.less";
import { lightTheme } from "../bloomMaterialUITheme";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { tabMargins } from "../collection/commonTabSettings";
import BloomButton from "../react_components/bloomButton";

import { NoteBox, WarningBox } from "../react_components/boxes";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { useEffect } from "react";
import { Label } from "../react_components/l10nComponents";
import { TextField } from "@mui/material";
import { DialogHelpButton } from "../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../react_components/l10nHooks";
import { administratorsAreValid } from "./administrators";
// LEGACY: this component, TeamCollectionSettingsPanel, and the WireUpForWinforms call at the end of
// this file are only for the WinForms Collection Settings dialog and are deleted with it at cutover
// (BL-17042). TeamCollectionSettings, further down this file, stays: the new dialog's Team
// Collection page uses it.
// The contents of the Team Collection panel of the WinForms Settings dialog. (The React Collection
// Settings dialog has its own page, settingsPages/TeamCollectionPage.tsx, which shows
// TeamCollectionSettings.) The administrators being typed live in C#, which that dialog saves on
// OK, so each change is posted to it.

export const TeamCollectionSettingsPanel: React.FunctionComponent = () => {
    const [adminstratorEmail, setAdminstratorEmail] =
        React.useState<string>("");

    useEffect(() => {
        get("settings/administrators", (result) => {
            setAdminstratorEmail(result.data);
        });
    }, []);

    return (
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={lightTheme}>
                <div
                    css={css`
                        margin: ${tabMargins.top} ${tabMargins.side}
                            ${tabMargins.bottom};
                    `}
                >
                    <TeamCollectionSettings
                        administrators={adminstratorEmail}
                        onAdministratorsChange={(newAdminString) => {
                            setAdminstratorEmail(newAdminString);
                            postString(
                                "settings/administrators",
                                newAdminString,
                            );
                        }}
                        onCreateTeamCollection={() =>
                            post(
                                "teamCollection/showCreateTeamCollectionDialog",
                            )
                        }
                    />
                </div>
            </ThemeProvider>
        </StyledEngineProvider>
    );
};

/**
 * What the Team Collection settings show: the experimental warning, and either the collection's
 * shared folder and administrators or how to create or join a Team Collection, all under the
 * subscription overlay. Whoever shows it keeps the administrators being typed.
 */
export const TeamCollectionSettings: React.FunctionComponent<{
    administrators: string;
    onAdministratorsChange: (administrators: string) => void;
    onCreateTeamCollection: () => void;
    // The collection name has an unsaved edit, so Create is disabled: the Team Collection would be
    // made under the old name (the rename only happens as Bloom restarts), and the Create dialog
    // asks the user to approve the old name.
    renameUnsaved?: boolean;
}> = (props) => {
    const [repoFolderPath] = useApiStringState(
        "teamCollection/repoFolderPath",
        "",
    );
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
    const administratorsValid = administratorsAreValid(props.administrators);

    const intro: JSX.Element = (
        <div>
            <ExperimentalWarningBox />
            <P
                l10nKey="TeamCollection.Intro"
                l10nParam0="https://docs.bloomlibrary.org/team-collections-intro"
                // Todo: once we have an actual video this should link to it! For now just another link to the document.
                //l10nParam1="https://docs.bloomlibrary.org/team-collections"
                temporarilyDisableI18nWarning={true}
            >
                Bloom's Team Collection system helps your team collaborate as
                you create, translate, and edit books. See how it works
                [here](%0).
            </P>
        </div>
    );

    const isTeamCollection: JSX.Element = (
        <div>
            <P
                l10nKey="TeamCollection.ThisIsATC"
                className="teamCollection-heading no-space-below"
                temporarilyDisableI18nWarning={true}
            >
                This is a Team Collection
            </P>
            <P
                l10nKey="TeamCollection.CloudLocation"
                temporarilyDisableI18nWarning={true}
                className="no-space-below"
            >
                Cloud Storage Folder Location:
            </P>
            <a
                className="directory-link"
                href=""
                onClick={(e) => {
                    e.preventDefault();
                    postJson("fileIO/showInFolder", {
                        folderPath: repoFolderPath,
                    });
                }}
            >
                {repoFolderPath}
            </a>
            <Label
                l10nKey="TeamCollection.AdministratorEmails"
                htmlFor="adminstratorEmails"
            >
                Administrator Emails:
            </Label>
            <TextField
                id="adminstratorEmails"
                value={props.administrators}
                onChange={(event) => {
                    props.onAdministratorsChange(event.target.value);
                }}
                error={!administratorsValid}
                helperText={
                    administratorsValid
                        ? undefined
                        : invalidAdministratorsMessage
                }
                required={false}
                css={css`
                    width: 100%;
                    margin-top: 5px;
                `}
            ></TextField>

            <P
                l10nKey="TeamCollection.AddingHelp"
                temporarilyDisableI18nWarning={true}
            >
                Need help adding someone to your Team Collection?
            </P>
            <div className="align-right">
                <DialogHelpButton
                    l10nKey="TeamCollection.HowToAddSomeone"
                    temporarilyDisableI18nWarning={true}
                    variant="text"
                    helpId="Tasks/Basic_tasks/Team_Collections/Add_someone_to_a_Team_Collection.htm"
                    buttonText="How to add someone to this Team Collection"
                />
            </div>
        </div>
    );

    const isNotTeamCollection: JSX.Element = (
        <div>
            {props.renameUnsaved && <NoteBox>{saveRenameFirstMessage}</NoteBox>}
            <div className="align-right">
                <BloomButton
                    className="align-right"
                    l10nKey="TeamCollection.CreateTeamCollection"
                    enabled={!props.renameUnsaved}
                    hasText={true}
                    variant="outlined"
                    onClick={props.onCreateTeamCollection}
                    temporarilyDisableI18nWarning={true}
                >
                    Create a Team Collection
                </BloomButton>
            </div>
            <P
                l10nKey="TeamCollection.JoiningHelp"
                className="extra-space-above"
                temporarilyDisableI18nWarning={true}
            >
                Has someone in your team already created a Team Collection for
                your Team?
            </P>
            <div className="align-right">
                <DialogHelpButton
                    l10nKey="TeamCollection.Joining"
                    temporarilyDisableI18nWarning={true}
                    variant="text"
                    helpId="Tasks/Basic_tasks/Team_Collections/Join_a_Team_Collection.htm"
                    buttonText="How to join an existing Team Collection"
                />
            </div>
        </div>
    );

    return (
        <div id="teamCollection-settings">
            <RequiresSubscriptionOverlayWrapper featureName="TeamCollection">
                <React.Fragment>
                    {intro}
                    {repoFolderPath ? isTeamCollection : isNotTeamCollection}
                </React.Fragment>
            </RequiresSubscriptionOverlayWrapper>
        </div>
    );
};

const ExperimentalWarningBox: React.FunctionComponent = () => (
    <WarningBox>
        <span>
            This is an <strong>experimental</strong> feature. Please contact us
            at{" "}
            <a
                href="mailto:experimental@bloomlibrary.org?subject= Our interest in Team Collections"
                target="blank"
            >
                experimental@bloomlibrary.org
            </a>{" "}
            so that we can talk over your needs and make sure that this feature
            is ready for you.
        </span>
    </WarningBox>
);
WireUpForWinforms(TeamCollectionSettingsPanel);
