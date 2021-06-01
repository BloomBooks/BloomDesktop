import React = require("react");
import * as ReactDOM from "react-dom";
import { BookPreviewPanel } from "../bookPreview/BookPreviewPanel";
import { TeamCollectionSettingsPanel } from "../teamCollection/TeamCollectionSettingsPanel";
import { TeamCollectionDialog } from "../teamCollection/TeamCollectionDialog";
import { JoinTeamCollectionDialog } from "../teamCollection/JoinTeamCollectionDialog";
import { AutoUpdateSoftwareDialog } from "../react_components/AutoUpdateSoftwareDialog";
import { ProblemDialog } from "../problemDialog/ProblemDialog";
import { ProgressDialog } from "../react_components/Progress/ProgressDialog";
import { IBloomDialogEnvironmentParams } from "../react_components/BloomDialog/BloomDialog";
import { CreateTeamCollectionDialog } from "../teamCollection/CreateTeamCollection";
import { DefaultBookshelfControl } from "../react_components/DefaultBookshelfControl";

// this is a bummer... haven't figured out how to do a lookup just from the string... have to have this map
const knownComponents = {
    BookPreviewPanel: BookPreviewPanel,
    TeamCollectionSettingsPanel: TeamCollectionSettingsPanel,
    TeamCollectionDialog: TeamCollectionDialog,
    JoinTeamCollectionDialog: JoinTeamCollectionDialog,
    AutoUpdateSoftwareDialog: AutoUpdateSoftwareDialog,
    ProblemDialog: ProblemDialog,
    ProgressDialog: ProgressDialog,
    CreateTeamCollectionDialog: CreateTeamCollectionDialog,
    DefaultBookshelfControl: DefaultBookshelfControl
};

// This is called from an html file created in the c# ReactControl class.
(window as any).wireUpReact = (
    root: HTMLElement,
    reactComponentName: string,
    props?: Object
) => {
    const dialogParamsWhenWrappedByWinforms: IBloomDialogEnvironmentParams = {
        omitOuterFrame: true,
        initiallyOpen: true
    };
    const p = {
        dialogEnvironment: dialogParamsWhenWrappedByWinforms,
        ...props
    };
    ReactDOM.render(
        React.createElement(knownComponents[reactComponentName], p, null),
        root
    );
};
