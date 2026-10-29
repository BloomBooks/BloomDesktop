// LEGACY: only for the WinForms Collection Settings dialog, which is deleted at cutover (BL-17042).
import { bootstrapReactComponent } from "../utils/entryPointBootstrap";
import { LegacyCreateTeamCollectionDialog } from "./CreateTeamCollection";

bootstrapReactComponent(LegacyCreateTeamCollectionDialog);
