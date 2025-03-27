/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import {
    get,
    post,
    postJson,
    postString,
    useApiStringState
} from "../utils/bloomApi";
import { P } from "../react_components/l10nComponents";
import { RequiresSubscriptionOverlayWrapper } from "../react_components/requiresSubscription";
import "./TeamCollectionSettingsPanel.less";
import { lightTheme } from "../bloomMaterialUITheme";
import { ThemeProvider, StyledEngineProvider } from "@mui/material/styles";
import { tabMargins } from "../collection/commonTabSettings";
import BloomButton from "../react_components/bloomButton";

import { WarningBox } from "../react_components/boxes";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { useEffect } from "react";
import { Label } from "../react_components/l10nComponents";
import { TextField } from "@mui/material";

// The contents of the Team Collection panel of the Settings dialog.

export const TeamCollectionSettingsPanel: React.FunctionComponent = props => {
    const [repoFolderPath] = useApiStringState(
        "teamCollection/repoFolderPath",
        ""
    );

    const [adminstratorEmail, setAdminstratorEmail] = React.useState<string>(
        ""
    );

    useEffect(() => {
        get("settings/administrators", result => {
            setAdminstratorEmail(result.data);
        });
    }, []);

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
                onClick={e => {
                    e.preventDefault();
                    postJson("fileIO/showInFolder", {
                        folderPath: repoFolderPath
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
                value={adminstratorEmail}
                onChange={event => {
                    const newAdminString: string = event.target.value;
                    setAdminstratorEmail(newAdminString);
                    postString("settings/administrators", newAdminString);
                }}
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
                <BloomButton
                    l10nKey="TeamCollection.HowToAddSomeone"
                    temporarilyDisableI18nWarning={true}
                    variant="text"
                    enabled={true}
                    hasText={true}
                    onClick={() =>
                        post(
                            "help?topic=Tasks/Basic_tasks/Team_Collections/Add_someone_to_a_Team_Collection.htm"
                        )
                    }
                >
                    How to add someone to this Team Collection
                </BloomButton>
            </div>
        </div>
    );

    const isNotTeamCollection: JSX.Element = (
        <div>
            <div className="align-right">
                <BloomButton
                    className="align-right"
                    l10nKey="TeamCollection.CreateTeamCollection"
                    enabled={true}
                    hasText={true}
                    variant="outlined"
                    onClick={() =>
                        post("teamCollection/showCreateTeamCollectionDialog")
                    }
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
                <BloomButton
                    l10nKey="TeamCollection.Joining"
                    enabled={true}
                    hasText={true}
                    variant="text"
                    temporarilyDisableI18nWarning={true}
                    onClick={() =>
                        post(
                            "help?topic=Tasks/Basic_tasks/Team_Collections/Join_a_Team_Collection.htm"
                        )
                    }
                >
                    How to join an existing Team Collection
                </BloomButton>
            </div>
        </div>
    );

    return (
        <StyledEngineProvider injectFirst>
            <ThemeProvider theme={lightTheme}>
                <div
                    id="teamCollection-settings"
                    css={css`
                        margin: ${tabMargins.top} ${tabMargins.side}
                            ${tabMargins.bottom};
                    `}
                >
                    <RequiresSubscriptionOverlayWrapper featureName="TeamCollection">
                        <React.Fragment>
                            {intro}
                            {repoFolderPath
                                ? isTeamCollection
                                : isNotTeamCollection}
                        </React.Fragment>
                    </RequiresSubscriptionOverlayWrapper>
                </div>
            </ThemeProvider>
        </StyledEngineProvider>
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
