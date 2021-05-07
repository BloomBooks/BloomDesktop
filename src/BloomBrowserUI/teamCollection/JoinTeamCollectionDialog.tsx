/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { BloomApi } from "../utils/bloomApi";
import { Div, P } from "../react_components/l10nComponents";
import BloomButton from "../react_components/bloomButton";

import {
    BloomDialog,
    DialogBottomButtons,
    DialogCancelButton,
    DialogMiddle,
    DialogTitle,
    IBloomDialogEnvironmentParams,
    NoteBox,
    useMakeBloomDialog
} from "../react_components/BloomDialog/BloomDialog";
import { useL10n } from "../react_components/l10nHooks";

// The contents of the dialog that comes up when double-clicking a .JoinBloomTC file.
// Two versions (create new local collection, and merge with existing local) are both handled here.

export const JoinTeamCollectionDialog: React.FunctionComponent<{
    collectionName: string;
    existingCollection: boolean;
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    const { showDialog, closeDialog, propsForBloomDialog } = useMakeBloomDialog(
        props.dialogEnvironment
    );

    const dialogTitle = useL10n(
        'Join the Bloom Team Collection "%0"',
        "TeamCollection.JoinHeading",
        undefined,
        props.collectionName
    );

    return (
        <BloomDialog {...propsForBloomDialog}>
            <DialogTitle title={`${dialogTitle} (experimental)`} />
            <DialogMiddle>
                {props.existingCollection ? (
                    <React.Fragment>
                        <P
                            l10nKey="TeamCollection.Merging"
                            l10nParam0={props.collectionName}
                            temporarilyDisableI18nWarning={true}
                        >
                            You already have a collection with this same name,
                            "%0". If you continue, Bloom will merge your
                            existing "%0" collection with the Team Collection
                            are joining.
                        </P>
                        <P
                            l10nKey="TeamCollection.MergingExplanation"
                            temporarilyDisableI18nWarning={true}
                        >
                            The rest of the team will receive any unique books
                            found in that collection. Any books you have that
                            are already in this Team Collection will be moved to
                            the "Lost and Found" folder, unless Bloom can
                            determine that they are the same.
                        </P>
                        <NoteBox>
                            <Div
                                l10nKey="TeamCollection.StartFresh"
                                l10nParam0={props.collectionName}
                                temporarilyDisableI18nWarning={true}
                            >
                                If you do not want to merge in your existing
                                "%0" collection, click Cancel and rename your
                                existing "%0" collection to something different,
                                or delete it. Then come back and join again.
                            </Div>
                        </NoteBox>
                    </React.Fragment>
                ) : (
                    <React.Fragment>
                        <P
                            l10nKey="TeamCollection.Joining"
                            l10nParam0={props.collectionName}
                            temporarilyDisableI18nWarning={true}
                        >
                            Bloom will set you up to work together with your
                            team on this collection of books.
                        </P>
                        <NoteBox>
                            <Div
                                l10nKey="TeamCollection.MergeInstead"
                                l10nParam0={props.collectionName}
                                temporarilyDisableI18nWarning={true}
                            >
                                If, instead, you want to <strong>merge</strong>{" "}
                                a collection you already have into this Team
                                Collection, click Cancel and rename the
                                collection you want to merge to "%0". Then try
                                to join again.
                            </Div>
                        </NoteBox>
                    </React.Fragment>
                )}
            </DialogMiddle>
            <DialogBottomButtons>
                <BloomButton
                    l10nKey={
                        props.existingCollection
                            ? "TeamCollection.JoinAndMerge"
                            : "TeamCollection.Join"
                    }
                    temporarilyDisableI18nWarning={true}
                    hasText={true}
                    enabled={true}
                    onClick={() => {
                        BloomApi.post("teamCollection/joinTeamCollection");
                    }}
                >
                    {props.existingCollection ? "Join and Merge" : "Join"}
                </BloomButton>
                <DialogCancelButton onClick={closeDialog} />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
