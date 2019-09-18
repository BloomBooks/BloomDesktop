import * as React from "react";

interface IContentEditableProps {
    content: string;
    onChange: (content: string) => void;
    onEnterKeyPressed: () => void;
}

// A simple div with plain text content that can be edited.
// Property onChange is a function that triggers when the editable content changes.
// Don't call onChange from onKeyPressed, unless Enter has been pressed; we don't want
// a total refresh on every key stroke.
// If onEnterKeyPressed is set, it will be called when the user types that key.
// The content property is the current text of the div.
export default class ContentEditable extends React.Component<
    IContentEditableProps,
    {}
> {
    private lastContent: string;
    private ipPosition: number;
    private ipNode: Node | null;

    constructor(props: IContentEditableProps) {
        super(props);
    }
    public render() {
        return (
            <div
                id="contenteditable"
                // Don't use onInput. It fires after every key stroke, which is bad when you're trying
                // to enter a hex value.
                onBlur={event => this.emitChange(event)}
                onKeyPress={event => {
                    if (
                        event.which === 13 &&
                        this.props.onEnterKeyPressed != null
                    ) {
                        event.stopPropagation();
                        event.preventDefault();
                        this.emitChange(event);
                        this.props.onEnterKeyPressed();
                    }
                }}
                suppressContentEditableWarning={true}
                contentEditable={true}
            >
                {this.props.content}
            </div>
        );
    }

    // The idea here is to minimise updating the div when content didn't really change, to reduce
    // the frequency with which the cursor gets messed up and (hopefully) restored. I'm not sure how
    // much it helps; it wasn't enough without the componentDidUpdate trick.
    public shouldComponentUpdate(nextProps: { content: string }) {
        return nextProps.content !== this.props.content;
    }

    public componentDidUpdate() {
        const sel = window.getSelection();
        if (!sel || this.ipNode !== sel.anchorNode) {
            // no selection => nothing to do
            // updated for some other reason than user editing...don't mess with window selection.
            return;
        }
        // restore the cursor position we saved when raising onChange.
        const range = document.createRange();
        if (this.ipNode) {
            range.setStart(this.ipNode, this.ipPosition);
            range.setEnd(this.ipNode, this.ipPosition);
        }
        sel.removeAllRanges();
        sel.addRange(range);
    }

    private emitChange(event: React.FormEvent<HTMLDivElement>) {
        const content: string = event.currentTarget.innerText;
        if (this.props.onChange && content !== this.lastContent) {
            // onChange will re-render, messing up the cursor position. So save it.
            const sel = window.getSelection();
            if (sel) {
                this.ipPosition = sel.anchorOffset;
                this.ipNode = sel.anchorNode;
            }
            this.props.onChange(content);
        }
        this.lastContent = content;
    }
}
