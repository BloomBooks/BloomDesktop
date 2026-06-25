import { css } from "@emotion/react";

import * as React from "react";
import { useEffect, useLayoutEffect, useRef, useState } from "react";
import CheckIcon from "@mui/icons-material/Check";

import { getAsync } from "../../utils/bloomApi";
import {
    kBloomBlue,
    kBloomBlue50Transparent,
    kBloomBuff,
    kMutedTextGray,
} from "../../bloomMaterialUITheme";
import { useL10n } from "../../react_components/l10nHooks";
import { ICopyrightAndLicenseData } from "./CopyrightAndLicenseDialog";
import { computePackageKey, getLicenseShorthand } from "./metadataReuseUtils";

// One distinct copyright/license/illustrator combination found among the book's images. We
// store only raw data here; the human-readable summary (which depends on localized labels) is
// computed at render time, where the labels are always current. (useL10n returns the English
// fallback synchronously and the localized string only on a later render, so a summary baked in
// during the once-on-mount gather effect would be stuck in English.)
interface IMetadataPackage {
    key: string;
    data: ICopyrightAndLicenseData;
    illustrator: string;
    // True for the book's own copyright/license, which has no illustrator and is labeled
    // "Copyright and license from this book" in place of the illustrator/photographer line.
    isFromThisBook?: boolean;
}

