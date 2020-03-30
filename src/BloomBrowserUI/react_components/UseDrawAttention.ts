import React = require("react");

// For examples of use, see problemDialog/EmailField.tsx and publish/commonPublish/DeviceAndControls.tsx.
// If your component uses bloomUI.less, the necessary .less rules for the .drawAttention class are already there.
export function useDrawAttention(
    submitAttempts: number,
    validate: () => boolean
) {
    const [attentionClass, setAttentionClass] = React.useState("");
    React.useEffect(() => {
        if (submitAttempts > 0 && !validate()) {
            setAttentionClass("drawAttention");
            // clear so that the animation can work next time
            window.setTimeout(() => setAttentionClass(""), 1000);
        }
    }, [submitAttempts]);
    return attentionClass;
}
