import { css } from "@emotion/react";
import { ImageGallery } from "bloom-image-gallery";
import type {
    IImage,
    IProviderKeysV1,
    ISearchReport,
} from "bloom-image-gallery";
import React, { useEffect, useRef, useState } from "react";
import {
    BloomDialog,
    DialogTitle,
} from "../../react_components/BloomDialog/BloomDialog";
import {
    getBloomApiPrefix,
    getAsync,
    postJsonAsync,
    postDataWithConfigAsync,
    trackEvent,
} from "../../utils/bloomApi";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import BloomMessageBoxSupport from "../../utils/bloomMessageBoxSupport";
import { getEditablePageBundleExports } from "../../bookEdit/js/workspaceFrames";
import { useMountEffect } from "../../utils/useMountEffect";
import { ShowEditViewDialog } from "../../bookEdit/workspaceRoot";

// The shape of what C# returns from imageGallery/imageGalleryResult
interface IImageGalleryApiResult {
    src: string;
    copyright: string;
    creator: string;
    license: string;
}

// The provider id the gallery stamps on an image the user opened from their own disk.
const kLocalDiskProviderId = "local-disk";

const ImageGalleryDialog: React.FunctionComponent<{
    img: HTMLElement;
    searchLang: string;
}> = (props) => {
    const [open, setOpen] = useState(true);
    // Keys are loaded from durable Bloom settings before the gallery is rendered,
    // so providers (e.g. Pixabay) receive their initial API key in their constructor.
    const [providerKeys, setProviderKeys] = useState<
        IProviderKeysV1 | undefined
    >(undefined);
    const [keysLoaded, setKeysLoaded] = useState(false);

    // ----- Analytics for this visit to the chooser (BL-16716) -----
    // The unit of analysis is the visit, not the query: people usually run several searches
    // before accepting an image, and a search's outcome is only known later. So a visit
    // accumulates what it did in the refs below and reports it all in one event when it closes.
    // Refs, not state: they are written from callbacks, read at close time, and must never cause
    // a re-render of the gallery.
    const searchCountRef = useRef(0);
    // In the order the user tried them, which is more telling than the bare count.
    const providersTriedRef = useRef(new Set<string>());
    // Pixabay is the one source a user cannot simply use -- it needs an API key fetched from
    // Pixabay's site -- so whether this session had one is the headline number for how much of
    // an obstacle that is.
    const pixabayKeyPresentRef = useRef(false);
    // How many times this user had opened the chooser before now. A key (or an accept) on the
    // first visit is a speed bump; one that takes eight visits is a real barrier.
    const priorChooserSessionsRef = useRef<number | undefined>(undefined);
    // Exactly one "Image Chooser Closed" event per dialog session.
    const closeReportedRef = useRef(false);

    // useEffect justified: this is a one-time async fetch that must run after mount
    // so the component can render before the network round-trip completes.
    // There are no dependencies to react to; [] is correct.
    useEffect(() => {
        getAsync("app/userSetting?settingName=ImageGalleryProviderKeys")
            .then((r) => {
                const json = r?.data?.settingValue as string;
                if (json) {
                    try {
                        const keys = JSON.parse(json) as IProviderKeysV1;
                        setProviderKeys(keys);
                        pixabayKeyPresentRef.current = !!keys.pixabay;
                    } catch {
                        // ignore malformed stored value
                    }
                }
            })
            .finally(() => setKeysLoaded(true));
    }, []);

    // Unlike the fetch above, this one WRITES, so it has to happen exactly once per dialog.
    // React StrictMode runs mount effects twice in development, which would bump the durable
    // counter by two on every visit -- skewing the very number this exists to provide, and
    // doing it precisely when a developer is checking the feature.
    const sessionCountedRef = useRef(false);
    useMountEffect(() => {
        if (sessionCountedRef.current) return;
        sessionCountedRef.current = true;
        getAsync("app/userSetting?settingName=ImageChooserSessionCount").then(
            (r) => {
                const priorSessions = (r?.data?.settingValue as number) ?? 0;
                priorChooserSessionsRef.current = priorSessions;
                postJsonAsync("app/userSetting", {
                    settingName: "ImageChooserSessionCount",
                    settingValue: priorSessions + 1,
                });
            },
        );
    });

    // Searches are counted, not reported one by one: how many a visit took and which sources it
    // tried are what the close event needs, and a per-query event adds nothing on top of them.
    //
    // WE DO NOT SEND THE SEARCH TERM. It is the only free-form user text this instrumentation ever
    // had, an earlier round of BL-16716 did send it, and we were then told not to. It is dropped
    // here, at Bloom's boundary, deliberately rather than by omission -- `report.term` is still
    // handed to us by the image gallery (see ISearchReport), because if the decision is revisited
    // the change is one property and nothing in the gallery has to move.
    //
    // What that costs us: we cannot see WHICH subjects people search for and never find, which
    // was the original argument for the term; and we cannot tell one idea tried in three
    // languages from three different ideas, so searchCount counts queries and nothing finer.
    const handleSearch = (report: ISearchReport) => {
        searchCountRef.current++;
        providersTriedRef.current.add(report.providerId);
    };

    // What turns a list of searches into a success rate. Note that a result count could never
    // do this job: Pixabay and Openverse nearly always return *some* pictures, so the failure
    // we care about isn't an empty page, it's a full page of pictures that are all wrong -- and
    // the only reliable sign of that is what the user did next.
    const reportChooserClosed = (
        outcome: "accepted" | "local disk" | "cancelled",
        acceptedProvider?: string,
    ) => {
        if (closeReportedRef.current) return;
        closeReportedRef.current = true;
        trackEvent("Image Chooser Closed", {
            outcome,
            acceptedProvider,
            // No acceptedTerm: see handleSearch above. Which SOURCE satisfied the user is the part
            // that makes this a success rate, and that is not user text.
            searchCount: searchCountRef.current,
            providersTried: providersTriedRef.current.size,
            providers: [...providersTriedRef.current].join(","),
            pixabayKeyPresent: pixabayKeyPresentRef.current,
            priorChooserSessions: priorChooserSessionsRef.current,
        });
    };

    const handleClose = () => {
        reportChooserClosed("cancelled");
        setOpen(false);
    };

    const onConfirmSelection = async (image: IImage) => {
        const exports = getEditablePageBundleExports();
        exports?.addRequestPageContentDelay("imageGalleryConfirm");
        try {
            const payload = {
                imageUrl: image.url ?? image.reasonableSizeUrl,
                localPath: image.localPath,
                license: image.license,
                licenseUrl: image.licenseUrl,
                credits: image.credits,
                creator: image.creator,
                // Which source the picture came from, so C#'s "Change Picture" event can say
                // where pictures actually come from rather than only how many there were.
                provider: image.providerId,
            };
            const response = await postJsonAsync(
                "imageGallery/imageGalleryResult",
                payload,
            );
            const result = response!.data as IImageGalleryApiResult;
            exports?.changeImageByElement(props.img, {
                src: result.src,
                copyright: result.copyright,
                creator: result.creator,
                license: result.license,
                undoable: "true",
            });
            // A file the user opened from disk is a different outcome from finding a picture in
            // one of the collections we offer, even though both end in an image being used.
            reportChooserClosed(
                image.providerId === kLocalDiskProviderId
                    ? "local disk"
                    : "accepted",
                image.providerId,
            );
            setOpen(false);
        } catch {
            BloomMessageBoxSupport.CreateAndShowSimpleMessageBox(
                "ImageLibrary.FailedToAddImage",
                "Sorry, there was a problem adding the image.",
                "",
            );
        } finally {
            exports?.removeRequestPageContentDelay("imageGalleryConfirm");
        }
    };

    const onPickLocalFile = async (): Promise<IImage | undefined> => {
        const response = await postJsonAsync(
            "imageGallery/pickLocalImageFile",
            {},
        );
        if (!response) return undefined;
        const { filePath, previewUrl, width, height, size } = response.data as {
            filePath: string;
            previewUrl: string;
            width: number;
            height: number;
            size: number;
        };
        if (!filePath) return undefined;
        return {
            thumbnailUrl: previewUrl,
            reasonableSizeUrl: previewUrl,
            localPath: filePath,
            // Set here as well as by the gallery, so that the "local disk" outcome and the
            // Change Picture provider do not silently depend on the package continuing to stamp
            // it. If it ever stopped, every disk-opened picture would be recorded as an ordinary
            // chooser accept from an unknown provider -- the exact split this event exists for.
            providerId: kLocalDiskProviderId,
            // C# reports the original file's dimensions and byte count. These matter because
            // previewUrl may serve a downscaled stand-in for an image too large for the
            // browser to display, and we want to report what the user actually chose.
            width: width || undefined,
            height: height || undefined,
            size: size ?? 0,
            type: "image",
        };
    };

    const localCollectionsBaseUrl = getBloomApiPrefix() + "imageGallery";

    return (
        <BloomDialog
            open={open}
            onClose={handleClose}
            onCancel={handleClose}
            maxWidth={false}
            disableDragging={true}
            css={css`
                .MuiDialog-paper {
                    width: min(92vw, 1300px);
                    height: min(88vh, 860px);
                    max-width: none;
                }
            `}
        >
            <DialogTitle title="Image Chooser" />
            <div
                css={css`
                    flex: 1;
                    min-height: 0;
                    overflow: hidden;
                    /* Counteract most of BloomDialog's side padding so the gallery
                       sits inset ~8px from the dialog edge; the gallery's own 20px
                       padding then provides the internal margins. */
                    margin-left: -16px;
                    margin-right: -16px;
                    margin-bottom: -10px;
                    /* bloom-image-gallery's content div uses height:100% + padding:20px
                       with box-sizing:content-box, making its total height 40px taller
                       than its parent and pushing the OK/Cancel buttons below the clipping
                       boundary. Switching to border-box makes height:100% include the
                       padding, so buttons stay visible even when the dialog is shrunk. */
                    main > div {
                        box-sizing: border-box;
                    }
                `}
            >
                {keysLoaded && (
                    <ImageGallery
                        onConfirmSelection={onConfirmSelection}
                        onPickLocalFile={onPickLocalFile}
                        onCancel={handleClose}
                        localCollectionsBaseUrl={localCollectionsBaseUrl}
                        lang={props.searchLang}
                        initialProviderKeys={providerKeys}
                        primaryColor={kBloomBlue}
                        onSearch={handleSearch}
                        onProviderKeysChange={(keys) => {
                            // Kept up to date mid-visit as well as read at startup, so that a
                            // key supplied while the chooser is open is reflected in what this
                            // visit reports.
                            pixabayKeyPresentRef.current = !!keys.pixabay;
                            postJsonAsync("app/userSetting", {
                                settingName: "ImageGalleryProviderKeys",
                                settingValue: JSON.stringify(keys),
                            });
                        }}
                        onLanguageChange={(lang) =>
                            postJsonAsync("app/userSetting", {
                                settingName: "ImageSearchLanguage",
                                settingValue: lang,
                            })
                        }
                        getLocalizations={async (strings) => {
                            // i18n/loadStrings expects form-encoded data, not JSON
                            const params = new URLSearchParams();
                            for (const [key, value] of Object.entries(
                                strings,
                            )) {
                                params.append(key, value);
                            }
                            const response = await postDataWithConfigAsync(
                                "i18n/loadStrings",
                                params,
                                {},
                            );
                            return (response?.data ?? strings) as Record<
                                string,
                                string
                            >;
                        }}
                    />
                )}
            </div>
        </BloomDialog>
    );
};

/** Show the image gallery dialog for the given image element. */
export function showImageGalleryDialog(
    img: HTMLElement,
    searchLang: string,
): void {
    ShowEditViewDialog(
        <ImageGalleryDialog img={img} searchLang={searchLang} />,
    );
}
