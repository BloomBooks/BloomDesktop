// Must match the TeamCollectionStatus enum in TeamCollectionMessageLog.cs
export type TeamCollectionStatus =
    | "None"
    | "Nominal"
    | "NewStuff"
    | "Error"
    | "ClobberPending"
    | "Disconnected";
