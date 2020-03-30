import React = require("react");

// For examples of use, see problemDialog/EmailField.tsx and publish/commonPublish/DeviceAndControls.tsx.
// Don't for get to add the two rules to the appropriate .less file too.
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
