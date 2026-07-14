import { css } from "@emotion/react";
import SearchIcon from "@mui/icons-material/Search";
import AddIcon from "@mui/icons-material/Add";
import CloseIcon from "@mui/icons-material/Close";
import { IconButton } from "@mui/material";
import BloomButton from "../react_components/bloomButton";
import {
    BloomDialog,
    BloomDialogContext,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle,
} from "../react_components/BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog,
} from "../react_components/BloomDialog/BloomDialogPlumbing";
import { WireUpForWinforms } from "../utils/WireUpWinform";
import { getBloomApiPrefix, post } from "../utils/bloomApi";
import { useL10n } from "../react_components/l10nHooks";
import { UiLanguageMenu } from "../react_components/TopBar/workspaceTopRightControls/UiLanguageMenu";
import { CollectionChooser } from "./CollectionChooser";
import { ICollectionInfo } from "./CollectionCard";

interface IProps {
    open?: boolean;
    onClose?: () => void;
    collections?: ICollectionInfo[];
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}

// A light footer band that spans the full width of the dialog. The negative
// margins bleed out over the dialog's side (24px) and bottom (10px) padding so
// the band reaches the edges; the padding then re-insets the buttons. We zero
// the inner DialogBottomButtons top padding so our own 14px sets the spacing.
const footerBandStyle = css`
    margin-top: auto;
    margin-left: -24px;
    margin-right: -24px;
    margin-bottom: -10px;
    padding: 14px 24px;
    background-color: #fafafa;
    border-top: 1px solid #eee;
    & > div {
        padding-top: 0;
    }
`;

// The language menu and close button form one right-aligned group in the title
// bar, matching the design (rather than the language menu drifting toward the
// middle). We render our own close button and suppress DialogTitle's built-in
// one via preventCloseButton so the two controls sit together.
const titleRightGroupStyle = css`
    margin-left: auto;
    display: flex;
    align-items: center;
    gap: 8px;
    // The language button sets text-transform:none on itself; override it here
    // (only in this dialog) so the label reads uppercase per the design.
    & button {
        text-transform: uppercase !important;
        font-size: 14px;
    }
    // DialogTitle forces font-weight:bold on all its descendants; the design
    // shows the language label at a normal weight, so override it here.
    & * {
        font-weight: 400 !important;
    }
`;

const closeButtonStyle = css`
    color: #8a8a8a;
`;

export const CollectionChooserDialog: React.FunctionComponent<IProps> = (
    props,
) => {
    const { propsForBloomDialog } = useSetupBloomDialog(
        props.dialogEnvironment,
    );
    const dialogTitle = useL10n(
        "Open / Create Collections",
        "OpenCreateNewCollectionsDialog.OpenAndCreateWindowTitle",
    );
    const closeText = useL10n("Close", "Common.Close");
    const handleClose = props.onClose ?? propsForBloomDialog.onClose;
    return (
        <BloomDialog
            {...propsForBloomDialog}
            open={props.open ?? propsForBloomDialog.open}
            onClose={props.onClose ?? propsForBloomDialog.onClose}
            onCancel={props.onClose ?? propsForBloomDialog.onClose}
            maxWidth={false}
            disableDragging={true}
        >
            <BloomDialogContext.Provider
                value={{
                    onCancel: props.onClose ?? propsForBloomDialog.onClose,
                    disableDragging: true,
                }}
            >
                <DialogTitle
                    title={dialogTitle}
                    preventCloseButton={true}
                    icon={
                        <img
                            css={css`
                                width: 15px;
                                height: 15px;
                                top: 7px;
                                position: relative;
                            `}
                            src={`${getBloomApiPrefix(false)}BloomIcon.svg`}
                            alt=""
                        />
                    }
                >
                    <div css={titleRightGroupStyle}>
                        <UiLanguageMenu />
                        <IconButton
                            aria-label={closeText}
                            title={closeText}
                            css={closeButtonStyle}
                            onClick={() => handleClose?.()}
                        >
                            <CloseIcon />
                        </IconButton>
                    </div>
                </DialogTitle>
                <DialogMiddle
                    css={css`
                        height: 420px;
                    `}
                >
                    <CollectionChooser collections={props.collections} />
                </DialogMiddle>
                <div css={footerBandStyle}>
                    <DialogBottomButtons>
                        <DialogBottomLeftButtons>
                            <BloomButton
                                variant="text"
                                color="primary"
                                enabled={true}
                                l10nKey={
                                    "OpenCreateNewCollectionsDialog.BrowseOnThisComputer"
                                }
                                startIcon={<SearchIcon />}
                                onClick={() =>
                                    post("workspace/browseForCollection")
                                }
                            >
                                Browse on this computer
                            </BloomButton>
                        </DialogBottomLeftButtons>
                        <BloomButton
                            variant="contained"
                            color="primary"
                            enabled={true}
                            l10nKey={
                                "OpenCreateNewCollectionsDialog.CreateNewCollection"
                            }
                            startIcon={<AddIcon />}
                            onClick={() =>
                                post("workspace/createNewCollection")
                            }
                        >
                            Create New Collection
                        </BloomButton>
                    </DialogBottomButtons>
                </div>
            </BloomDialogContext.Provider>
        </BloomDialog>
    );
};

WireUpForWinforms(CollectionChooserDialog);
