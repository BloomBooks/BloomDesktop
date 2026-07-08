// Selects a book via the direct `external/select-book` API instead of a CDP click.
//
// DISCOVERED (E2E-3): a fresh `connectOverCdp` against an instance that joined a cloud
// collection via pullDown + kill + relaunch (Bob's pattern in twoInstanceSetup.ts) reliably finds
// only a stuck `about:blank` CDP target -- confirmed via the raw `/json/list` endpoint showing
// exactly one target, that URL, unchanging for the full 120s retry window, even though the same
// instance's HTTP API (`teamCollection/capabilities`, etc.) was already responding correctly
// (i.e. the instance itself is fully initialized; this is specifically a CDP-attach problem, not
// a slow-startup one). This is the same class of problem as README.md's "ReactDialog-hosted
// WebView2 controls are not CDP-reachable" finding (only one WebView2 control's content can ever
// be visible on the single shared `--remote-debugging-port` at a time) -- Alice's page stays
// reliably attached only because it connects ONCE, before any dialog/reload churn, and is never
// reconnected afterward. Bob's instance never gets that same early, held-open connection in these
// scenarios, so a *fresh* connect after his relaunch hits the same problem createCloudTeamCollection's
// reopen causes for a fresh post-hoc connect.
//
// Workaround: `external/select-book` (src/BloomExe/web/controllers/ExternalApi.cs) does exactly
// what a real book-tile click does (`_collectionModel.SelectBook`) without needing any WebView2 at
// all, given the book's id (`BookInfo.Id`, JSON property `bookInstanceId` in the book's meta.json).
// This sidesteps the CDP-attach problem entirely for any instance that doesn't already have a page
// held open from before its own launch/reopen churn.
import * as fs from "node:fs/promises";
import * as path from "node:path";
import { expect } from "@playwright/test";
import { postApi } from "./bloomApi";

/** Reads `bookInstanceId` out of `<collectionFolder>/<bookName>/meta.json`. */
export const readBookInstanceId = async (
    collectionFolder: string,
    bookName: string,
): Promise<string> => {
    const metaPath = path.join(collectionFolder, bookName, "meta.json");
    const meta = JSON.parse(await fs.readFile(metaPath, "utf8"));
    if (!meta.bookInstanceId) {
        throw new Error(`${metaPath} has no bookInstanceId.`);
    }
    return meta.bookInstanceId as string;
};

/** Makes `bookName` (identified by its `bookInstanceId`, read from `collectionFolder`'s copy of
 * its meta.json -- any instance's copy works, since cloud-collection books share one server-side
 * id) the current selection on the instance at `httpPort`, via `external/select-book` rather than
 * a CDP click. Requires the Collection tab to be the active tab (true by default on launch). */
export const selectBookByName = async (
    httpPort: number,
    collectionFolder: string,
    bookName: string,
): Promise<void> => {
    const id = await readBookInstanceId(collectionFolder, bookName);
    const response = await postApi(
        httpPort,
        "external/select-book",
        JSON.stringify({ id }),
    );
    expect(response.status).toBe(200);
};
