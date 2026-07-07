import * as React from "react";
import { useState } from "react";
import { post } from "../utils/bloomApi";
import { Div, P, Span } from "../react_components/l10nComponents";
import BloomButton from "../react_components/bloomButton";

import {
    BloomDialog,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle,
} from "../react_components/BloomDialog/BloomDialog";
import { useL10n } from "../react_components/l10nHooks";
import {
    DialogCancelButton,
    DialogReportButton,
} from "../react_components/BloomDialog/commonDialogComponents";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog,
} from "../react_components/BloomDialog/BloomDialogPlumbing";
import { ErrorBox, NoteBoxSansBorder } from "../react_components/boxes";
import { pullDownCollection } from "./sharingApi";

// The pull-down-join dialog for cloud Team Collections: same shape as the folder-TC
// JoinTeamCollectionDialog (see that file), extended with two states that only make sense for
// a cloud collection where "join" means "sign in, then pull down a copy from the server" rather
// than "point at a shared folder": NotSignedIn and ApprovalRemoved. Eight variations total.
//
// Embedded directly inside CollectionChooser (not opened as its own WinForms ReactDialog --
// there is no C# call site or bundle entry for it, unlike the other top-level dialogs in this
// folder), so it deliberately does NOT call WireUpForWinforms itself: CollectionChooser lives
// in the same bundle as CollectionChooserDialog, which already owns that bundle's one
// WireUpForWinforms call, and a second call here would silently overwrite it (the exact bug
// item 1 of this task fixed elsewhere -- see CreateTeamCollection.tsx).
enum JoinCloudCollectionState {
    // The signed-in user must sign in before Bloom can check their approval / pull anything down.
    "NotSignedIn",
    // Signed in, but this email is not (or no longer) on the collection's approved-accounts list.
    "ApprovalRemoved",
    // No local collection with the same name yet. Offer to pull down a fresh copy.
    "CreateNewCollection",
    // A local collection with the same name exists but isn't a Team Collection. Offer to merge.
    "MatchesExistingNonTeamCollection",
    // Already pulled down and linked to this same cloud collection, at the expected location.
    "MatchesExistingTeamCollection",
    // Already linked to this same cloud collection, but the local copy has moved. Offer to fix up.
    "MatchesExistingTeamCollectionElsewhere",
    // A local collection of the same name is linked to a *different* cloud collection. Conflict.
    "MatchesDifferentTeamCollection",
    // A previous pull-down left an incomplete/corrupt local cache. Needs a fresh pull-down.
    "IncompleteLocalCopy",
}

