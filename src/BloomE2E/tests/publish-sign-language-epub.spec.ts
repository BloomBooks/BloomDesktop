// A Sign Language book published as an ePUB keeps its video: the ePUB screen's preview shows the
// video and can play it, and the ePUB saved to disk holds the video file, with a page in its reading
// order that plays it and a package document that lists it. BL-16051 was the regression in which
// the preview, and the ePUBs Bloom made, showed a blank space where the video should be.
//
// Automates "Publish SL To ePUB" (Test Case ID 595). The case's last step, opening the saved ePUB
// in a separate ePUB reader, is checked by reading the saved file the way a reader finds its way
// through one: from its container.xml to its package document, its spine and the page that holds
// the video. How the video looks in some particular reader stays a person's job.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { launchWithDontDisturb } from "../fixtures/launchBloom";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    kEnterpriseSubscriptionCode,
    readSavedCollectionSetting,
} from "../helpers/collectionSettings";
import {
    attribute,
    getEpubSpine,
    getPreviewVideoState,
    playPreviewVideo,
    previewEpub,
    readEpub,
    saveEpub,
    showEpubPreviewDocument,
} from "../helpers/epub";
import {
    chooseVideoFile,
    getVideoFileName,
    readVideoFile,
    SHORT_VIDEO,
    videoBoxes,
} from "../helpers/videos";

