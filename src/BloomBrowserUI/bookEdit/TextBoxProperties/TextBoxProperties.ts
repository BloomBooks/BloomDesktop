import theOneLocalizationManager from '../../lib/localizationManager/localizationManager';
import axios = require("axios");
import { EditableDivUtils } from "../js/editableDivUtils";

declare function WebFxTabPane(element: HTMLElement, useCookie: boolean, callback: any): any; // from tabpane, from a <script> tag

export default class TextBoxProperties {

    private _previousBox: Element;
    private boxBeingEdited: HTMLElement;
    private _supportFilesRoot: string;
    private ignoreControlChanges: boolean;
    private authorMode: boolean; // true if authoring (rather than translating)

    constructor(supportFilesRoot: string) {
        this._supportFilesRoot = supportFilesRoot;
    }

    AttachToBox(targetBox: HTMLElement) {
        // This method is called when the window gets focus. This may be before CkEditor has finished loading.
        // Somewhere in the course of loading, it detects editable divs that are empty except for our gear icon.
        // It decides to insert some content...typically <p><br></p>, and in doing so, replaces the gear icon div.
        // Attempts to suppress this with  config.fillEmptyBlocks, config.protectedSource,
        // config.allowedContent, and data-cke-survive did not work.
        // The only solution we have found is to postpone adding the gear icon until CkEditor has done
        // its nefarious work. The following block achieves this.
        // gjm: Refactored it into EditableDivUtils.
        EditableDivUtils.WaitForCKEditorReady(window, targetBox, this.AttachToBox);

        var propDlg = this;
        this._previousBox = targetBox;

        // put the format button in the text box itself, so that it's always in the right place.
        $(targetBox).append('<div id="formatButton" contenteditable="false" class="bloom-ui"><img  contenteditable="false" src="' + propDlg._supportFilesRoot + '/img/cogGrey.svg"></div>');

        var formatButton = $('#formatButton');

        // BL-2476: Readers made from BloomPacks should have formatting dialogs disabled
        var suppress = $(document).find('meta[name="lockFormatting"]');
        var noFormatChange = suppress.length > 0 && suppress.attr('content').toLowerCase() === 'true';

        // Why do we use .off and .on? See comment in a nearly identical location in StyleEditor.ts
        $(targetBox).off('click.formatButton');
        $(targetBox).on('click.formatButton', '#formatButton', function () {
            // Review (gjm): We don't really need font names for this dialog, but when I remove this, the dialog
            // no longer works; ideas?
            axios.get('/bloom/availableFontNames').then(result => {
                propDlg.boxBeingEdited = targetBox;

                // If this text box has had properties set already, get them so we can setup the dialog contents
                // if not already set, this will return 'auto'
                var languageGroup = propDlg.getTextBoxLanguage(targetBox);

                var html = '<div id="format-toolbar" class="bloom-ui bloomDialogContainer">'
                    + '<div data-i18n="EditTab.TextBoxProperties.Title" class="bloomDialogTitleBar">Text Box Properties</div>';
                if (noFormatChange) {
                    // Review gjm: Is this true for this dialog?
                    var translation = theOneLocalizationManager.getText('BookEditor.FormattingDisabled', 'Sorry, Reader Templates do not allow changes to formatting.');
                    html += '<div class="bloomDialogMainPage"><p>' + translation + '</p></div>';
                }
                else {
                    html += '<div class="tab-pane" id="tabRoot">';
                    html += '<div class="tab-page"><h2 class="tab" data-i18n="EditTab.TextBoxProperties.LanguageTab">Language</h2>'
                        + propDlg.makeDiv(null, null, 'width: 420px; font-size: 8pt; margin-top: 10px;', 'EditTab.TextBoxProperties.NormalLabel',
                            '"Normal" will show local language, and potentially regional or national, depending on the bilingual settings of the book. Use this for most content.')
                        + propDlg.makeDiv('language-group', 'state-initial', null, null,
                            propDlg.makeRadio('languageRadioGroup', ' Normal', 'auto', languageGroup == 'auto')
                            + propDlg.makeDiv(null, null, 'width: 420px; font-size: 8pt; margin-top: 10px;', 'EditTab.TextBoxProperties.OtherLanguagesLabel',
                                'Use one of these for simple text boxes that are always in only one language.')
                            + propDlg.makeRadio('languageRadioGroup', ' National Language', 'N1', languageGroup == 'N1')
                            + propDlg.makeRadio('languageRadioGroup', ' Regional Language', 'N2', languageGroup == 'N2')
                            + propDlg.makeRadio('languageRadioGroup', ' Local Language', 'V', languageGroup == 'V'))
                        + "</div>"; // end of Language tab-page div
                    // Here follows a skeleton implementation of tabs 2 and 3
                    // html += '<div class="tab-page" id="bordersPage"><h2 class="tab" data-i18n="EditTab.TextBoxProperties.BordersTab">Borders</h2>'
                    //     // + propDlg.makeBordersContent(currentBorder)
                    //     + '</div>' // end of tab-page div for borders tab
                    //     + '<div class="tab-page" id="bubblesPage"><h2 class="tab" data-i18n="EditTab.TextBoxProperties.BubblesTab">Hint Bubbles</h2>'
                    //     // + propDlg.makeBubblesContent(currentBubbles)
                    //     + '</div>' // end of tab-page div for 'more' tab
                    //     + '</div>'; // end of tab-pane div
                }
                html += '</div>'; // end of format-toolbar div
                $('#format-toolbar').remove(); // in case there's still one somewhere else
                $('body').append(html);

                var toolbar = $('#format-toolbar');
                toolbar.find('*[data-i18n]').localize();
                toolbar.draggable({ distance: 10, scroll: false, containment: $('html') });
                toolbar.draggable("disable"); // until after we make sure it's in the Viewport
                toolbar.css('opacity', 1.0);
                if (!noFormatChange) {
                    // Hook up change event handlers

                    $('#language-group').change(function () { propDlg.changeLanguageGroup(); });
                    new WebFXTabPane($('#tabRoot').get(0), false, null);
                }
                var offset = $('#formatButton').offset();
                toolbar.offset({ left: offset.left + 30, top: offset.top - 30 });
                TextBoxProperties.positionInViewport(toolbar);
                toolbar.draggable("enable");

                $('html').off('click.toolbar');
                $('html').on("click.toolbar", function (event) {
                    if (event.target !== toolbar.get(0) &&
                        toolbar.has(event.target).length === 0 &&
                        $(event.target).parent() !== toolbar &&
                        toolbar.has(event.target).length === 0 &&
                        toolbar.is(":visible")) {
                        toolbar.remove();
                        event.stopPropagation();
                        event.preventDefault();
                    }
                });
                toolbar.on("click.toolbar", function (event) {
                    // this stops an event inside the dialog from propagating to the html element, which would close the dialog
                    event.stopPropagation();
                });
            });
        })
    }

