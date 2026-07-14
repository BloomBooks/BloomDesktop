import { bootstrapReactComponent } from "../utils/entryPointBootstrap";
import { CreateTeamCollectionBundleDispatcher } from "./CreateTeamCollection";

// Importing CreateTeamCollection.tsx already registers window.wireUpRootComponentFromWinforms
// (the dispatcher's own WireUpForWinforms call), which bootstrapReactComponent prefers when
// present -- see its own comment. Passing the dispatcher here too keeps the plain-Vite path
// (no `wireUpRootComponentFromWinforms`, e.g. a future non-WinForms host) selecting the same
// component instead of always defaulting to the folder dialog.
bootstrapReactComponent(CreateTeamCollectionBundleDispatcher);
