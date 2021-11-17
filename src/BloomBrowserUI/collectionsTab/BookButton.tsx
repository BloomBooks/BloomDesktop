/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import Grid from "@material-ui/core/Grid";
import * as React from "react";
import { BloomApi } from "../utils/bloomApi";
import { Button } from "@material-ui/core";
import TruncateMarkup from "react-truncate-markup";
import {
    IBookTeamCollectionStatus,
    useTColBookStatus
} from "../teamCollection/teamCollectionApi";
import { BloomAvatar } from "../react_components/bloomAvatar";
import {
    kBloomBlue,
    kBloomGold,
    kBloomLightBlue
} from "../bloomMaterialUITheme.js";
import { useRef, useState, useEffect } from "react";
import { useSubscribeToWebSocketForEvent } from "../utils/WebSocketManager";

export const BookButton: React.FunctionComponent<{
    book: any;
    isInEditableCollection: boolean;
    selected: boolean;
    renaming: boolean;
    onClick: (bookId: string) => void;
    onRenameComplete: (newName: string) => void;
}> = props => {
    // TODO: the c# had Font = bookInfo.IsEditable ? _editableBookFont : _collectionBookFont,

    const renameDiv = useRef<HTMLElement | null>();

    const teamCollectionStatus = useTColBookStatus(
        props.book.folderName,
        props.isInEditableCollection
    );

    const [reload, setReload] = useState(0);
    // Force a reload when our book's thumbnail image changed
    useSubscribeToWebSocketForEvent("bookImage", "reload", args => {
        if (args.message === props.book.id) {
            setReload(old => old + 1);
        }
    });

    useEffect(() => {
        if (renameDiv.current) {
            // we just turned it on. Select everything.
            const p = renameDiv.current;
            const s = window.getSelection();
            const r = document.createRange();
            r.selectNodeContents(p);
            s!.removeAllRanges();
            s!.addRange(r);
            //
            window.setTimeout(() => {
                // I tried the obvious approach of putting an onBlur in the JSX for the renameDiv,
                // but it gets activated immediately, and I cannot figure out why.
                renameDiv.current?.addEventListener("blur", () => {
                    props.onRenameComplete(renameDiv.current!.innerText);
                });
                p.focus();
            }, 10);
        }
    }, [props.renaming, renameDiv.current]);

    const label =
        props.book.title.length > 20 ? (
            <TruncateMarkup lines={2}>
                <span>{props.book.title}</span>
            </TruncateMarkup>
        ) : (
            props.book.title
        );

    const buttonHeight = 120;
    const renameHeight = 40;

    return (
        <Grid
            item={true}
            className={props.selected ? "selected-book-wrapper" : ""}
        >
            <div
                // relative so the absolutely positioned rename div will be relative to this.
                css={css`
                    position: relative;
                `}
            >
                {teamCollectionStatus?.who && (
                    <BloomAvatar
                        email={teamCollectionStatus.who}
                        name={teamCollectionStatus.whoFirstName}
                        avatarSizeInt={32}
                        borderColor={
                            teamCollectionStatus.who ===
                            teamCollectionStatus.currentUser
                                ? kBloomGold
                                : kBloomBlue
                        }
                    />
                )}
                <Button
                    className={
                        "bookButton" +
                        (props.selected ? " selected " : "") +
                        (teamCollectionStatus?.who ? " checkedOut" : "")
                    }
                    css={css`
                        height: ${buttonHeight}px;
                        width: 90px;
                        border: none;
                        overflow: hidden;
                        padding: 0;
                    `}
                    variant="outlined"
                    size="large"
                    onClick={() => props.onClick(props.book.id)}
                    startIcon={
                        <div className={"thumbnail-wrapper"}>
                            <img
                                src={`/bloom/api/collections/book/thumbnail?book-id=${
                                    props.book.id
                                }&collection-id=${encodeURIComponent(
                                    props.book.collectionId
                                )}&reload=${reload}`}
                            />
                        </div>
                    }
                >
                    {props.renaming || label}
                </Button>
                {// I tried putting this div inside the button as an alternate to label.
                // Somehow, this causes the blur to happen when the user clicks in the label
                // to position the IP during editing. I suspect default events are being
                // triggered by the fact that we're in a button. It was very hard to debug,
                // and stopPropagation did not help. I finally decided that letting the
                // edit box be over the button was easier and possibly safer.
                props.renaming && (
                    <div
                        // For some unknown reason, the selection background color was coming out white.
                        // I reset it to what I think is Windows standard.
                        // Enhance: ideally we'd either figure out why we don't get the usual default
                        // selection background (maybe because the window has a dark background?)
                        // or make it use the user's configured system highlight background (but that
                        // really might not work with white text and a dark background?)

                        // 12px of text height matches the size we're getting in the button.
                        // 18px is more line spacing than we normally use for these labels, but it's
                        // the minimum to avoid descenders in the top line being cut off by the highlight
                        // of selected text in the second line. (Of course other fonts might need
                        // a different value...but it's not a terrible problem if there is some cut off.)

                        // (I think the 6 fudge factor in the top calculation is made up of two 1px
                        // borders and the 4px padding-top.)
                        css={css`
                            width: calc(100% - 4px);
                            height: ${renameHeight}px;
                            margin-left: 1px;
                            border: 1px solid ${kBloomLightBlue};
                            top: ${buttonHeight - renameHeight - 6}px;
                            padding-top: 4px;
                            position: absolute;
                            font-size: 12px;
                            line-height: 18px;
                            text-align: center;
                            &::selection {
                                background: rgb(0, 120, 215);
                            }
                        `}
                        contentEditable={true}
                        tabIndex={0}
                        ref={renderedElement =>
                            (renameDiv.current = renderedElement)
                        }
                        // Note: we want a blur on this element, but putting it here does not work right.
                        // See the comment where we add it in a delayed effect.
                    >
                        {props.book.title}
                    </div>
                )}
            </div>
        </Grid>
    );
};
