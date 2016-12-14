import theOneLocalizationManager from '../../lib/localizationManager/localizationManager';
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

    // This method is called when the origami panel gets focus.
    // targetBox is actually the '.textBox-identifier' div that overlaps with the '.bloom-translationGroup' we want.
    AttachToBox(targetBox: HTMLElement) {
        var propDlg = this;
        this._previousBox = targetBox;

        // Put the format button in the text box itself, so that it's always in the right place.
        $(targetBox).append(propDlg.getDialogActivationButton());

        // BL-2476: Readers made from BloomPacks should have formatting dialogs disabled
        var suppress = $(document).find('meta[name="lockFormatting"]');
        var noFormatChange = suppress.length > 0 && suppress.attr('content').toLowerCase() === 'true';

        // Why do we use .off and .on? See comment in a nearly identical location in StyleEditor.ts
        $(targetBox).off('click.formatButton');
        $(targetBox).on('click.formatButton', '.formatButton', function () {
            propDlg.boxBeingEdited = targetBox;

            // If this text box has had properties set already, get them so we can setup the dialog contents
            // if not already set, this will return 'Auto'
            var languageGroup = propDlg.getTextBoxLanguage(targetBox);

            var html = '<div id="text-properties-dialog" class="bloom-ui bloomDialogContainer">'
                + '<div data-i18n="EditTab.TextBoxProperties.Title" class="bloomDialogTitleBar">Text Box Properties</div>';
            if (noFormatChange) {
                // Review gjm: Is this true for this dialog?
                var translation = theOneLocalizationManager.getText('BookEditor.FormattingDisabled', 'Sorry, Reader Templates do not allow changes to formatting.');
                html += '<div class="bloomDialogMainPage"><p>' + translation + '</p></div>';
            }
            else {
                html += '<div class="tab-pane" id="tabRoot">';
                html += '<div class="tab-page">'
                    + '<h2 class="tab" data-i18n="EditTab.TextBoxProperties.LanguageTab">Language</h2>'
                    + '<div style="width: 420px; font-size: 8pt; margin-top: 10px;" data-i18n="EditTab.TextBoxProperties.NormalLabel">'
                    + '"Normal" will show local language, and potentially regional or national, depending on the multilingual settings of the book. Use this for most content.'
                    + '</div>'
                    + '<div id="language-group">'
                    + '<input id="tbprop-normal" type="radio" style="margin-left: 5px;" name="languageRadioGroup" value="Auto"'
                    + propDlg.getCheckedValue(languageGroup, "Auto")
                    + '/><label for="tbprop-normal" data-i18n="EditTab.TextBoxProperties.Normal" style="margin-left: 7px;">Normal</label><br/>'
                    + '<div style="width: 420px; font-size: 8pt; margin-top: 10px;" data-i18n="EditTab.TextBoxProperties.OtherLanguagesLabel">'
                    + 'Use one of these for simple text boxes that are always in only one language.'
                    + '</div>'
                    + '<input id="tbprop-vernacular" type="radio" style="margin-left: 5px;" name="languageRadioGroup" value="V"'
                    + propDlg.getCheckedValue(languageGroup, "V")
                    + '/><label for="tbprop-vernacular" data-i18n="EditTab.TextBoxProperties.LocalLanguage" style="margin-left: 7px;">Local Language</label><br/>'
                    + '<input id="tbprop-national" type="radio" style="margin-left: 5px;" name="languageRadioGroup" value="N1"'
                    + propDlg.getCheckedValue(languageGroup, "N1")
                    + '/><label for="tbprop-national" data-i18n="EditTab.TextBoxProperties.NationalLanguage" style="margin-left: 7px;">National Language</label><br/>'
                    + '<input id="tbprop-regional" type="radio" style="margin-left: 5px;" name="languageRadioGroup" value="N2"'
                    + propDlg.getCheckedValue(languageGroup, "N2")
                    + '/><label for="tbprop-regional" data-i18n="EditTab.TextBoxProperties.RegionalLanguage" style="margin-left: 7px;">Regional Language</label><br/>'
                    + '</div>' // end of #language-group div
                    + '</div>'; // end of Language tab-page div
                // Here follows a skeleton implementation of tabs 2 and 3
                // (Don't forget to add the appropriate bits to the .tmx files when activated.)
                // html += '<div class="tab-page" id="bordersPage"><h2 class="tab" data-i18n="EditTab.TextBoxProperties.BordersTab">Borders</h2>'
                //     // + propDlg.makeBordersContent(currentBorder)
                //     + '</div>' // end of tab-page div for borders tab
                //     + '<div class="tab-page" id="bubblesPage"><h2 class="tab" data-i18n="EditTab.TextBoxProperties.BubblesTab">Hint Bubbles</h2>'
                //     // + propDlg.makeBubblesContent(currentBubbles)
                //     + '</div>' // end of tab-page div for hint bubbles tab
                html += '</div>'; // end of tab-pane div
            }
            html += '</div>'; // end of text-properties-dialog div
            $('#text-properties-dialog').remove(); // in case there's still one somewhere else
            $('body').append(html);

            var dialogElement = $('#text-properties-dialog');
            dialogElement.find('*[data-i18n]').localize();
            dialogElement.draggable({ distance: 10, scroll: false, containment: $('html') });
            dialogElement.draggable("disable"); // until after we make sure it's in the Viewport
            dialogElement.css('opacity', 1.0);
            if (!noFormatChange) {
                // Hook up change event handlers
                $('#language-group').change(function () { propDlg.changeLanguageGroup(); });
                new WebFXTabPane($('#tabRoot').get(0), false, null);
            }
            // Give the browser time to get the dialog into the DOM first, before doing this stuff
            // It just needs to delay one 'cycle'.
            // http://stackoverflow.com/questions/779379/why-is-settimeoutfn-0-sometimes-useful
            setTimeout(function () {
                var offset = $(propDlg.boxBeingEdited).find('.formatButton').offset(); // make sure we get the right button!
                dialogElement.offset({ left: offset.left + 30, top: offset.top - 30 });
                EditableDivUtils.positionInViewport(dialogElement);
                dialogElement.draggable("enable");

                $('html').off('click.dialogElement');
                $('html').on("click.dialogElement", function (event) {
                    if (event.target !== dialogElement.get(0) &&
                        dialogElement.has(event.target).length === 0 &&
                        $(event.target).parent() !== dialogElement &&
                        dialogElement.has(event.target).length === 0 &&
                        dialogElement.is(":visible")) {
                        dialogElement.remove();
                        event.stopPropagation();
                        event.preventDefault();
                    }
                });
                dialogElement.on("click.dialogElement", function (event) {
                    // this stops an event inside the dialog from propagating to the html element, which would close the dialog
                    event.stopPropagation();
                });
            }, 0); // just push this to the end of the event queue
        })
    }

    changeLanguageGroup() {
        // get radio button value and set 'data-default-languages' attribute
        var radioValue = $('input[name="languageRadioGroup"]:checked').val();
        var targetGroup = $(this.getAffectedTranslationGroup(this.boxBeingEdited));
        // currently 'radioValue' should be one of: 'Auto', 'N1', 'N2', or 'V'
        if (targetGroup)
            targetGroup.attr('data-default-languages', radioValue);
    }

    getTextBoxLanguage(targetBox: HTMLElement): string {
        var targetGroup = $(this.getAffectedTranslationGroup(targetBox));
        if (!targetGroup || !targetGroup.hasAttr('data-default-languages')) return "Auto";
        var result = targetGroup.attr('data-default-languages');
        const acceptable = ['Auto', 'N1', 'N2', 'V'];
        return acceptable.indexOf(result) > -1 ? result : 'Auto';
    }

    getAffectedTranslationGroup(targetBox: HTMLElement): HTMLElement {
        var container = $(targetBox).parent();
        // I'm not sure (gjm) how often another translationGroup with box-header-off shows up,
        // but I found at least one instance, so make sure that's not the one we grab.
        return container.find('.bloom-translationGroup:not(.box-header-off)')[0];
    }

    getCheckedValue(value: string, compareTo: string): string {
        return (value == compareTo) ? ' checked="true"' : '';
    }

    // The z-index puts the formatButton above the origami-ui stuff so a click will find it.
    getDialogActivationButton(): string {
        return '<div style="z-index: 60000; position: absolute; left:0; bottom: 0;" contenteditable="false" class="bloom-ui formatButton">'
            + '<img  contenteditable="false" src="' + this._supportFilesRoot + '/img/cogGrey.svg"></div>';
    }
}
