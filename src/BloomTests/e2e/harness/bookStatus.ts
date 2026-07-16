// Small, shared `teamCollection/bookStatus` + "force an immediate poll" helpers, factored out of
// E2E-2 (the two-instance collaboration loop) once E2E-3 needed the exact same pair. Kept here
// rather than duplicated per-spec so a future change to either shape only needs one edit.
import { expect } from "@playwright/test";
import { postApi, getApi } from "./bloomApi";

/** The subset of the `teamCollection/bookStatus` payload (see IBookTeamCollectionStatus in
 * TeamCollectionApi.tsx / GetBookStatusJson in TeamCollectionApi.cs) that the e2e specs consume. */
export interface BookTeamCollectionStatus {
    who: string;
    where: string;
    currentUser: string;
    isChangedRemotely: boolean;
}

export const bookStatus = async (
    httpPort: number,
    folderName: string,
): Promise<BookTeamCollectionStatus> => {
    const response = await getApi(
        httpPort,
        `teamCollection/bookStatus?folderName=${encodeURIComponent(folderName)}`,
    );
    expect(response.status).toBe(200);
    return (await response.json()) as BookTeamCollectionStatus;
};

// CloudCollectionMonitor only polls the server every 60s by default
// (CloudCollectionMonitor.DefaultPollInterval) — an instance sitting idle would take up to a
// minute to notice a remote change organically. `teamCollection/receiveUpdates` internally calls
// `CloudTeamCollection.PollNow()` before doing anything else, so calling it is the harness's way
// of forcing an immediate poll instead of waiting out the timer.
export const pollNowViaReceiveUpdates = async (httpPort: number) => {
    const response = await postApi(
        httpPort,
        "teamCollection/receiveUpdates",
        "{}",
    );
    expect(response.status).toBe(200);
};
