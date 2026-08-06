import { beforeEach, describe, expect, test, vi } from "vitest";

import {
    kBackgroundImageClass,
    kCanvasElementClass,
} from "../../toolbox/canvas/canvasElementConstants";

// CanvasElementClipboard pulls in the whole editing/games/toolbox world at module load
// time. We only care about how it updates the DOM after an image arrives from the
// clipboard, so stub the collaborators out. SetupMetadataButton is the one we assert on:
// this suite exists because the Ctrl+V path used to skip it (BL-16605).
vi.mock("../bloomImages", () => ({
    kImageContainerClass: "bloom-imageContainer",
    // Mirror the real helper's semantics (bloomImages.ts): case-insensitive, and it wants the
    // whole "placeholder.png", not just the stem. The empty-canvas branch now leans on this
    // predicate, so a looser stub here would let the tests pass on behavior we don't ship.
    isPlaceHolderImage: (src: string | null) =>
        !!src && src.toLowerCase().includes("placeholder.png"),
    SetupMetadataButton: vi.fn(),
}));

vi.mock("../bloomEditing", () => ({
    kMakeNewCanvasElement: "makeNewCanvasElement",
    // Just the part of the real changeImageInfo that this suite depends on: the new src and
    // metadata attributes land on the img synchronously, before the button is rebuilt.
    changeImageInfo: vi.fn(
        (img: HTMLElement, info: { src: string; copyright: string }) => {
            img.setAttribute("src", info.src);
            img.setAttribute("data-copyright", info.copyright);
        },
    ),
    notifyToolOfChangedImage: vi.fn(),
    wrapWithRequestPageContentDelay: vi.fn(),
}));

vi.mock("../../toolbox/games/GameTool", () => ({
    adjustTarget: vi.fn(),
    // Not the play tab, so pasting is allowed, and not a start/correct/wrong tab either.
    getActiveGameTab: () => -1,
    startTabIndex: 0,
    correctTabIndex: 1,
    wrongTabIndex: 2,
    playTabIndex: 3,
}));

vi.mock("bloom-player", () => ({ getTarget: vi.fn() }));
vi.mock("../../../utils/bloomApi", () => ({ postJson: vi.fn(), get: vi.fn() }));
vi.mock("../../../react_components/featureStatus", () => ({}));
vi.mock("../../../react_components/requiresSubscription", () => ({
    showRequiresSubscriptionDialogInEditView: vi.fn(),
}));
vi.mock("../../../utils/bloomMessageBoxSupport", () => ({
    default: {
        CreateAndShowSimpleMessageBoxWithLocalizedText: vi.fn(),
    },
}));
vi.mock("../../toolbox/canvas/CanvasElementItem", () => ({
    makeTargetAndMatchSize: vi.fn(),
}));

import { SetupMetadataButton } from "../bloomImages";
import {
    changeImageInfo,
    wrapWithRequestPageContentDelay,
} from "../bloomEditing";
import {
    CanvasElementClipboard,
    ICanvasElementClipboardHost,
} from "./CanvasElementClipboard";
import { CanvasSnapProvider } from "./CanvasSnapProvider";

const pastedImageInfo = {
    imageId: "makeNewCanvasElement",
    src: "pasted.png",
    copyright: "Copyright © 2026, Somebody",
    creator: "Somebody",
    license: "cc-by",
};

// Build a bloom-canvas holding one canvas element, whose image starts out as a placeholder.
// isBackground controls whether that canvas element is the background image or an overlay.
function makeCanvasWithPlaceholder(isBackground: boolean): {
    bloomCanvas: HTMLElement;
    canvasElement: HTMLElement;
    img: HTMLImageElement;
} {
    const bloomCanvas = document.createElement("div");
    bloomCanvas.classList.add("bloom-canvas");
    const canvasElement = document.createElement("div");
    canvasElement.classList.add(kCanvasElementClass);
    if (isBackground) {
        canvasElement.classList.add(kBackgroundImageClass);
    }
    canvasElement.innerHTML =
        '<div class="bloom-imageContainer"><img src="placeHolder.png" /></div>';
    bloomCanvas.appendChild(canvasElement);
    document.body.appendChild(bloomCanvas);
    return {
        bloomCanvas,
        canvasElement,
        img: canvasElement.getElementsByTagName("img")[0],
    };
}

