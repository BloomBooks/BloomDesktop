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
import type { IImageInfo } from "../../bookEdit/js/bloomEditing";
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
    imageId: string;
    searchLang: string;
}> = (props) => {
    const [open, setOpen] = useState(true);
    // Keys are loaded from durable Bloom settings before the gallery is rendered,
    // so providers (e.g. Pixabay) receive their initial API key in their constructor.
    const [providerKeys, setProviderKeys] = useState<
        IProviderKeysV1 | undefined
    >(undefined);
    const [keysLoaded, setKeysLoaded] = useState(false);

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
        getEditablePageBundleExports()?.removeImageId(props.imageId);
    };

    const onConfirmSelection = async (image: IImage) => {
        setOpen(false);
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
            const imageInfo: IImageInfo = {
                imageId: props.imageId,
                src: result.src,
                copyright: result.copyright,
                creator: result.creator,
                license: result.license,
                undoable: "false",
            };
            getEditablePageBundleExports()?.changeImage(imageInfo);
        } catch {
            getEditablePageBundleExports()?.removeImageId(props.imageId);
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

    const artOfReadingBaseUrl =
        getBloomApiPrefix() + "imageGallery/artOfReading";

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
                        artOfReadingBaseUrl={artOfReadingBaseUrl}
                        lang={props.searchLang}
                        initialProviderKeys={providerKeys}
                        primaryColor={kBloomBlue}
                        onProviderKeysChange={(keys) =>
                            postJsonAsync("app/userSetting", {
                                settingName: "ImageGalleryProviderKeys",
                                settingValue: JSON.stringify(keys),
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

/** Show the image gallery dialog. imageId must already be set on the imgElement. */
export function showImageGalleryDialog(
    imageId: string,
    searchLang: string,
): void {
    ShowEditViewDialog(
        <ImageGalleryDialog imageId={imageId} searchLang={searchLang} />,
    );
}
