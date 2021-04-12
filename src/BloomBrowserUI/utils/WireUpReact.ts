import React = require("react");
import * as ReactDOM from "react-dom";
import { BookPreviewPanel } from "../bookPreview/BookPreviewPanel";
import { TeamCollectionSettingsPanel } from "../teamCollection/TeamCollectionSettingsPanel";
import { TeamCollectionDialog } from "../teamCollection/TeamCollectionDialog";
import { JoinTeamCollection } from "../teamCollection/JoinTeamCollection";
import { AutoUpdateSoftwareDialog } from "../react_components/AutoUpdateSoftwareDialog";
import { ProblemDialog } from "../problemDialog/ProblemDialog";
import { IndependentProgressDialog } from "../react_components/IndependentProgressDialog";

// this is a bummer... haven't figured out how to do a lookup just from the string... have to have this map
const knownComponents = {
    BookPreviewPanel: BookPreviewPanel,
    TeamCollectionSettingsPanel: TeamCollectionSettingsPanel,
    TeamCollectionDialog: TeamCollectionDialog,
    JoinTeamCollection: JoinTeamCollection,
    AutoUpdateSoftwareDialog: AutoUpdateSoftwareDialog,
    ProblemDialog: ProblemDialog,
    IndependentProgressDialog: IndependentProgressDialog
};

// This is called from an html file created in the c# ReactControl class.
(window as any).wireUpReact = (
    root: HTMLElement,
    reactComponentName: string
) => {
    ReactDOM.render(
        React.createElement(knownComponents[reactComponentName], {}, null),
        root
    );
};