// A host that records the geometry calls, so we can tell which branch ran.
function makeHost(
    bloomCanvas: HTMLElement,
    activeElement: HTMLElement | undefined,
) {
    return {
        getActiveOrFirstBloomCanvasOnPage: () => bloomCanvas,
        getActiveElement: () => activeElement,
        adjustBackgroundImageSize: vi.fn(),
        adjustContainerAspectRatio: vi.fn(),
        addPictureCanvasElement: vi.fn(),
        setDoAfterNewImageAdjusted: vi.fn(),
    } as unknown as ICanvasElementClipboardHost & {
        adjustBackgroundImageSize: ReturnType<typeof vi.fn>;
        adjustContainerAspectRatio: ReturnType<typeof vi.fn>;
    };
}

function makeClipboard(host: ICanvasElementClipboardHost) {
    return new CanvasElementClipboard(
        host,
        {} as unknown as CanvasSnapProvider,
        10,
        10,
    );
}

describe("CanvasElementClipboard paste refreshes the metadata button (BL-16605)", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
        // mockReset (not mockClear) so an implementation installed by one test cannot leak
        // into the next. Safe here because the mock factory gives SetupMetadataButton no
        // implementation of its own.
        vi.mocked(SetupMetadataButton).mockReset();
        // changeImageInfo must keep the implementation from its factory, so only clear calls.
        vi.mocked(changeImageInfo).mockClear();
    });

    test("pasting into an empty canvas rebuilds the background image's metadata button", () => {
        const { bloomCanvas, canvasElement, img } =
            makeCanvasWithPlaceholder(true);
        const host = makeHost(bloomCanvas, undefined);

        // Sanity checks: we start with a placeholder, no copyright, and no button calls yet.
        expect(img.getAttribute("src")).toBe("placeHolder.png");
        expect(img.hasAttribute("data-copyright")).toBe(false);
        expect(SetupMetadataButton).not.toHaveBeenCalled();

        makeClipboard(host).finishPasteImageFromClipboard(pastedImageInfo);

        expect(img.getAttribute("src")).toBe("pasted.png");
        expect(img.getAttribute("data-copyright")).toBe(
            pastedImageInfo.copyright,
        );
        expect(host.adjustBackgroundImageSize).toHaveBeenCalledTimes(1);
        expect(SetupMetadataButton).toHaveBeenCalledTimes(1);
        // It must be given the background canvas element itself, as
        // updateCanvasElementForChangedImage does.
        expect(vi.mocked(SetupMetadataButton).mock.calls[0][0]).toBe(
            canvasElement,
        );
    });

    test("the metadata button is rebuilt only after the new image info is in place", () => {
        const { bloomCanvas, img } = makeCanvasWithPlaceholder(true);
        const host = makeHost(bloomCanvas, undefined);

        // A stale button would show the old image's copyright, so capture what the img looked
        // like at the moment SetupMetadataButton was called.
        let srcWhenButtonBuilt: string | null = "not called";
        let copyrightWhenButtonBuilt: string | null = "not called";
        vi.mocked(SetupMetadataButton).mockImplementation(() => {
            srcWhenButtonBuilt = img.getAttribute("src");
            copyrightWhenButtonBuilt = img.getAttribute("data-copyright");
        });

        makeClipboard(host).finishPasteImageFromClipboard(pastedImageInfo);

        expect(srcWhenButtonBuilt).toBe("pasted.png");
        expect(copyrightWhenButtonBuilt).toBe(pastedImageInfo.copyright);
    });

    test("pasting into a selected overlay does not touch the metadata button", () => {
        // Two canvas elements, so we skip the empty-canvas branch and land on the
        // selected-element branch. Only background images get a metadata button, so there is
        // nothing to rebuild here.
        const { bloomCanvas } = makeCanvasWithPlaceholder(true);
        const background = bloomCanvas.getElementsByTagName("img")[0];
        background.setAttribute("src", "realBackground.png");
        const overlay = document.createElement("div");
        overlay.classList.add(kCanvasElementClass);
        overlay.innerHTML =
            '<div class="bloom-imageContainer"><img src="placeHolder.png" /></div>';
        bloomCanvas.appendChild(overlay);
        const overlayImg = overlay.getElementsByTagName("img")[0];
        const host = makeHost(bloomCanvas, overlay);

        expect(overlayImg.getAttribute("src")).toBe("placeHolder.png");

        makeClipboard(host).finishPasteImageFromClipboard(pastedImageInfo);

        expect(overlayImg.getAttribute("src")).toBe("pasted.png");
        expect(host.adjustContainerAspectRatio).toHaveBeenCalledTimes(1);
        expect(host.adjustBackgroundImageSize).not.toHaveBeenCalled();
        expect(SetupMetadataButton).not.toHaveBeenCalled();
    });
});