test.use({
    collectionSpec: {
        name: "publish-sign-language-epub",
        languages: ["en"],
        signLanguage: "ase",
        // An ePUB carries a book's videos only at the Pro tier or above; below it the ePUB screen
        // shows an upgrade notice instead of a preview.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/**
 * Make a book with a page that holds a sign language video, in the collection's sign language, and
 * return the book's folder and the name of its video file.
 */
async function makeSignLanguageBook(
    page: Page,
    collectionDir: string,
): Promise<{ bookFolder: string; videoFile: string }> {
    expect(
        readSavedCollectionSetting(collectionDir, "SignLanguageIso639Code"),
        "sanity check: the collection has a sign language",
    ).toBe("ase");
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    const before = (await getContentPages(page)).map((p) => p.id);
    await addPage(page, "Video Over Text");
    const videoPage = (await getContentPages(page)).find(
        (p) => !before.includes(p.id),
    )!;
    await goToPage(page, videoPage.id);
    await chooseVideoFile(page, videoBoxes(page).first(), SHORT_VIDEO);
    const videoFile = await getVideoFileName(videoBoxes(page).first());
    expect(videoFile, "sanity check: the box shows a video").not.toBe("");
    return { bookFolder, videoFile };
}

/** The text files of a staged ePUB folder, by their path within it, for getEpubSpine. */
function readFolderTexts(folder: string): Map<string, string> {
    const texts = new Map<string, string>();
    for (const entry of fs.readdirSync(folder, {
        recursive: true,
        withFileTypes: true,
    })) {
        if (!entry.isFile() || !/\.(xml|opf|xhtml)$/.test(entry.name)) continue;
        const full = Path.join(entry.parentPath, entry.name);
        texts.set(
            Path.relative(folder, full).split(Path.sep).join("/"),
            fs.readFileSync(full, "utf8"),
        );
    }
    return texts;
}

/** The path, from the ePUB's root, of `href` as written in the document at `fromPath`. */
function resolveInEpub(fromPath: string, href: string): string {
    return Path.posix.normalize(
        Path.posix.join(Path.posix.dirname(fromPath), decodeURIComponent(href)),
    );
}

test("a Sign Language book's video shows and plays in the ePUB preview [Test Case ID 595]", async ({
    page,
    bloomApp,
}) => {
    test.setTimeout(300000);
    const { videoFile } = await makeSignLanguageBook(
        page,
        bloomApp.collectionDir,
    );

    const preview = await previewEpub(page);
    const staged = readFolderTexts(preview.stagingFolder);
    const { packagePath, hrefs } = getEpubSpine(staged);
    const videoDocument = hrefs.find((href) =>
        staged
            .get(resolveInEpub(packagePath, href))!
            .includes(`video/${videoFile}`),
    );
    expect(
        videoDocument,
        `No page of the staged ePUB plays video/${videoFile}.`,
    ).toBeDefined();

    const pageFrame = await showEpubPreviewDocument(
        preview,
        hrefs,
        videoDocument!,
    );
    // SHORT_VIDEO is 160x120.
    await expect
        .poll(() => getPreviewVideoState(pageFrame), {
            message: "The preview's video did not load.",
        })
        .toEqual({
            fileName: videoFile,
            controls: true,
            hasFrame: true,
            videoWidth: 160,
            videoHeight: 120,
            errorCode: null,
        });
    await playPreviewVideo(pageFrame);
});

test("a Sign Language book saved as an ePUB carries its video, on a page that plays it [Test Case ID 595]", async ({
    page,
    bloomApp,
}, testInfo) => {
    // Save... asks for the file in the native Save As dialog, which Windows activates whatever
    // --dont-disturb says, so this would take the developer's keyboard. It runs where Bloom
    // behaves as it does for a user: on CI, or with BLOOM_E2E_DONT_DISTURB=0.
    test.skip(
        launchWithDontDisturb(),
        "The Save As dialog would take the developer's keyboard; set BLOOM_E2E_DONT_DISTURB=0 to run it.",
    );
    test.setTimeout(300000);
    const { bookFolder, videoFile } = await makeSignLanguageBook(
        page,
        bloomApp.collectionDir,
    );
    const videoBytes = readVideoFile(bookFolder, videoFile);

    const epubPath = testInfo.outputPath("Sign Language book.epub");
    fs.mkdirSync(Path.dirname(epubPath), { recursive: true });
    const files = readEpub(await saveEpub(page, bloomApp.bloomPid, epubPath));

    // What every ePUB reader checks first.
    const names = [...files.keys()];
    expect(names[0]).toBe("mimetype");
    expect(files.get("mimetype")!.content.toString("ascii")).toBe(
        "application/epub+zip",
    );

    const texts = new Map<string, string>();
    for (const [name, entry] of files) {
        if (/\.(xml|opf|xhtml)$/.test(name))
            texts.set(name, entry.content.toString("utf8"));
    }
    const { packagePath, hrefs } = getEpubSpine(texts);
    const videoPath = resolveInEpub(packagePath, `video/${videoFile}`);
    expect(
        files.get(videoPath)?.content.equals(videoBytes),
        `The saved ePUB should hold the book's video, unchanged, at ${videoPath}; it has ${names.join(", ")}.`,
    ).toBe(true);

    // The package document lists the video as a resource of the ePUB, which a reader needs before
    // it will load it. (Its media type is left alone: Bloom calls an mp4 "audio/mp4" on purpose,
    // EpubMaker.GetMediaType.)
    const listed = (
        texts.get(packagePath)!.match(/<item\s[^>]*>/g) ?? []
    ).filter(
        (tag) =>
            resolveInEpub(packagePath, attribute(tag, "href") ?? "") ===
            videoPath,
    );
    expect(
        listed,
        "The package document should list the video once.",
    ).toHaveLength(1);

    // One page in the reading order plays it, with controls to start it.
    const videoPages = hrefs.filter((href) => {
        const path = resolveInEpub(packagePath, href);
        return Array.from(
            texts.get(path)!.matchAll(/<video\s[^>]*>[\s\S]*?<\/video>/g),
        ).some(
            ([video]) =>
                /\scontrols=/.test(video.match(/<video\s[^>]*>/)![0]) &&
                Array.from(video.matchAll(/<source\s[^>]*>/g)).some(
                    ([source]) =>
                        resolveInEpub(path, attribute(source, "src") ?? "") ===
                        videoPath,
                ),
        );
    });
    expect(videoPages).toHaveLength(1);
});
