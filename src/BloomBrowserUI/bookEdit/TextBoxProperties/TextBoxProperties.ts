import theOneLocalizationManager from '../../lib/localizationManager/localizationManager';
import axios = require("axios");

export default class TextBoxProperties {

    private _previousBox: Element;
    private boxBeingEdited: HTMLElement;
    private _supportFilesRoot: string;
    private ignoreControlChanges: boolean;
    private styles: string[];
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
        // Enhance: this logic is roughly duplicated in toolbox.ts restoreToolboxSettingsWhenCkEditorReady.
        // There may be some way to refactor it into a common place, but I don't know where.
        var editorInstances = (<any>window).CKEDITOR.instances;
        for (var i = 1; ; i++) {
            var instance = editorInstances['editor' + i];
            if (instance == null) {
                if (i === 0) {
                    // no instance at all...if one is later created, get us invoked.
                    (<any>window).CKEDITOR.on('instanceReady', e => this.AttachToBox(targetBox));
                    return;
                }
                break; // if we get here all instances are ready
            }
            if (!instance.instanceReady) {
                instance.on('instanceReady', e => this.AttachToBox(targetBox));
                return;
            }
        }

        // var styleName = TextBoxProperties.GetStyleNameForElement(targetBox);
        // if (!styleName)
        //     return;
        var propDlg = this;
        // I'm assuming here that since we're dealing with a local server, we'll get a result long before
        // the user could actually modify a style and thus need the information.
        // More dangerous is using it in getCharTabDescription. But as that is launched by a later
        // async request, I think it should be OK.

        axios.get('/bloom/authorMode').then(result => {
            propDlg.authorMode = result.data == true;
        });
        // propDlg.xmatterMode = this.IsPageXMatter(targetBox);

        // if (this._previousBox != null) {
        //     TextBoxProperties.CleanupElement(this._previousBox);
        // }
        this._previousBox = targetBox;

        $('#format-toolbar').remove(); // in case there's still one somewhere else

        // put the format button in the editable text box itself, so that it's always in the right place.
        // unfortunately it will be subject to deletion because this is an editable box. But we can mark it as uneditable, so that
        // the user won't see resize and drag controls when they click on it
        $(targetBox).append('<div id="formatButton" contenteditable="false" class="bloom-ui"><img  contenteditable="false" src="' + propDlg._supportFilesRoot + '/img/cogGrey.svg"></div>');

        //make the button stay at the bottom if we overflow and thus scroll
        //review: It's not clear to me that this is actually working (JH 3/19/2016)
        //$(targetBox).on("scroll", e => { this.AdjustFormatButton(e.target) });


        // And in case we are starting out on a centerVertically page we might need to adjust it now
        //this.AdjustFormatButton(targetBox);

        var formatButton = $('#formatButton');
        /* we removed this for BL-799, plus it was always getting in the way, once the format popup was opened
        var txt = theOneLocalizationManager.getText('EditTab.FormatDialogTip', 'Adjust formatting for style');
        propDlg.AddQtipToElement(formatButton, txt, 1500);
        */

        // BL-2476: Readers made from BloomPacks should have the formatting dialog disabled
        var suppress = $(document).find('meta[name="lockFormatting"]');
        var noFormatChange = suppress.length > 0 && suppress.attr('content').toLowerCase() === 'true';

