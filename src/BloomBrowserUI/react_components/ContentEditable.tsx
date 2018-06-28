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
    lastContent: string;
    editDiv: HTMLDivElement;
    ipPosition: number;
    ipNode: Node;

    constructor(props: IContentEditableProps) {
        super(props);
    }
    render() {
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
                contentEditable
                ref={div => {
                    this.editDiv = div;
                }}
            >
                {this.props.content}
            </div>
        );
    }

    // The idea here is to minimise updating the div when content didn't really change, to reduce
    // the frequency with which the cursor gets messed up and (hopefully) restored. I'm not sure how
    // much it helps; it wasn't enough without the componentDidUpdate trick.
    shouldComponentUpdate(nextProps) {
        let result = nextProps.content !== this.props.content;
        return result;
    }

    componentDidUpdate() {
        if (this.ipNode !== window.getSelection().anchorNode) {
            // updated for some other reason than user editing...don't mess with window selection.
            return;
        }
        // restore the cursor position we saved when raising onChange.
        var range = document.createRange();
        range.setStart(this.ipNode, this.ipPosition);
        range.setEnd(this.ipNode, this.ipPosition);
        let sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(range);
    }

    emitChange(event: React.FormEvent<HTMLDivElement>) {
        var content: string = event.currentTarget.innerText;
        if (this.props.onChange && content !== this.lastContent) {
            // onChange will re-render, messing up the cursor position. So save it.
            this.ipPosition = window.getSelection().anchorOffset;
            this.ipNode = window.getSelection().anchorNode;
            this.props.onChange(content);
        }
        this.lastContent = content;
    }
}
