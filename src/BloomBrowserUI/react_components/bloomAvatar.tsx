import { css } from "@emotion/react";

import * as React from "react";
import Avatar, { Cache, ConfigProvider } from "react-avatar";
import { getMd5 } from "../bookEdit/toolbox/talkingBook/md5Util";

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

    // react-avatar does not cache actual avatars. It only caches which gravatar urls failed
    // (whether because user was offline or doesn't have a gravatar),
    // and then doesn't retry the failed urls so long as they are valid in cache.
    // We do want it to retry retrieving gravatars, so keep its cache empty
    const cache = new Cache({
        sourceTTL: 0, // retain for 0 milliseconds
        sourceSize: 0, // retain a maximum of 0 items in cache
    });
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
                <ConfigProvider cache={cache}>
                    <Avatar
                        md5Email={getMd5(props.email)}
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
