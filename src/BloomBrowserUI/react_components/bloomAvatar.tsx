import React = require("react");
import Avatar from "react-avatar";
import { getMd5 } from "../bookEdit/toolbox/talkingBook/md5Util";

export const BloomAvatar: React.FunctionComponent<{
    email: string;
    name: string;
    borderColor?: string;
    radius?: number;
}> = props => {
    const borderSizeInt = 3;

    const radius = props.radius ?? 48;
    const avatarSize = props.borderColor
        ? `${radius - borderSizeInt}px`
        : `${radius}px`;
    const borderStyle = props.borderColor
        ? `${borderSizeInt}px solid ${props.borderColor}`
        : undefined;
    return (
        <React.Suspense fallback={<></>}>
            <div
                className="avatar"
                style={{
                    borderRadius: "50%",
                    overflow: "hidden",
                    width: avatarSize,
                    height: avatarSize,
                    border: borderStyle
                }}
            >
                <Avatar
                    md5Email={getMd5(props.email)}
                    name={props.name}
                    size={avatarSize}
                    maxInitials={3}

                    // If you do this instead of styling the outer div, you can't put a border around the circle, only the square
                    //round={true}
                />
            </div>
        </React.Suspense>
    );
};
