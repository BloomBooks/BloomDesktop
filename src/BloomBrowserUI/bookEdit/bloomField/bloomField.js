var BloomField = (function () {
    function BloomField() {
    }
    BloomField.ManageField = function (bloomEditableDiv) {
        BloomField.PreventRemovalOfSomeElements(bloomEditableDiv);

        if (BloomField.RequiresParagraphs(bloomEditableDiv)) {
            BloomField.ModifyForParagraphMode(bloomEditableDiv);
            BloomField.ManageWhatHappensIfTheyDeleteEverything(bloomEditableDiv);
            BloomField.PreventArrowingOutIntoField(bloomEditableDiv);
            BloomField.PreventBackspaceAtStartFromMovingTextIntoEmbeddedImageCaption(bloomEditableDiv);
            BloomField.MakeTabEnterTabElement(bloomEditableDiv);
            BloomField.MakeShiftEnterInsertLineBreak(bloomEditableDiv);
            $(bloomEditableDiv).on('paste', this.ProcessIncomingPaste);
            $(bloomEditableDiv).click(function () {
                this.ProcessClick;
            });
            $(bloomEditableDiv).blur(function () {
                BloomField.ModifyForParagraphMode(this);
            });
            $(bloomEditableDiv).focusin(function () {
                BloomField.HandleFieldFocus(this);
            });
        } else {
            BloomField.PrepareNonParagraphField(bloomEditableDiv);
        }
    };

    BloomField.MakeTabEnterTabElement = function (field) {
        $(field).keydown(function (e) {
            if (e.which === 9) {
                document.execCommand("insertHTML", false, "&emsp;");
                e.stopPropagation();
                e.preventDefault();
            }
        });
    };

    BloomField.MakeShiftEnterInsertLineBreak = function (field) {
        $(field).keypress(function (e) {
            if (e.which == 13) {
                if (e.shiftKey) {
                    document.execCommand("insertHTML", false, "<span class='bloom-linebreak'></span>&#xfeff;");
                } else {
                    document.execCommand("insertHTML", false, "<p>&zwnj;</p>");
                }
                e.stopPropagation();
                e.preventDefault();
            }
        });
    };

    BloomField.ProcessClick = function (e) {
        var txt = e.originalEvent.clipboardData.getData('text/plain');

        var html;
        if (e.ctrlKey) {
            html = txt.replace(/\n\n/g, 'twonewlines');
            html = html.replace(/\n/g, ' ');
            html = html.replace(/\s+/g, ' ');
            html = html.replace(/twonewlines/g, '\n');

            html = html.replace(/\n/g, '</p><p>');

            document.execCommand("insertHTML", false, html);

            e.stopPropagation();
            e.preventDefault();
        }
    };

    BloomField.ProcessIncomingPaste = function (e) {
        var txt = e.originalEvent.clipboardData.getData('text/plain');

        var html;

        if (e.ctrlKey) {
            html = txt.replace(/\n\n/g, 'twonewlines');
            html = html.replace(/\n/g, ' ');
            html = html.replace(/\s+/g, ' ');
            html = html.replace(/twonewlines/g, '\n');
        } else {
            html = txt.replace(/\n\s{3,}/g, ' ');
        }

        html = html.replace(/\n/g, '</p><p>');

        document.execCommand("insertHTML", false, html);

        e.stopPropagation();
        e.preventDefault();
    };

    BloomField.PreventBackspaceAtStartFromMovingTextIntoEmbeddedImageCaption = function (field) {
        $(field).keydown(function (e) {
            if (e.which == 8) {
                var sel = window.getSelection();

                if (sel.anchorOffset == 0 && $(sel.anchorNode).closest('P').prev().hasClass('bloom-preventRemoval')) {
                    e.stopPropagation();
                    e.preventDefault();
                    console.log("Prevented Backspace");
                }
            }
        });
    };

    BloomField.PreventArrowingOutIntoField = function (field) {
        $(field).keydown(function (e) {
            var leftArrowPressed = e.which === 37;
            var rightArrowPressed = e.which === 39;
            if (leftArrowPressed || rightArrowPressed) {
                var sel = window.getSelection();
                if (sel.anchorNode === this) {
                    e.preventDefault();
                    BloomField.MoveCursorToEdgeOfField(this, leftArrowPressed ? 0 /* start */ : 1 /* end */);
                }
            }
        });
    };

    BloomField.EnsureStartsWithParagraphElement = function (field) {
        if ($(field).children().length > 0 && ($(field).children().first().prop("tagName").toLowerCase() === 'p')) {
            return;
        }
        $(field).prepend('<p></p>');
    };

    BloomField.EnsureEndsWithParagraphElement = function (field) {
        if ($(field).children().length > 0 && ($(field).children().last().prop("tagName").toLowerCase() === 'p')) {
            return;
        }
        $(field).append('<p></p>');
    };

    BloomField.ConvertTopLevelTextNodesToParagraphs = function (field) {
        var nodes = field.childNodes;
        for (var n = 0; n < nodes.length; n++) {
            var node = nodes[n];
            if (node.nodeType === 3) {
                var paragraph = document.createElement('p');
                if (node.textContent.trim() !== '') {
                    paragraph.textContent = node.textContent;
                    node.parentNode.insertBefore(paragraph, node);
                }
                node.parentNode.removeChild(node);
            }
        }
    };

    BloomField.ModifyForParagraphMode = function (field) {
        BloomField.ConvertTopLevelTextNodesToParagraphs(field);
        $(field).find('br').remove();

        if ($(field).find('.bloom-keepFirstInField').length > 0) {
            BloomField.EnsureEndsWithParagraphElement(field);
            return;
        } else {
            BloomField.EnsureStartsWithParagraphElement(field);
        }
    };

    BloomField.RequiresParagraphs = function (field) {
        return $(field).closest('.bloom-requiresParagraphs').length > 0 || ($(field).css('border-top-style') === 'dashed');
    };

    BloomField.HandleFieldFocus = function (field) {
        BloomField.MoveCursorToEdgeOfField(field, 0 /* start */);
    };

    BloomField.MoveCursorToEdgeOfField = function (field, position) {
        var range = document.createRange();
        if (position === 0 /* start */) {
            range.selectNodeContents($(field).find('p').first()[0]);
        } else {
            range.selectNodeContents($(field).find('p').last()[0]);
        }
        range.collapse(position === 0 /* start */);
        var sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(range);
    };

    BloomField.ManageWhatHappensIfTheyDeleteEverything = function (field) {
        $(field).on("input", function (e) {
            if ($(this).find('p').length === 0) {
                BloomField.ModifyForParagraphMode(this);

                BloomField.MoveCursorToEdgeOfField(field, 1 /* end */);
            }
        });
    };

    BloomField.PreventRemovalOfSomeElements = function (field) {
        var numberThatShouldBeThere = $(field).find(".bloom-preventRemoval").length;
        if (numberThatShouldBeThere > 0) {
            $(field).on("input", function (e) {
                if ($(this).find(".bloom-preventRemoval").length < numberThatShouldBeThere) {
                    document.execCommand('undo');
                    e.preventDefault();
                }
            });
        }

        $(field).blur(function (e) {
            if ($(this).html().indexOf('RESETRESET') > -1) {
                $(this).remove();
                alert("Now go to another book, then back to this book and page.");
            }
        });
    };

    BloomField.PrepareNonParagraphField = function (field) {
        if ($(field).text() === '') {
            $(field).html('&nbsp;');

            $(field).one('paste keypress', this.FixUpOnFirstInput);
        }
    };

    BloomField.FixUpOnFirstInput = function (event) {
        var field = event.target;

        if ($(field).html().indexOf("&nbsp;") === 0) {
            var selection = window.getSelection();

            if (selection.anchorOffset === 0) {
                selection.modify("extend", "forward", "character");

                selection.deleteFromDocument();
            } else if (selection.anchorOffset === 1) {
                selection.modify("extend", "backward", "character");
            }
        }
    };
    return BloomField;
})();
var CursorPosition;
(function (CursorPosition) {
    CursorPosition[CursorPosition["start"] = 0] = "start";
    CursorPosition[CursorPosition["end"] = 1] = "end";
})(CursorPosition || (CursorPosition = {}));
