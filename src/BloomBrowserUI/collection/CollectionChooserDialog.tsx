import { css } from "@emotion/react";
import FileFindIcon from "@mui/icons-material/FindInPage";
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

export const CollectionChooserDialog: React.FunctionComponent<IProps> = (
    props,
) => {
    const { propsForBloomDialog } = useSetupBloomDialog(
        props.dialogEnvironment,
    );
    const dialogTitle = useL10n(
        "Open/Create Collections",
        "OpenCreateNewCollectionsDialog.OpenAndCreateWindowTitle",
    );
    return (
        <BloomDialog
            {...propsForBloomDialog}
            open={props.open ?? propsForBloomDialog.open}
            onClose={props.onClose ?? propsForBloomDialog.onClose}
            onCancel={props.onClose ?? propsForBloomDialog.onClose}
            maxWidth={false}
        >
            <BloomDialogContext.Provider
                value={{
                    onCancel: props.onClose ?? propsForBloomDialog.onClose,
                    disableDragging: true,
                }}
            >
                <DialogTitle
                    title={dialogTitle}
                    icon={
                        <img
                            css={css`
                                width: 15px;
                                height: 15px;
                                top: 2px;
                                position: relative;
                            `}
                            src={`${getBloomApiPrefix(false)}BloomIcon.svg`}
                            alt=""
                        />
                    }
                >
                    <div
                        css={css`
                            margin-left: auto;
                            margin-right: 8px;
                            display: flex;
                            align-items: center;
                        `}
                    >
                        <UiLanguageMenu />
                    </div>
                </DialogTitle>
            </BloomDialogContext.Provider>
            <DialogMiddle
                css={css`
                    height: 340px;
                `}
            >
                <CollectionChooser collections={props.collections} />
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogBottomLeftButtons>
                    <BloomButton
                        variant="text"
                        color="primary"
                        enabled={true}
                        l10nKey={
                            "OpenCreateNewCollectionsDialog.BrowseForOtherCollections"
                        }
                        startIcon={<FileFindIcon />}
                        onClick={() => post("workspace/browseForCollection")}
                    >
                        Browse for another collection on this computer
                    </BloomButton>
                </DialogBottomLeftButtons>
                <BloomButton
                    variant="contained"
                    color="primary"
                    enabled={true}
                    l10nKey={
                        "OpenCreateNewCollectionsDialog.CreateNewCollection"
                    }
                    onClick={() => post("workspace/createNewCollection")}
                >
                    Create New Collection
                </BloomButton>
            </DialogBottomButtons>
        </BloomDialog>
    );
};

WireUpForWinforms(CollectionChooserDialog);
