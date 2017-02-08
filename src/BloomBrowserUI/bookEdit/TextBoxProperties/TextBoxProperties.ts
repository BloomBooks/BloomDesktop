import theOneLocalizationManager from '../../lib/localizationManager/localizationManager';
import axios = require('axios');
import { EditableDivUtils } from '../js/editableDivUtils';

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
            axios.get<string>('/bloom/textBoxPropertiesContent').then(result => {
                var html = result.data;
                propDlg.boxBeingEdited = targetBox;

                // If this text box has had properties set already, get them so we can setup the dialog contents
                // if not already set, this will return 'Auto'
                var languageGroup = propDlg.getTextBoxLanguage(targetBox);

                $('#text-properties-dialog').remove(); // in case there's still one somewhere else
                $('body').append(html);

                if (noFormatChange) {
                    $('.formattingEnabled').hide();
                } else {
                    $('.formattingDisabled').hide();
                    $(':radio[name=languageRadioGroup][value=' + languageGroup + ']').prop('checked', true);
                }

                var dialogElement = $('#text-properties-dialog');
                dialogElement.find('*[data-i18n]').localize();
                dialogElement.draggable({ distance: 10, scroll: false, containment: $('html') });
                dialogElement.draggable('disable'); // until after we make sure it's in the Viewport
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
                    dialogElement.draggable('enable');

                    $('html').off('click.dialogElement');
                    $('html').on('click.dialogElement', function (event) {
                        if (event.target !== dialogElement.get(0) &&
                            dialogElement.has(event.target).length === 0 &&
                            $(event.target).parent() !== dialogElement &&
                            dialogElement.has(event.target).length === 0 &&
                            dialogElement.is(':visible')) {
                            dialogElement.remove();
                            event.stopPropagation();
                            event.preventDefault();
                        }
                    });
                    dialogElement.on('click.dialogElement', function (event) {
                        // this stops an event inside the dialog from propagating to the html element, which would close the dialog
                        event.stopPropagation();
                    });
                }, 0); // just push this to the end of the event queue
            });
        });
    }

    changeLanguageGroup() {
        // get radio button value and set 'data-default-languages' attribute
        var radioValue = $('input[name="languageRadioGroup"]:checked').val();
        var targetGroup = $(this.getAffectedTranslationGroup(this.boxBeingEdited));
        // currently 'radioValue' should be one of: 'Auto', 'N1', 'N2', or 'V'
        if (targetGroup) {
            targetGroup.attr('data-default-languages', radioValue);
        }
    }

    getTextBoxLanguage(targetBox: HTMLElement): string {
        var targetGroup = $(this.getAffectedTranslationGroup(targetBox));
        if (!targetGroup || !targetGroup.hasAttr('data-default-languages')) {
            return 'Auto';
        }
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

    // The z-index puts the formatButton above the origami-ui stuff so a click will find it.
    getDialogActivationButton(): string {
        return '<div contenteditable="false" class="bloom-ui formatButton">'
            + '<img  contenteditable="false" src="' + this._supportFilesRoot + '/img/cogGrey.svg"></div>';
    }
}