// Shown at the bottom of the Copyright and License dialog. It gathers candidate metadata
// (progressively, one request at a time) from some source — the other images in this book, or
// the other books in this collection — and offers the distinct results so the user can reuse
// one with a single click instead of retyping. This is an experiment: we deliberately do no
// caching, and the scan stops if the dialog closes.
export const MetadataChooser: React.FunctionComponent<{
    // The dialog's current copyright/license values. A package is shown as "chosen"
    // (check mark) when it matches these, so the current metadata is checked when the dialog
    // opens and the check mark follows whatever the user picks.
    currentData: ICopyrightAndLicenseData;
    onChoose: (data: ICopyrightAndLicenseData) => void;
    // The header shown above the list (already localized by the caller).
    headerText: string;
    // Endpoint returning a string[] of item ids to gather (image file names, or book folders).
    listEndpoint: string;
    // Given an item id, the endpoint that returns that item's ICopyrightAndLicenseData.
    getItemEndpoint: (id: string) => string;
    // When true (editing an image), also offer the book's own copyright/license — minus the
    // illustrator/photographer — since images usually share the book's copyright and license.
    alsoOfferBookMetadata?: boolean;
}> = (props) => {
    const [packages, setPackages] = useState<IMetadataPackage[]>([]);
    const [isGathering, setIsGathering] = useState(true);
    const currentKey = computePackageKey(props.currentData);

    // The package matching the item's existing metadata (the one shown checked when the dialog
    // opens) is pinned to the top of the list. We freeze the open-time key in a ref so the list
    // does not reshuffle as the user clicks different options afterward.
    const initialKeyRef = useRef(currentKey);

    // We never cap the list when there are three or fewer options (so three choices never
    // scroll). With four or more, we measure three full rows plus half of the fourth and cap to
    // that height, so a peek of the fourth option makes it obvious the list scrolls. Measuring
    // (rather than a fixed pixel height) keeps this correct across fonts and translations, where
    // row heights vary.
    const listRef = useRef<HTMLDivElement>(null);
    const [listMaxHeight, setListMaxHeight] = useState<number | undefined>(
        undefined,
    );
    useLayoutEffect(() => {
        const list = listRef.current;
        if (!list || packages.length <= 3) {
            setListMaxHeight(undefined);
            return;
        }
        const rows = list.children;
        const firstTop = rows[0].getBoundingClientRect().top;
        const fourthRect = rows[3].getBoundingClientRect();
        // Bottom of the third row is the top of the fourth; add half the fourth row's height so
        // half of it peeks below the cutoff.
        setListMaxHeight(
            Math.ceil(fourthRect.top + fourthRect.height / 2 - firstTop),
        );
    }, [packages.length]);

    // Localized labels for the rows. These are read at render time (below), NOT inside the
    // gather effect: useL10n delivers the localized value asynchronously, but the effect runs
    // only once on mount, so anything it captured would be stuck on the English fallback.
    const allRightsReservedLabel = useL10n(
        "All Rights Reserved",
        "License.AllRightsReserved",
    );
    const customLabel = useL10n("Custom", "License.Custom");
    const illustratorLabel = useL10n(
        "Illustrator/Photographer",
        "Copyright.IllustratorOrPhotographer",
    );
    const fromThisBookLabel = useL10n(
        "Copyright and license from this book",
        "CopyrightAndLicense.FromThisBook",
    );
    const gatheringLabel = useL10n("Gathering…", "Common.Gathering");

    useEffect(() => {
        // Set when the dialog unmounts so the scan stops instead of running on against a
        // collection/book that may have hundreds of items.
        const cancelled = { current: false };
        const seen = new Set<string>();

        // De-duplicate and append one candidate (ignoring ones too sparse to be worth offering).
        // isFromThisBook marks the book's own copyright/license (labeled specially at render).
        function addData(
            data: ICopyrightAndLicenseData | undefined,
            isFromThisBook?: boolean,
        ) {
            if (!data) return;
            const pkg = makePackage(data, isFromThisBook);
            if (!pkg) return; // not enough metadata to be worth offering
            if (seen.has(pkg.key)) return;
            seen.add(pkg.key);
            setPackages((previous) => [...previous, pkg]);
        }

        async function gather() {
            // Offer the book's own copyright/license first (when editing an image), minus the
            // illustrator/photographer.
            if (props.alsoOfferBookMetadata) {
                try {
                    const bookResponse = await getAsync(
                        "copyrightAndLicense/bookCopyrightAndLicense",
                    );
                    if (!cancelled.current && bookResponse.data) {
                        const bookData =
                            bookResponse.data as ICopyrightAndLicenseData;
                        addData(
                            {
                                copyrightInfo: {
                                    ...bookData.copyrightInfo,
                                    imageCreator: "",
                                },
                                licenseInfo: bookData.licenseInfo,
                            },
                            true, // isFromThisBook
                        );
                    }
                } catch (error) {
                    console.warn(
                        "MetadataChooser: could not read the book's metadata",
                        error,
                    );
                }
            }
            if (cancelled.current) return;

            const namesResponse = await getAsync(props.listEndpoint);
            const ids: string[] = namesResponse.data || [];
            for (const id of ids) {
                if (cancelled.current) return;

                let data: ICopyrightAndLicenseData | undefined;
                try {
                    const response = await getAsync(props.getItemEndpoint(id));
                    // The endpoint replies with an empty string for a missing/unreadable item.
                    if (response.data) data = response.data;
                } catch (error) {
                    // Never let a single bad item abort (or error out of) gathering: just skip
                    // it. The backend already turns read failures into an empty reply; this also
                    // covers an unexpected network/parse failure.
                    console.warn(
                        `MetadataChooser: skipping "${id}" (could not read its metadata)`,
                        error,
                    );
                }
                if (cancelled.current) return;
                addData(data);
            }
        }

        gather().finally(() => {
            if (!cancelled.current) setIsGathering(false);
        });

        return () => {
            cancelled.current = true;
        };
        // We intentionally run this once on mount; the props are stable for the dialog's life.
        // (Localized labels are deliberately NOT used here — see the labels comment above.)
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    function handleChoose(pkg: IMetadataPackage) {
        // Filling the dialog's fields updates currentData, which makes this package's
        // key match currentKey, so the check mark moves here automatically.
        props.onChoose(pkg.data);
    }

    // If we finished scanning the book and found no other image with reusable copyright/
    // license metadata, show no chooser UI at all (no header, no separator line).
    if (!isGathering && packages.length === 0) return null;

    // While still scanning and nothing has turned up yet, show only a minimal progress hint.
    // We hold off on the header and separator line until there is something to offer, so that
    // if the scan ends up empty nothing flashes into view and back out.
    if (packages.length === 0) {
        return (
            <div
                css={css`
                    margin-top: 10px;
                    font-style: italic;
                    color: ${kMutedTextGray};
                `}
            >
                {gatheringLabel}
            </div>
        );
    }

    // Pin the open-time match (see initialKeyRef) to the top, keeping discovery order otherwise.
    // The match may be appended at any point during the asynchronous scan, so we reorder here at
    // render time rather than when building the list.
    const orderedPackages = (() => {
        const index = packages.findIndex(
            (pkg) => pkg.key === initialKeyRef.current,
        );
        if (index <= 0) return packages;
        const reordered = [...packages];
        const [match] = reordered.splice(index, 1);
        return [match, ...reordered];
    })();

    return (
        <div
            css={css`
                // The enclosing group hugs the bottom of the dialog page; sit just below the
                // "copy to all" button.
                display: flex;
                flex-direction: column;
                align-self: stretch;
            `}
        >
            <div
                css={css`
                    font-size: 0.8em;
                    color: ${kMutedTextGray};
                    margin-bottom: 5px;
                `}
            >
                {props.headerText}
            </div>
            <div
                ref={listRef}
                style={
                    listMaxHeight === undefined
                        ? undefined
                        : { maxHeight: `${listMaxHeight}px` }
                }
                css={css`
                    display: flex;
                    flex-direction: column;
                    gap: 4px;
                    // Height is capped to three and a half rows (see listMaxHeight) only when
                    // there are more than three options, so three choices never scroll and a
                    // fourth peeks in to show the list scrolls.
                    overflow-y: auto;
                `}
            >
                {orderedPackages.map((pkg) => {
                    const isChosen = pkg.key === currentKey;
                    // Computed here (not when gathering) so the labels are always the current,
                    // localized ones rather than the English fallbacks present at mount.
                    const summary = getSummary(
                        pkg.data,
                        allRightsReservedLabel,
                        customLabel,
                    );
                    return (
                        <div
                            key={pkg.key}
                            onClick={() => handleChoose(pkg)}
                            css={css`
                                display: flex;
                                align-items: center;
                                cursor: pointer;
                                padding: 6px 8px;
                                border-radius: 4px;
                                // Each option looks like a clickable card: a visible border and
                                // background that lifts on hover, and a bloom-blue highlight when
                                // it is the chosen one.
                                border: 1px solid
                                    ${isChosen ? kBloomBlue : kBloomBuff};
                                background-color: ${isChosen
                                    ? kBloomBlue50Transparent
                                    : "white"};
                                transition:
                                    background-color 0.1s,
                                    border-color 0.1s;
                                &:hover {
                                    border-color: ${kBloomBlue};
                                    background-color: ${isChosen
                                        ? kBloomBlue50Transparent
                                        : "rgba(29, 148, 164, 0.08)"};
                                }
                            `}
                        >
                            <CheckIcon
                                css={css`
                                    color: ${kBloomBlue};
                                    margin-right: 6px;
                                    visibility: ${isChosen
                                        ? "visible"
                                        : "hidden"};
                                `}
                            />
                            <div
                                css={css`
                                    display: flex;
                                    flex-direction: column;
                                `}
                            >
                                <div>{summary}</div>
                                {pkg.isFromThisBook ? (
                                    <div
                                        css={css`
                                            font-size: 0.8em;
                                            color: ${kMutedTextGray};
                                        `}
                                    >
                                        {fromThisBookLabel}
                                    </div>
                                ) : (
                                    pkg.illustrator && (
                                        <div
                                            css={css`
                                                font-size: 0.8em;
                                                color: ${kMutedTextGray};
                                            `}
                                        >
                                            {illustratorLabel}:{" "}
                                            {pkg.illustrator}
                                        </div>
                                    )
                                )}
                            </div>
                        </div>
                    );
                })}
            </div>
        </div>
    );
};

// Builds a package from raw metadata, or returns undefined if there's not enough information
// (no copyright holder and no illustrator) to be worth offering. No localized labels here: the
// display summary is built at render time by getSummary so it tracks the current language.
function makePackage(
    data: ICopyrightAndLicenseData,
    isFromThisBook?: boolean,
): IMetadataPackage | undefined {
    const holder = (data.copyrightInfo.copyrightHolder || "").trim();
    const illustrator = (data.copyrightInfo.imageCreator || "").trim();
    if (!holder && !illustrator) return undefined;

    return { key: computePackageKey(data), data, illustrator, isFromThisBook };
}

// Builds the first row of a package: copyright (© year holder) and the license shorthand,
// joined by a middot. The illustrator is shown separately, so it's intentionally omitted here.
// Takes the localized labels as arguments so it can be called at render time with current values.
function getSummary(
    data: ICopyrightAndLicenseData,
    allRightsReservedLabel: string,
    customLabel: string,
): string {
    const holder = (data.copyrightInfo.copyrightHolder || "").trim();
    const year = (data.copyrightInfo.copyrightYear || "").trim();
    const license = getLicenseShorthand(
        data.licenseInfo,
        allRightsReservedLabel,
        customLabel,
    );
    let copyright = "";
    if (holder) copyright = year ? `© ${year} ${holder}` : `© ${holder}`;
    return [copyright, license].filter((s) => !!s).join(" · ");
}