describe("CanvasElementClipboard only claims a placeholder background (BL-16542)", () => {
    beforeEach(() => {
        document.body.innerHTML = "";
        vi.mocked(SetupMetadataButton).mockReset();
        vi.mocked(changeImageInfo).mockClear();
        vi.mocked(wrapWithRequestPageContentDelay).mockClear();
    });

    test("a canvas whose only content is a placeholder background takes the pasted image as its background", () => {
        const { bloomCanvas, img } = makeCanvasWithPlaceholder(true);
        const host = makeHost(bloomCanvas, undefined);

        expect(img.getAttribute("src")).toBe("placeHolder.png");

        makeClipboard(host).finishPasteImageFromClipboard(pastedImageInfo);

        expect(img.getAttribute("src")).toBe("pasted.png");
        expect(host.adjustBackgroundImageSize).toHaveBeenCalledTimes(1);
        // We handled it here, so we never reached the add-a-new-element branch.
        expect(wrapWithRequestPageContentDelay).not.toHaveBeenCalled();
    });

    test("a background that already holds a real image is left alone; the paste becomes a new canvas element", () => {
        // Nothing is selected (the state right after the page is displayed), so the only way
        // this paste could replace the background is the empty-canvas branch. That branch must
        // not fire once the background holds a real image: replacing the picture the user can
        // see needs them to select it first. See BL-16542.
        const { bloomCanvas, img } = makeCanvasWithPlaceholder(true);
        img.setAttribute("src", "realBackground.png");
        const host = makeHost(bloomCanvas, undefined);

        // Sanity check: one canvas element, and it IS the background, so the only thing
        // keeping us out of that branch is the non-placeholder src.
        expect(
            bloomCanvas.getElementsByClassName(kCanvasElementClass).length,
        ).toBe(1);
        expect(
            bloomCanvas
                .getElementsByClassName(kCanvasElementClass)[0]
                .classList.contains(kBackgroundImageClass),
        ).toBe(true);

        makeClipboard(host).finishPasteImageFromClipboard(pastedImageInfo);

        expect(img.getAttribute("src")).toBe("realBackground.png");
        expect(changeImageInfo).not.toHaveBeenCalled();
        expect(host.adjustBackgroundImageSize).not.toHaveBeenCalled();
        expect(SetupMetadataButton).not.toHaveBeenCalled();
        // Instead we fell through to the branch that adds a new canvas element.
        expect(wrapWithRequestPageContentDelay).toHaveBeenCalledTimes(1);
    });

    test("a background whose src is missing counts as empty", () => {
        const { bloomCanvas, img } = makeCanvasWithPlaceholder(true);
        img.removeAttribute("src");
        const host = makeHost(bloomCanvas, undefined);

        makeClipboard(host).finishPasteImageFromClipboard(pastedImageInfo);

        expect(img.getAttribute("src")).toBe("pasted.png");
        expect(host.adjustBackgroundImageSize).toHaveBeenCalledTimes(1);
    });
});
