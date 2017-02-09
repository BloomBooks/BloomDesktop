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
        $(targetBox).on('click.formatButton', '.formatButton', () => {
            axios.get<string>('/bloom/bookEdit/TextBoxProperties/TextBoxProperties.html').then(result => {
                var html = result.data;
                propDlg.boxBeingEdited = targetBox;

                // If this text box has had properties set already, get them so we can setup the dialog contents
                // if not already set, this will return 'Auto'
                var languageGroup = propDlg.getTextBoxLanguage(targetBox);

                $('#text-properties-dialog').remove(); // in case there's still one somewhere else
                $('body').append(html);

                if (noFormatChange) {
                    $('#text-properties-dialog').addClass('formattingDisabled');
                } else {
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
                setTimeout(() => {
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
                    this.removeButtonSelection();
                    this.initializeAlignment();
                    this.initializeBorderStyle();
                    this.initializeBackground();
                    this.setButtonClickActions();
                }, 0); // just push this to the end of the event queue
            });
        });
    }

    getButtonIds() {
        return ['align-top', 'align-center', 'align-bottom',
            'border-none', 'border-black', 'border-black-round', 'border-gray', 'border-gray-round',
            'background-none', 'background-light-gray', 'background-gray', 'background-black'];
    }

    removeButtonSelection() {
        var buttonIds = this.getButtonIds();
        for (var i = 0; i < buttonIds.length; i++) {
            $('#' + buttonIds[i]).removeClass('selectedIcon');
        }
    }

    setButtonClickActions() {
        var buttonIds = this.getButtonIds();
        for (var idIndex = 0; idIndex < buttonIds.length; idIndex++) {
            var button = $('#' + buttonIds[idIndex]);
            button.click((event) => {
                this.buttonClick(event.target.parentElement);
            });
        }
    }
    buttonClick(buttonDiv) {
        var button = $(buttonDiv);
        var id = button.attr('id');
        var index = id.indexOf('-');
        if (index >= 0) {
            // buttons in a group are given ids starting with group-
            button.addClass('selectedIcon');
            var group = id.substring(0, index);
            $('.propButton').each((index, b) => {
                var item = $(b);
                if (b !== button.get(0) && item.attr('id').startsWith(group)) {
                    item.removeClass('selectedIcon');
                }
            });
        } else {
            // button is not part of a group, so must toggle
            // (not used yet)
            // if (button.hasClass('selectedIcon')) {
            //     button.removeClass('selectedIcon');
            // } else {
            //     button.addClass('selectedIcon');
            // }
        }
        // Now make it so
        if (id.startsWith('align')) {
            this.changeAlignment();
        }
        if (id.startsWith('border')) {
            this.changeBorderStyle();
        }
        if (id.startsWith('background')) {
            this.changeBackground();
        }
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

    initializeAlignment() {
        var targetGroup = $(this.getAffectedTranslationGroup(this.boxBeingEdited));
        if (targetGroup) {
            var style = targetGroup.attr('style') || '';
            if (style.indexOf('justify-content:center;') >= 0) {
                $('#align-center').addClass('selectedIcon');
            } else if (style.indexOf('justify-content:flex-end;') >= 0) {
                $('#align-bottom').addClass('selectedIcon');
            } else {
                $('#align-top').addClass('selectedIcon');
            }
        }
    }

    changeAlignment() {
        var targetGroup = $(this.getAffectedTranslationGroup(this.boxBeingEdited));
        if (targetGroup) {
            var style = (targetGroup.attr('style') || '')
                .replace('justify-content:flex-end;', '')
                .replace('justify-content:center;', '');

            if ($('#align-center').hasClass('selectedIcon')) {
                style = style + 'justify-content:center;';
            } else if ($('#align-bottom').hasClass('selectedIcon')) {
                style = style + 'justify-content:flex-end;';
            } // else leave it missing.
            targetGroup.attr('style', style);
        }
    }

    initializeBorderStyle() {
        var targetGroup = $(this.getAffectedTranslationGroup(this.boxBeingEdited));
        if (targetGroup) {
            var style = targetGroup.attr('style') || '';
            if (style.indexOf('border:1pt solid black;border-radius:0px;box-sizing:border-box;') >= 0) {
                $('#border-black').addClass('selectedIcon');
            } else if (style.indexOf('border:1pt solid black;border-radius:10px;box-sizing:border-box;') >= 0) {
                $('#border-black-round').addClass('selectedIcon');
            } else if (style.indexOf('border:1pt solid gray;border-radius:0px;box-sizing:border-box;') >= 0) {
                $('#border-gray').addClass('selectedIcon');
            } else if (style.indexOf('border:1pt solid gray;border-radius:10px;box-sizing:border-box;') >= 0) {
                $('#border-gray-round').addClass('selectedIcon');
            } else {
                $('#border-none').addClass('selectedIcon');
            }
        }
    }

    changeBorderStyle() {
        var targetGroup = $(this.getAffectedTranslationGroup(this.boxBeingEdited));
        if (!targetGroup) {
            return;
        }
        var style = (targetGroup.attr('style') || '')
            .replace(/border:.+?;/gi, '')
            // REVIEW: need to clear all the options? top, bottom, left, right?
            .replace(/border-style:.+?;/gi, '')
            .replace(/border-color:.+?;/gi, '')
            .replace(/border-radius:.+?;/gi, '')
            .replace(/box-sizing:.+?;/gi, '');

        if ($('#border-black').hasClass('selectedIcon')) {
            style = style + 'border:1pt solid black;';
            style = style + 'border-radius:0px;';
            style = style + 'box-sizing:border-box;';
        } else if ($('#border-black-round').hasClass('selectedIcon')) {
            style = style + 'border:1pt solid black;';
            style = style + 'border-radius:10px;';
            style = style + 'box-sizing:border-box;';
        } else if ($('#border-gray').hasClass('selectedIcon')) {
            style = style + 'border:1pt solid gray;';
            style = style + 'border-radius:0px;';
            style = style + 'box-sizing:border-box;';
        } else if ($('#border-gray-round').hasClass('selectedIcon')) {
            style = style + 'border:1pt solid gray;';
            style = style + 'border-radius:10px;';
            style = style + 'box-sizing:border-box;';
        }
        targetGroup.attr('style', style);
    }

    initializeBackground() {
        var targetGroup = $(this.getAffectedTranslationGroup(this.boxBeingEdited));
        if (targetGroup) {
            var style = targetGroup.attr('style') || '';
            if (style.indexOf('background-color:hsl(0,0%,86%);') >= 0) {
                $('#background-gray').addClass('selectedIcon');
            } else {
                $('#background-none').addClass('selectedIcon');
            }
        }
    }

    changeBackground() {
        var targetGroup = $(this.getAffectedTranslationGroup(this.boxBeingEdited));
        if (!targetGroup) {
            return;
        }
        var style = (targetGroup.attr('style') || '')
            .replace(/background-color:.+?;/gi, '');

        if ($('#background-gray').hasClass('selectedIcon')) {
            style = style + 'background-color:hsl(0,0%,86%);';
        }
        targetGroup.attr('style', style);
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
