import { css } from "@emotion/react";
import * as React from "react";
import { Link } from "@mui/material";
import { DialogControlGroup } from "./BloomDialog/commonDialogComponents";
import { BloomAvatar } from "./bloomAvatar";
import { useL10n } from "./l10nHooks";
import { BloomTooltip } from "./BloomToolTip";
import { default as InfoIcon } from "@mui/icons-material/InfoOutlined";
import { kBloomBlue } from "../bloomMaterialUITheme";
import { Div } from "./l10nComponents";

interface IBookInfoCardProps {
    title: string;
    bookUrl?: string;
    languages: string[];
    thumbnailUrl?: string;
    originalUpload?: string;
    lastUpdated?: string;
    uploadedBy?: string;
    userEmail?: string;
    canUpload?: boolean;
}

export const BookInfoCard: React.FunctionComponent<IBookInfoCardProps> = (
    props,
) => {
    // room for 4 lines; show up to 4 unless we have more, then show 3 and a "more" message
    const langsToShow = props.languages.length > 4 ? 3 : 4;
    const languageList = props.languages
        .slice(0, langsToShow)
        .map((languageName, index) => {
            return <div key={index}>{languageName}</div>;
        });
    const moreMessage = useL10n(
        "{0} more",
        "PublishTab.UploadCollisionDialog.MoreLanguages",
        "{0} is the number of additional languages the book has that we don't have room to show",
        (props.languages.length - langsToShow).toString(),
    );
    const moreToolTip = (
        <div
            css={css`
                display: flex;
                max-width: 150px;
                flex-wrap: wrap;
                gap: 5px;
            `}
        >
            {props.languages.slice(langsToShow).map((languageName, index) => {
                return (
                    <div key={index}>
                        {languageName +
                            (index < props.languages.length - langsToShow - 1
                                ? ","
                                : "")}
                    </div>
                );
            })}
        </div>
    );

    let uploaderToShow = props.uploadedBy;
    if (!props.canUpload) {
        uploaderToShow = uploaderToShow?.replace(/@.*$/, "@...");
    }
    const uploaderMsg = useL10n(
        "Uploaded by {0}",
        "PublishTab.UploadCollisionDialog.UploadedBy",
        "{0} is the email of the person who uploaded the book",
        uploaderToShow,
    );

    const titleWithOptionalLink = (): JSX.Element => (
        <div>
            {props.bookUrl ? (
                <Link color="primary" underline="always" href={props.bookUrl}>
                    {props.title}
                </Link>
            ) : (
                props.title
            )}
        </div>
    );

    return (
        <div
            css={css`
                width: 320px;
                max-height: 250px;
                overflow-y: auto;
            `}
        >
            <DialogControlGroup>
                <div>
                    <div
                        css={css`
                            display: flex;
                            flex-direction: row;
                        `}
                    >
                        {props.thumbnailUrl && (
                            <div
                                css={css`
                                    margin-right: 8px;
                                    display: flex;
                                    flex-direction: column;
                                    justify-content: flex-start;
                                `}
                            >
                                <img
                                    css={css`
                                        max-width: 70px;
                                        max-height: 70px;
                                    `}
                                    src={props.thumbnailUrl}
                                />
                            </div>
                        )}
                        <div
                            css={css`
                                font-size: 11pt;
                            `}
                        >
                            {titleWithOptionalLink()}
                            {languageList}
                            {props.languages.length > langsToShow && (
                                <BloomTooltip
                                    tip={moreToolTip}
                                    placement="bottom"
                                >
                                    <div
                                        css={css`
                                            color: ${kBloomBlue};
                                            display: flex;
                                        `}
                                    >
                                        <InfoIcon fontSize="small" />
                                        <div
                                            css={css`
                                                margin-left: 5px;
                                            `}
                                        >
                                            {moreMessage}
                                        </div>
                                    </div>
                                </BloomTooltip>
                            )}
                        </div>
                    </div>
                    {props.uploadedBy && (
                        <div>
                            <div
                                css={css`
                                    margin-top: 10px;
                                    display: flex;
                                `}
                            >
                                {props.canUpload && (
                                    <BloomAvatar
                                        email={props.uploadedBy}
                                        name={props.uploadedBy}
                                        avatarSizeInt={20}
                                    ></BloomAvatar>
                                )}
                                <div
                                    css={css`
                                        margin-left: 10px;
                                    `}
                                >
                                    <div
                                        css={css`
                                            margin-bottom: 5px;
                                        `}
                                    >
                                        {uploaderMsg}
                                    </div>

                                    {props.originalUpload && (
                                        <Div
                                            l10nKey="PublishTab.UploadCollisionDialog.FirstUploadedOn"
                                            l10nParam0={props.originalUpload}
                                        >
                                            Originally uploaded {0}
                                        </Div>
                                    )}
                                    {props.lastUpdated && (
                                        <Div
                                            l10nKey="PublishTab.UploadCollisionDialog.LastUploadedOn"
                                            l10nParam0={props.lastUpdated}
                                        >
                                            Last updated {0}
                                        </Div>
                                    )}
                                </div>
                            </div>
                            {props.userEmail && props.canUpload && (
                                <div
                                    css={css`
                                        margin-top: 10px;
                                        display: flex;
                                    `}
                                >
                                    <BloomAvatar
                                        email={props.userEmail}
                                        name={props.userEmail}
                                        avatarSizeInt={20}
                                    ></BloomAvatar>
                                    <Div
                                        css={css`
                                            margin-left: 10px;
                                        `}
                                        l10nKey="PublishTab.UploadCollisionDialog.YouHavePermissionToModify"
                                    >
                                        You have permission to modify this book
                                    </Div>
                                </div>
                            )}
                        </div>
                    )}
                </div>
            </DialogControlGroup>
        </div>
    );
};
