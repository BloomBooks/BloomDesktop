import * as React from "react";

interface IContentEditableProps {
    content: string;
    onChange: (content: string) => void;
    onEnterKeyPressed: () => void;
}

// A simple div with plain text content that can be edited.
// Property onChange is a function that triggers when the editable content changes.
// If onEnterKeyPressed is set, it will be called when the user types that key.
// content property is the current text of the div.
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
                onInput={event => this.emitChange(event)}
                onBlur={event => this.emitChange(event)}
                onKeyPress={event => {
                    if (
                        event.which === 13 &&
                        this.props.onEnterKeyPressed != null
                    ) {
                        event.stopPropagation();
                        event.preventDefault();
                        this.props.onEnterKeyPressed();
                    }
                }}
                contentEditable={true}
            >
                {this.props.content}
            </div>
        );
    }

    // The idea here is to minimise updating the div when content didn't really change, to reduce
    // the frequency with which the cursor gets messed up and (hopefully) restored. I'm not sure how
    // much it helps; it wasn't enough without the componentDidUpdate trick.
    public shouldComponentUpdate(nextProps) {
        let result = nextProps.content !== this.props.content;
        return result;
    }

    public componentDidUpdate() {
        const sel = window.getSelection();
        if (!sel || this.ipNode !== sel.anchorNode) {
            // no selection => nothing to do
            // updated for some other reason than user editing...don't mess with window selection.
            return;
        }
        // restore the cursor position we saved when raising onChange.
        var range = document.createRange();
        if (this.ipNode) {
            range.setStart(this.ipNode, this.ipPosition);
            range.setEnd(this.ipNode, this.ipPosition);
        }
        sel.removeAllRanges();
        sel.addRange(range);
    }

    private emitChange(event: React.FormEvent<HTMLDivElement>) {
        var content: string = event.currentTarget.innerText;
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