// In normal use (not storybook), this is a top-level component in a ReactDialog, opened when
// the user picks a cloud collection from "Get my Team Collections" in the collection chooser.
export const JoinCloudCollectionDialog: React.FunctionComponent<{
    collectionId: string;
    collectionName: string;
    signedIn: boolean;
    isApproved: boolean;
    incompleteLocalCopy?: boolean;
    existingCollection: boolean;
    isAlreadyTcCollection: boolean;
    isSameCollection: boolean; // that is, linked to this same cloud collectionId
    isCurrentCollection: boolean; // that is, it already points at the expected local location
    existingCollectionFolder: string; // if there's an existing local collection, a path to it
    conflictingCollection: string; // if there's a conflicting repo the existing collection is linked to
    dialogEnvironment?: IBloomDialogEnvironmentParams;
    // Called after the dialog closes, whether via Cancel or a successful pull-down. Lets an
    // embedding parent (CollectionChooser) unmount/hide it; optional so storybook and existing
    // tests that don't care about this can omit it.
    onClose?: () => void;
}> = (props) => {
    const { closeDialog, propsForBloomDialog } = useSetupBloomDialog(
        props.dialogEnvironment,
    );
    const [joining, setJoining] = useState(false);
    const [joinError, setJoinError] = useState<string | undefined>(undefined);

    const dialogTitle = useL10n(
        'Join the Bloom Team Collection "%0"',
        "TeamCollection.JoinHeading",
        undefined,
        props.collectionName,
        undefined,
        true, // temporarilyDisableI18nWarning
    );
    const dialogState = getDialogStateFromProps();
    const l10nJoinButtonKey = getL10nKeyForJoinButton();
    const joinButtonEnglish = getJoinButtonEnglish();

    function getDialogStateFromProps(): JoinCloudCollectionState {
        if (!props.signedIn) {
            return JoinCloudCollectionState.NotSignedIn;
        }
        if (!props.isApproved) {
            return JoinCloudCollectionState.ApprovalRemoved;
        }
        if (props.incompleteLocalCopy) {
            return JoinCloudCollectionState.IncompleteLocalCopy;
        }
        if (!props.existingCollection) {
            return JoinCloudCollectionState.CreateNewCollection;
        }
        if (!props.isAlreadyTcCollection) {
            return JoinCloudCollectionState.MatchesExistingNonTeamCollection;
        }
        if (!props.isSameCollection) {
            return JoinCloudCollectionState.MatchesDifferentTeamCollection;
        }
        if (props.isCurrentCollection) {
            return JoinCloudCollectionState.MatchesExistingTeamCollection;
        } else {
            return JoinCloudCollectionState.MatchesExistingTeamCollectionElsewhere;
        }
    }

    function getL10nKeyForJoinButton(): string {
        if (dialogState === JoinCloudCollectionState.NotSignedIn) {
            return "TeamCollection.Sharing.SignIn";
        }
        return dialogState ===
            JoinCloudCollectionState.MatchesExistingNonTeamCollection
            ? "TeamCollection.JoinAndMerge"
            : dialogState ===
                JoinCloudCollectionState.MatchesExistingTeamCollection
              ? "TeamCollection.Open"
              : "TeamCollection.Join";
    }

    function getJoinButtonEnglish(): string {
        if (dialogState === JoinCloudCollectionState.NotSignedIn) {
            return "Sign In";
        }
        return dialogState ===
            JoinCloudCollectionState.MatchesExistingNonTeamCollection
            ? "Join and Merge"
            : dialogState ===
                JoinCloudCollectionState.MatchesExistingTeamCollection
              ? "Open"
              : "Join";
        // Leaving it as "Join" for the pathological/disabled cases.
    }

    function getMatchingCollection() {
        return (
            <p>
                <Span
                    l10nKey="TeamCollection.MatchingLocal"
                    temporarilyDisableI18nWarning={true}
                >
                    Matching local collection:
                </Span>{" "}
                <span>{props.existingCollectionFolder}</span>
            </p>
        );
    }

    function getDialogBodyNotSignedIn(): JSX.Element {
        return (
            <NoteBoxSansBorder>
                <Div
                    l10nKey="TeamCollection.Sharing.MustSignInToJoin"
                    l10nParam0={props.collectionName}
                    temporarilyDisableI18nWarning={true}
                >
                    Sign in with your Bloom account to join "%0".
                </Div>
            </NoteBoxSansBorder>
        );
    }

    function getDialogBodyApprovalRemoved(): JSX.Element {
        return (
            <ErrorBox>
                <Div
                    l10nKey="TeamCollection.Sharing.ApprovalRemoved"
                    l10nParam0={props.collectionName}
                    temporarilyDisableI18nWarning={true}
                >
                    You are not currently on the approved list for "%0". Contact
                    an administrator of this Team Collection to be added.
                </Div>
            </ErrorBox>
        );
    }

    function getDialogBodyIncompleteLocalCopy(): JSX.Element {
        return (
            <ErrorBox>
                <Div
                    l10nKey="TeamCollection.Sharing.IncompleteLocalCopy"
                    temporarilyDisableI18nWarning={true}
                >
                    Bloom found an incomplete local copy of this Team
                    Collection, probably left over from an earlier attempt to
                    join. Bloom will download a fresh copy.
                </Div>
            </ErrorBox>
        );
    }

    function getBloomWillPullDown() {
        return (
            <P
                l10nKey="TeamCollection.Sharing.BloomWillPullDown"
                l10nParam0={props.collectionName}
                temporarilyDisableI18nWarning={true}
            >
                Bloom will download this Team Collection so you can work
                together with your team.
            </P>
        );
    }

    function getDialogBodyExistingNonTC(): JSX.Element {
        return (
            <React.Fragment>
                {getBloomWillPullDown()}
                <NoteBoxSansBorder>
                    <Span
                        l10nKey="TeamCollection.Merging"
                        l10nParam0={props.collectionName}
                        temporarilyDisableI18nWarning={true}
                    >
                        You already have a collection with this same name. If
                        you continue, Bloom will merge your existing "%0"
                        collection with the Team Collection that you are
                        joining.
                    </Span>
                </NoteBoxSansBorder>
                {getMatchingCollection()}
            </React.Fragment>
        );
    }

    // Existing local collection is already linked to this same cloud collection.
    function getDialogBodyExistingTC(): JSX.Element {
        return (
            <React.Fragment>
                <NoteBoxSansBorder>
                    <Div
                        l10nKey="TeamCollection.AlreadyJoined"
                        temporarilyDisableI18nWarning={true}
                    >
                        This computer is already connected to this collection.
                        Bloom will open it for you.
                    </Div>
                </NoteBoxSansBorder>
                {getMatchingCollection()}
            </React.Fragment>
        );
    }

    // No local collection of this name yet: just say Bloom will pull one down.
    function getDialogBodyCreateNew(): JSX.Element {
        return <React.Fragment>{getBloomWillPullDown()}</React.Fragment>;
    }

    // Common content for the two pathological cases where the existing local collection is a TC
    // but NOT already linked to the cloud collection we're trying to join.
    function getConflictingCollectionCommon() {
        return (
            <React.Fragment>
                <ErrorBox>
                    <Div
                        l10nKey="TeamCollection.ConflictingCollection"
                        temporarilyDisableI18nWarning={true}
                    >
                        Bloom found another collection with this same name that
                        is already connected to a different Team Collection.
                        Click REPORT to get help from the Bloom team.
                    </Div>
                </ErrorBox>
                {getMatchingCollection()}
                <p>
                    <Span
                        l10nKey="TeamCollection.ConflictingCollection"
                        temporarilyDisableI18nWarning={true}
                    >
                        Conflicting Team collection:
                    </Span>
                    <span>{props.conflictingCollection}</span>
                </p>
            </React.Fragment>
        );
    }

    // Existing local collection is a TC linked to this same cloud collection but at a different
    // local path (e.g. the local cache folder moved).
    function getDialogBodyExistingTcElsewhere() {
        return (
            <React.Fragment>
                <NoteBoxSansBorder>
                    <Div
                        l10nKey="TeamCollection.AlreadyJoinedElsewhere"
                        temporarilyDisableI18nWarning={true}
                    >
                        This computer is already connected to this collection,
                        which appears to have moved. Bloom will fix things up
                        and open it for you.
                    </Div>
                </NoteBoxSansBorder>
                {getMatchingCollection()}
            </React.Fragment>
        );
    }

    // Existing local collection is a TC for a different cloud collection (same name, different id).
    function getDialogBodyDifferentTc() {
        return (
            <React.Fragment>
                {getConflictingCollectionCommon()}
                <P
                    l10nKey="TeamCollection.SameIds"
                    temporarilyDisableI18nWarning={true}
                >
                    (Different TC IDs)
                </P>
            </React.Fragment>
        );
    }

    function getBodyOfDialogByState(): JSX.Element {
        switch (dialogState) {
            case JoinCloudCollectionState.NotSignedIn:
                return getDialogBodyNotSignedIn();
            case JoinCloudCollectionState.ApprovalRemoved:
                return getDialogBodyApprovalRemoved();
            case JoinCloudCollectionState.IncompleteLocalCopy:
                return getDialogBodyIncompleteLocalCopy();
            case JoinCloudCollectionState.MatchesExistingNonTeamCollection:
                return getDialogBodyExistingNonTC();
            case JoinCloudCollectionState.MatchesExistingTeamCollection:
                return getDialogBodyExistingTC();
            case JoinCloudCollectionState.CreateNewCollection:
                return getDialogBodyCreateNew();
            case JoinCloudCollectionState.MatchesExistingTeamCollectionElsewhere:
                return getDialogBodyExistingTcElsewhere();
            case JoinCloudCollectionState.MatchesDifferentTeamCollection:
                return getDialogBodyDifferentTc();
            default:
                return <div />;
        }
    }

    const wantReportButton =
        dialogState === JoinCloudCollectionState.MatchesDifferentTeamCollection;
    // ApprovalRemoved has no useful action to offer besides closing the dialog.
    const joinButtonDisabled =
        wantReportButton ||
        dialogState === JoinCloudCollectionState.ApprovalRemoved ||
        joining;

    function handleJoinClick() {
        if (dialogState === JoinCloudCollectionState.NotSignedIn) {
            post("sharing/showSignIn");
            return;
        }
        setJoining(true);
        setJoinError(undefined);
        // collections/pullDown (SharingApi.cs's HandlePullDown) either succeeds outright (the
        // ordinary case: no local conflict) or fails with a human-readable message -- it does
        // not report back which of the six local-vs-remote scenarios above applied ahead of
        // time (that matching happens server-side, in CloudJoinFlow, only once the pull-down is
        // actually attempted). So on failure we show the server's real message rather than
        // guessing which specific state's copy to switch to.
        pullDownCollection(props.collectionId).then(
            () => {
                setJoining(false);
                closeDialog();
                props.onClose?.();
            },
            (error) => {
                setJoining(false);
                setJoinError(String(error?.message ?? error));
            },
        );
    }

    return (
        <BloomDialog {...propsForBloomDialog}>
            <DialogTitle title={`${dialogTitle} (experimental)`} />
            <DialogMiddle>
                <div data-testid="join-cloud-collection-body">
                    {getBodyOfDialogByState()}
                </div>
                {joinError && (
                    <div data-testid="join-cloud-collection-error">
                        <ErrorBox>{joinError}</ErrorBox>
                    </div>
                )}
            </DialogMiddle>
            <DialogBottomButtons>
                {wantReportButton && (
                    <DialogBottomLeftButtons>
                        <DialogReportButton
                            shortMessage="Problem joining cloud Team Collection due to conflicting local collection"
                            messageGenerator={() =>
                                // Not trying to be nice about this message. The user won't usually see it;
                                // it's buried in the report we send to YouTrack telling US what went wrong.
                                `trying to join cloud collection ${props.collectionId}, but local collection ${props.existingCollectionFolder} is linked to ${props.conflictingCollection}`
                            }
                        />
                    </DialogBottomLeftButtons>
                )}
                <BloomButton
                    l10nKey={l10nJoinButtonKey}
                    temporarilyDisableI18nWarning={true}
                    hasText={true}
                    enabled={!joinButtonDisabled}
                    data-testid="join-cloud-collection-action-button"
                    onClick={handleJoinClick}
                >
                    {joinButtonEnglish}
                </BloomButton>
                <DialogCancelButton
                    onClick_DEPRECATED={() => {
                        closeDialog();
                        props.onClose?.();
                    }}
                />
            </DialogBottomButtons>
        </BloomDialog>
    );
};