    /**
     * Positions the Text Box Properties dialog so that it is completely visible, so that it does not extend below the
     * current viewport.
     * @param toolbar
     */
    static positionInViewport(toolbar: JQuery): void {

        // get the current size and position of the toolbar
        var elem: HTMLElement = toolbar[0];
        var top = elem.offsetTop;
        var height = elem.offsetHeight;

        // get the top of the toolbar in relation to the top of its containing elements
        while (elem.offsetParent) {
            elem = <HTMLElement>elem.offsetParent;
            top += elem.offsetTop;
        }

        // diff is the portion of the toolbar that is below the viewport
        var diff = (top + height) - (window.pageYOffset + window.innerHeight);
        if (diff > 0) {
            var offset = toolbar.offset();

            // the extra 30 pixels is for padding
            toolbar.offset({ left: offset.left, top: offset.top - diff - 30 });
        }
    }

    makeDiv(id: string, className: string, style: string, i18nAttr: string, content: string): string {
        var result = '<div';
        if (id) result += ' id="' + id + '"';
        if (className) result += ' class="' + className + '"';
        if (i18nAttr) result += ' data-i18n="' + i18nAttr + '"';
        if (style) result += ' style="' + style + '"';
        result += '>';
        if (content) result += content;
        return result + '</div>';
    }

    makeRadio(groupName: string, buttonLabel: string, value: string, checked: boolean): string {
        var result = '<input type="radio" style="margin-left: 5px;"';
        result += ' name="' + groupName + '"'; // can't NOT have a group name for radio buttons
        if (value) result += ' value="' + value + '"';
        if (checked) result += ' checked="' + checked + '"';
        result += '>';
        if (buttonLabel) result += buttonLabel;
        return result + '</input><br/>';
    }

    changeLanguageGroup() {
        // get radio button value and set 'data-default-languages' attribute
        var radioValue = $('input[name="languageRadioGroup"]:checked').val();
        var targetGroup = $(this.getAffectedTranslationGroup(this.boxBeingEdited));
        // currently 'radioValue' should be one of: 'auto', 'N1', 'N2', or 'V'
        if (targetGroup)
            targetGroup.attr('data-default-languages', radioValue);
    }

    getTextBoxLanguage(targetBox: HTMLElement): string {
        var targetGroup = $(this.getAffectedTranslationGroup(targetBox));
        if (!targetGroup || !targetGroup.hasAttr('data-default-languages')) return "auto";
        return targetGroup.attr('data-default-languages');
    }

    getAffectedTranslationGroup(targetBox: HTMLElement): HTMLElement {
        var container = $(targetBox).parent();
        // I'm not sure (gjm) how often another translationGroup with box-header-off shows up,
        // but I found at least one instance, so make sure that's not the one we grab.
        return container.find('.bloom-translationGroup:not(.box-header-off)')[0];
    }
}