        // It is not reliable to attach the click handler directly, as in  $(#formatButton).click(...)
        // I don't know why it doesn't work because even when it fails $(#formatButton).length is 1, so it seems to be
        // finding the right element. But some of the time it doesn't work. See BL-2701. This rather awkard
        // approach is the recommended way to make events fire for dynamically added elements.
        // The .off prevents adding multiple event handlers as the parent box gains focus repeatedly.
        // The namespace (".formatButton") in the event name prevents off from interfering with other click handlers.
        $(targetBox).off('click.formatButton');
        $(targetBox).on('click.formatButton', '#formatButton', function () {
            axios.get('/bloom/availableFontNames').then(result => {
                propDlg.boxBeingEdited = targetBox;
                // This line is needed inside the click function to keep from using a stale version of 'styleName'
                // and chopping off 6 characters each time!
                //styleName = TextBoxProperties.GetStyleNameForElement(targetBox);
                //styleName = styleName.substr(0, styleName.length - 6); // strip off '-style'
                //var current = propDlg.getFormatValues();

                //alert('font: ' + fontName + ' size: ' + sizeString + ' height: ' + lineHeight + ' space: ' + wordSpacing);
                // Enhance: lineHeight may well be something like 35px; what should we select initially?

                var fonts = result.data['fonts'];
                //propDlg.styles = propDlg.getFormattingStyles();
                // if (propDlg.styles.indexOf(styleName) == -1) {
                //     propDlg.styles.push(styleName);
                // }
                // propDlg.styles.sort(function (a: string, b: string) {
                //     return a.toLowerCase().localeCompare(b.toLowerCase());
                // });

                var html = '<div id="format-toolbar" class="bloom-ui bloomDialogContainer">'
                    + '<div data-i18n="EditTab.FormatDialog.Format" class="bloomDialogTitleBar">Format</div>';
                if (noFormatChange) {
                    var translation = theOneLocalizationManager.getText('BookEditor.FormattingDisabled', 'Sorry, Reader Templates do not allow changes to formatting.');
                    html += '<div class="bloomDialogMainPage"><p>' + translation + '</p></div>';
                }
                else if (propDlg.authorMode) {
                    html += '<div class="tab-pane" id="tabRoot">';
                    // if (!propDlg.xmatterMode) {
                    //     html += '<div class="tab-page"><h2 class="tab" data-i18n="EditTab.FormatDialog.StyleNameTab">Style Name</h2>'
                    //         + propDlg.makeDiv(null, null, null, 'EditTab.FormatDialog.Style', 'Style')
                    //         + propDlg.makeDiv("style-group", "state-initial", null, null,
                    //             propDlg.makeSelect(propDlg.styles, styleName, 'styleSelect')
                    //             + propDlg.makeDiv('dont-see', null, null, null,
                    //                 '<span data-i18n="EditTab.FormatDialog.DontSeeNeed">' + "Don't see what you need?" + '</span>'
                    //                 + ' <a id="show-createStyle" href="" data-i18n="EditTab.FormatDialog.CreateStyle">Create a new style</a>')
                    //             + propDlg.makeDiv('createStyle', null, null, null,
                    //                 propDlg.makeDiv(null, null, null, 'EditTab.FormatDialog.NewStyle', 'New style')
                    //                 + propDlg.makeDiv(null, null, null, null, '<input type="text" id="style-select-input"/> <button id="create-button" data-i18n="EditTab.FormatDialog.Create" disabled>Create</button>')
                    //                 + propDlg.makeDiv("please-use-alpha", null, 'color: red;',
                    //                     'EditTab.FormatDialog.PleaseUseAlpha',
                    //                     'Please use only alphabetical characters. Numbers at the end are ok, as in "part2".')
                    //                 + propDlg.makeDiv("already-exists", null, 'color: red;', 'EditTab.FormatDialog.AlreadyExists',
                    //                     'That style already exists. Please choose another name.')))
                    //         + "</div>"; // end of Style Name tab-page div
                    // }
                    html += '<div class="tab-page" id="formatPage"><h2 class="tab" data-i18n="EditTab.FormatDialog.CharactersTab">Characters</h2>'
                        // + propDlg.makeCharactersContent(fonts, current)
                        // + '</div>' // end of tab-page div for format
                        // + '<div class="tab-page"><h2 class="tab" data-i18n="EditTab.FormatDialog.MoreTab">More</h2>'
                        // + propDlg.makeDiv(null, null, null, null,
                        //     propDlg.makeDiv(null, 'mainBlock leftBlock', null, null,
                        //         propDlg.makeDiv(null, null, null, 'EditTab.Emphasis', 'Emphasis') + propDlg.makeDiv(null, null, null, null,
                        //             propDlg.makeDiv('bold', 'iconLetter', 'font-weight:bold', null, 'B')
                        //             + propDlg.makeDiv('italic', 'iconLetter', 'font-style: italic', null, 'I')
                        //             + propDlg.makeDiv('underline', 'iconLetter', 'text-decoration: underline', null, 'U')))
                        //     + propDlg.makeDiv(null, 'mainBlock', null, null,
                        //         propDlg.makeDiv(null, null, null, 'EditTab.Position', 'Position') + propDlg.makeDiv(null, null, null, null,
                        //             propDlg.makeDiv('position-leading', 'icon16x16', null, null, propDlg.makeImage('text_align_left.png'))
                        //             + propDlg.makeDiv('position-center', 'icon16x16', null, null, propDlg.makeImage('text_align_center.png')))))
                        // + propDlg.makeDiv(null, null, 'margin-top:10px', null,
                        //     propDlg.makeDiv(null, 'mainBlock leftBlock', null, null,
                        //         propDlg.makeDiv(null, null, null, 'EditTab.Borders', 'Borders')
                        //         + propDlg.makeDiv(null, null, 'margin-top:-11px', null,
                        //             propDlg.makeDiv('border-none', 'icon16x16', null, null, propDlg.makeImage('grayX.png'))
                        //             + propDlg.makeDiv('border-black', 'iconHtml', null, null, propDlg.makeDiv(null, 'iconBox', 'border-color: black', null, ''))
                        //             + propDlg.makeDiv('border-black-round', 'iconHtml', null, null, propDlg.makeDiv(null, 'iconBox bdRounded', 'border-color: black', null, '')))
                        //         + propDlg.makeDiv(null, null, 'margin-left:24px;margin-top:-13px', null,
                        //             propDlg.makeDiv('border-gray', 'iconHtml', null, null, propDlg.makeDiv(null, 'iconBox', 'border-color: gray', null, ''))
                        //             + propDlg.makeDiv('border-gray-round', 'iconHtml', null, null, propDlg.makeDiv(null, 'iconBox bdRounded', 'border-color: gray', null, ''))))
                        //     + propDlg.makeDiv(null, 'mainBlock', null, null,
                        //         propDlg.makeDiv(null, null, null, 'EditTab.Background', 'Background')
                        //         + propDlg.makeDiv(null, null, 'margin-top:-11px', null,
                        //             propDlg.makeDiv('background-none', 'icon16x16', null, null, propDlg.makeImage('grayX.png'))
                        //             + propDlg.makeDiv('background-gray', 'iconHtml', null, null, propDlg.makeDiv(null, 'iconBack', 'background-color: ' + propDlg.preferredGray(), null, '')))))
                        + '<div class="format-toolbar-description" id="formatMoreDesc"></div>'
                        + '</div>' // end of tab-page div for 'more' tab
                        + '</div>'; // end of tab-pane div
                } else {
                    // not in authorMode...much simpler dialog, no tabs, just the body of the characters tab.
                    html += '<div class="bloomDialogMainPage">'
                        //+ propDlg.makeCharactersContent(fonts, current)
                        + '</div>';
                }
                html += '</div>';
                $('#format-toolbar').remove(); // in case there's still one somewhere else
                $('body').append(html);

                //make some select boxes permit custom values
                $('.allowCustom').select2({
                    tags: true //this is weird, we're not really doing tags, but this is how you get to enable typing
                });
                $('select:not(.allowCustom)').select2({
                    tags: false,
                    minimumResultsForSearch: -1 // result is that no search box is shown
                });

                var toolbar = $('#format-toolbar');
                toolbar.find('*[data-i18n]').localize();
                toolbar.draggable({ distance: 10, scroll: false, containment: $('html') });
                toolbar.draggable("disable"); // until after we make sure it's in the Viewport
                toolbar.css('opacity', 1.0);
                if (!noFormatChange) {
                    // propDlg.getCharTabDescription();
                    // propDlg.getMoreTabDescription();

                    // $('#font-select').change(function () { propDlg.changeFont(); });
                    // propDlg.AddQtipToElement($('#font-select'), theOneLocalizationManager.getText('EditTab.FormatDialog.FontFaceToolTip', 'Change the font face'), 1500);
                    // $('#size-select').change(function () { propDlg.changeSize(); });
                    // propDlg.AddQtipToElement($('#size-select'), theOneLocalizationManager.getText('EditTab.FormatDialog.FontSizeToolTip', 'Change the font size'), 1500);
                    // $('#line-height-select').change(function () { propDlg.changeLineheight(); });
                    // propDlg.AddQtipToElement($('#line-height-select').parent(), theOneLocalizationManager.getText('EditTab.FormatDialog.LineSpacingToolTip', 'Change the spacing between lines of text'), 1500);
                    // $('#word-space-select').change(function () { propDlg.changeWordSpace(); });
                    // propDlg.AddQtipToElement($('#word-space-select').parent(), theOneLocalizationManager.getText('EditTab.FormatDialog.WordSpacingToolTip', 'Change the spacing between words'), 1500);
                    // if (propDlg.authorMode) {
                    //     if (!propDlg.xmatterMode) {
                    //         $('#styleSelect').change(function () { propDlg.selectStyle(); });
                    //         (<alphanumInterface>$('#style-select-input')).alphanum({ allowSpace: false, preventLeadingNumeric: true });
                    //         $('#style-select-input').on('input', function () { propDlg.styleInputChanged(); }); // not .change(), only fires on loss of focus
                    //         // Here I'm taking advantage of JS by pushing an extra field into an object whose declaration does not allow it,
                    //         // so typescript checking just has to be worked around. This enables a hack in jquery.alphanum.js.
                    //         (<any>$('#style-select-input').get(0)).trimNotification = function () { propDlg.styleStateChange('invalid-characters'); }
                    //         $('#show-createStyle').click(function (event) {
                    //             event.preventDefault();
                    //             propDlg.showCreateStyle();
                    //             return false;
                    //         });
                    //         $('#create-button').click(function () { propDlg.createStyle(); });
                    //     }
                    //     var buttonIds = propDlg.getButtonIds();
                    //     for (var idIndex = 0; idIndex < buttonIds.length; idIndex++) {
                    //         var button = $('#' + buttonIds[idIndex]);
                    //         button.click(function () { propDlg.buttonClick(this); });
                    //         button.addClass('propButton');
                    //     }
                    //     propDlg.selectButtons(current);
                    //     new WebFXTabPane($('#tabRoot').get(0), false, null);
                    // }
                }
                var offset = $('#formatButton').offset();
                toolbar.offset({ left: offset.left + 30, top: offset.top - 30 });
                //TextBoxProperties.positionInViewport(toolbar);
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
}