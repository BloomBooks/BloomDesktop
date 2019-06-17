import React = require("react");

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
