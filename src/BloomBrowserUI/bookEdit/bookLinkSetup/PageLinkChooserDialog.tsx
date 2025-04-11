import * as React from "react";
import {
    BookInfoForLinks,
    PageInfoForLinks,
    ThumbnailGenerator
} from "./BookLinkTypes";
import { css } from "@emotion/react";
import {
    BloomDialog,
    DialogTitle,
    DialogMiddle,
    DialogBottomButtons
} from "../../react_components/BloomDialog/BloomDialog";
import { DialogCancelButton } from "../../react_components/BloomDialog/commonDialogComponents";

interface Props {
    open: boolean;
    selectedBook: BookInfoForLinks | null;
    onClose: () => void;
    onPageSelected: (pageInfo: PageInfoForLinks) => void;
    thumbnailGenerator: ThumbnailGenerator;
}

export const PageLinkChooserDialog: React.FC<Props> = ({
    open,
    selectedBook,
    onClose,
    onPageSelected,
    thumbnailGenerator
}) => {
    return (
        <BloomDialog open={open} onClose={onClose} maxWidth="md" fullWidth>
            <DialogTitle title="Select Page" />
            <DialogMiddle>
                <div
                    css={css`
                        display: grid;
                        grid-template-columns: repeat(
                            auto-fill,
                            minmax(140px, 1fr)
                        );
                        gap: 16px;
                    `}
                >
                    {selectedBook &&
                        [...Array(selectedBook.pageLength)].map((_, index) => {
                            const pageNumber = index + 1;
                            return (
                                <div
                                    key={pageNumber}
                                    css={css`
                                        cursor: pointer;
                                        &:hover {
                                            opacity: 0.8;
                                        }
                                    `}
                                    onClick={() =>
                                        onPageSelected({
                                            pageId: pageNumber,
                                            thumbnail: thumbnailGenerator(
                                                selectedBook.id,
                                                pageNumber
                                            )
                                        })
                                    }
                                >
                                    <img
                                        src={thumbnailGenerator(
                                            selectedBook.id,
                                            pageNumber
                                        )}
                                        alt={`Page ${pageNumber}`}
                                        css={css`
                                            width: 100%;
                                            border-radius: 4px;
                                            margin-bottom: 4px;
                                        `}
                                    />
                                    <div
                                        css={css`
                                            text-align: center;
                                        `}
                                    >
                                        Page {pageNumber}
                                    </div>
                                </div>
                            );
                        })}
                </div>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogCancelButton onClick_DEPRECATED={onClose} />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
