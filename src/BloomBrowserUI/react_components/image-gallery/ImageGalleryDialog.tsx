import { css } from "@emotion/react";
import { ImageGallery } from "bloom-image-gallery";
import type { IImage, IProviderKeysV1 } from "bloom-image-gallery";
import React, { useEffect, useState } from "react";
import {
    BloomDialog,
    DialogTitle,
} from "../../react_components/BloomDialog/BloomDialog";
import {
    getBloomApiPrefix,
    getAsync,
    postJsonAsync,
    postDataWithConfigAsync,
} from "../../utils/bloomApi";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import BloomMessageBoxSupport from "../../utils/bloomMessageBoxSupport";
import { getEditablePageBundleExports } from "../../bookEdit/js/workspaceFrames";
import { ShowEditViewDialog } from "../../bookEdit/workspaceRoot";

// The shape of what C# returns from editView/imageGalleryResult
interface IImageGalleryApiResult {
    src: string;
    copyright: string;
    creator: string;
    license: string;
}

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

    // useEffect justified: this is a one-time async fetch that must run after mount
    // so the component can render before the network round-trip completes.
    // There are no dependencies to react to; [] is correct.
    useEffect(() => {
        getAsync("app/userSetting?settingName=ImageGalleryProviderKeys")
            .then((r) => {
                const json = r?.data?.settingValue as string;
                if (json) {
                    try {
                        setProviderKeys(JSON.parse(json) as IProviderKeysV1);
                    } catch {
                        // ignore malformed stored value
                    }
                }
            })
            .finally(() => setKeysLoaded(true));
    }, []);

    const handleClose = () => {
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
            };
            const response = await postJsonAsync(
                "editView/imageGalleryResult",
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
        const response = await postJsonAsync("editView/pickLocalImageFile", {});
        if (!response) return undefined;
        const { filePath, previewUrl } = response.data as {
            filePath: string;
            previewUrl: string;
        };
        if (!filePath) return undefined;
        return {
            thumbnailUrl: previewUrl,
            reasonableSizeUrl: previewUrl,
            localPath: filePath,
            size: 0,
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
                    /* Counteract BloomDialog's side and bottom padding so the gallery
                       fills edge-to-edge and its own 20px padding provides the margins. */
                    margin-left: -24px;
                    margin-right: -24px;
                    margin-bottom: -10px;
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
                        onProviderKeysChange={(keys) =>
                            postJsonAsync("app/userSetting", {
                                settingName: "ImageGalleryProviderKeys",
                                settingValue: JSON.stringify(keys),
                            })
                        }
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
