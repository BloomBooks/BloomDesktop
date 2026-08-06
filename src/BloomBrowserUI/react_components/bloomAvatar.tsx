import { css } from "@emotion/react";

import * as React from "react";
import Avatar, { Cache, ConfigProvider } from "react-avatar";
import { getBloomApiPrefix } from "../utils/bloomApi";

// react-avatar does not cache actual avatars. It only caches which source urls failed
// (whether because the user was offline or the source 404'd), and then doesn't retry the failed
// urls so long as they are valid in cache. We do want it to retry (the local server may have gained
// connectivity), so keep its cache empty. The config never changes, so this is a module-level
// constant rather than being rebuilt on every render.
const emptyAvatarCache = new Cache({
    sourceTTL: 0, // retain for 0 milliseconds
    sourceSize: 0, // retain a maximum of 0 items in cache
});

export const BloomAvatar: React.FunctionComponent<{
    email: string;
    name: string;
    borderColor?: string;
    avatarSizeInt?: number;
}> = (props) => {
    const borderSizeInt = 3;
    const avatarSizeInt = props.avatarSizeInt || 48;
    const avatarSize = props.borderColor
        ? `${avatarSizeInt - borderSizeInt}px`
        : `${avatarSizeInt}px`;
    const borderStyle = props.borderColor
        ? `${borderSizeInt}px solid ${props.borderColor}`
        : undefined;

    // The avatar image comes from Bloom's own local server, keyed by email: it decides the best
    // source (the person's known Google/Firebase photo if we have one, otherwise Gravatar), caches
    // the actual bytes so avatars survive restart and work offline, and returns 404 when it has
    // nothing -- in which case react-avatar falls back to the generated initials from `name`. The
    // server hashes/normalizes the email itself, so we just pass the email.
    const avatarSrc = props.email
        ? `${getBloomApiPrefix()}avatar?email=${encodeURIComponent(props.email)}`
        : undefined;

    return (
        <React.Suspense fallback={<React.Fragment />}>
            <div
                className={"avatar " + props["className"]}
                css={css`
                    border-radius: 50%;
                    overflow: hidden;
                    width: ${avatarSize};
                    height: ${avatarSize};
                    border: ${borderStyle};
                    // Generated (three-letter) avatars somehow let a little of the background
                    // show through around the edges. This makes it look like just part of the
                    // border.
                    background-color: ${props.borderColor};
                `}
            >
                <ConfigProvider cache={emptyAvatarCache}>
                    <Avatar
                        src={avatarSrc}
                        name={props.name}
                        size={avatarSize}
                        maxInitials={3}

                        // If you do this instead of styling the outer div, you can't put a border around the circle, only the square
                        //round={true}
                    />
                </ConfigProvider>
            </div>
        </React.Suspense>
    );
};
