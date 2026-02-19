import * as React from "react";
export const AudioIcon: React.FunctionComponent<{
    className?: string;
    color?: string;
}> = (props) => {
    return (
        // This svg is basically the same as the one in CanvasElementManager.tsx's addSoundCanvasElement.
        // Likely, changes to one should be mirrored in the other.
        <svg
            className={props.className}
            width="32"
            height="31"
            viewBox="0 0 32 31"
            fill="none"
            xmlns="http://www.w3.org/2000/svg"
        >
            <rect width="31" height="31" rx="1.81232" fill="#0C8597" />
            <path
                d="M23.0403 9.12744C24.8868 10.8177 25.9241 13.11 25.9241 15.5C25.9241 17.8901 24.8868 20.1823 23.0403 21.8726M19.5634 12.3092C20.4867 13.1544 21.0053 14.3005 21.0053 15.4955C21.0053 16.6906 20.4867 17.8367 19.5634 18.6818M15.0917 9.19054L10.1669 12.796H6.22705V18.2041H10.1669L15.0917 21.8095V9.19054Z"
                stroke="white"
                strokeWidth="1.15865"
                strokeLinecap="round"
                strokeLinejoin="round"
            />
        </svg>
    );
};
