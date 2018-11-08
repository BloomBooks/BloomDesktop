/// <reference path="../../typings/select2/select2.d.ts" />
import "../../node_modules/select2/dist/js/select2.js";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { BloomApi } from "../../utils/bloomApi";
import { EditableDivUtils } from "../js/editableDivUtils";
import BloomHintBubbles from "../js/BloomHintBubbles";

declare function WebFxTabPane(
    element: HTMLElement,
    useCookie: boolean,
    callback: any
): any; // from tabpane, from a <script> tag

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
    public AttachToBox(targetBox: HTMLElement) {
        var propDlg = this;
        this._previousBox = targetBox;

        // Put the format button in the text box itself, so that it's always in the right place.
        $(targetBox).append(propDlg.getDialogActivationButton());

        // BL-2476: Readers made from BloomPacks should have formatting dialogs disabled
        var suppress = $(document).find('meta[name="lockFormatting"]');
        var noFormatChange =
            suppress.length > 0 &&
            suppress.attr("content").toLowerCase() === "true";

        // Why do we use .off and .on? See comment in a nearly identical location in StyleEditor.ts
        $(targetBox).off("click.formatButton");
        $(targetBox).on("click.formatButton", ".formatButton", () => {
            BloomApi.get(
                "bookEdit/TextBoxProperties/TextBoxProperties.html",
                result => {
                    var html = result.data;
                    propDlg.boxBeingEdited = targetBox;

                    // If this text box has had properties set already, get them so we can setup the dialog contents
                    // if not already set, this will return 'Auto'
                    var languageGroup = propDlg.getTextBoxLanguage(targetBox);

                    $("#text-properties-dialog").remove(); // in case there's still one somewhere else
                    $("body").append(html);

                    // Use select2 in place of regular selects. One reason to do this is that, for
                    // reasons I can't fathom, clicking in a regular select doesn't work within this
                    // dialog...a problem that seems to occur only in Bloom, not when we open the page
                    // in Firefox. This makes it very hard to debug.
                    // Another advantage is that the lists can scroll...we are starting to have a lot
                    // of UI languages.
                    // The commented out block is needed if we add ones that allow custom items.
                    // $('.allowCustom').select2({
                    //     tags: true //this is weird, we're not really doing tags, but this is how you get to enable typing
                    // });
                    $("select:not(.allowCustom)").select2({
                        tags: false,
                        minimumResultsForSearch: -1 // result is that no search box is shown
                    });

                    if (noFormatChange) {
                        $("#text-properties-dialog").addClass(
                            "formattingDisabled"
                        );
                    } else {
                        $(
                            ":radio[name=languageRadioGroup][value=" +
                                languageGroup +
                                "]"
                        ).prop("checked", true);
                    }

                    var dialogElement = $("#text-properties-dialog");
                    dialogElement.find("*[data-i18n]").localize();
                    dialogElement.draggable({
                        distance: 10,
                        scroll: false,
                        containment: $("html")
                    });
                    dialogElement.draggable("disable"); // until after we make sure it's in the Viewport
                    dialogElement.css("opacity", 1.0);
                    if (!noFormatChange) {
                        // Hook up change event handlers
                        $("#language-group").change(() => {
                            propDlg.changeLanguageGroup();
                        });
                        new WebFXTabPane($("#tabRoot").get(0), false);
                    }
                    // Give the browser time to get the dialog into the DOM first, before doing this stuff
                    // It just needs to delay one 'cycle'.
                    // http://stackoverflow.com/questions/779379/why-is-settimeoutfn-0-sometimes-useful
                    setTimeout(() => {
                        // Make sure we get the right button!
                        var orientOnButton = $(propDlg.boxBeingEdited).find(
                            ".formatButton"
                        );
                        EditableDivUtils.positionDialogAndSetDraggable(
                            dialogElement,
                            orientOnButton
                        );
                        dialogElement.draggable("enable");

                        $("html").off("click.dialogElement");
                        $("html").on("click.dialogElement", event => {
                            if (
                                event.target !== dialogElement.get(0) &&
                                dialogElement.has(event.target).length === 0 &&
                                $(event.target).parent() !== dialogElement &&
                                dialogElement.has(event.target).length === 0 &&
                                dialogElement.is(":visible")
                            ) {
                                dialogElement.remove();
                                event.stopPropagation();
                                event.preventDefault();
                            }
                        });
                        dialogElement.on("click.dialogElement", event => {
                            // this stops an event inside the dialog from propagating to the html element, which would close the dialog
                            event.stopPropagation();
                        });
                        this.removeButtonSelection();
                        this.initializeAlignment();
                        this.initializeBorderStyle();
                        this.initializeBackground();
                        this.setButtonClickActions();
                        this.makeLanguageSelect();
                        this.initializeHintTab();
                    }, 0); // just push this to the end of the event queue
                }
            );
        });
    }

    private getButtonIds() {
        return [
            "align-top",
            "align-center",
            "align-bottom",
            "borderstyle-none",
            "borderstyle-black",
            "borderstyle-black-round",
            "borderstyle-gray",
            "borderstyle-gray-round",
            "background-none",
            "background-light-gray",
            "background-gray",
            "background-black",
            "bordertop",
            "borderleft",
            "borderright",
            "borderbottom"
        ];
    }

    private removeButtonSelection() {
        var buttonIds = this.getButtonIds();
        for (var i = 0; i < buttonIds.length; i++) {
            $("#" + buttonIds[i]).removeClass("selectedIcon");
        }
    }

    private setButtonClickActions() {
        var buttonIds = this.getButtonIds();
        for (var idIndex = 0; idIndex < buttonIds.length; idIndex++) {
            var button = $("#" + buttonIds[idIndex]);
            button.click(e => this.buttonClick(e.currentTarget));
        }
    }

    // set uiChangeOnly to true to change only the appearance of buttons
    private buttonClick(buttonDiv, uiChangeOnly = false) {
        var button = $(buttonDiv);
        var id = button.attr("id");
        var index = id.indexOf("-");
        if (index >= 0) {
            // buttons in a group are given ids starting with group-
            button.addClass("selectedIcon");
            var group = id.substring(0, index);
            $(".propButton").each((index, b) => {
                var item = $(b);
                if (
                    b !== button.get(0) &&
                    item.attr("id").startsWith(group + "-")
                ) {
                    item.removeClass("selectedIcon");
                }
            });
        } else {
            // button is not part of a group, so must toggle
            if (button.hasClass("selectedIcon")) {
                button.removeClass("selectedIcon");
            } else {
                button.addClass("selectedIcon");
            }
        }
        if (uiChangeOnly) return;

        // Now make it so
        if (id.startsWith("align")) {
            this.changeAlignment();
        } else if (id.startsWith("borderstyle-")) {
            this.changeBorder(
                $(this.getAffectedTranslationGroup(this.boxBeingEdited)),
                true
            );
        } else if (id.startsWith("border")) {
            this.changeBorder(
                $(this.getAffectedTranslationGroup(this.boxBeingEdited)),
                false
            );
        } else if (id.startsWith("background")) {
            this.changeBackground(
                $(this.getAffectedTranslationGroup(this.boxBeingEdited))
            );
        }
    }

    private changeLanguageGroup() {
        // get radio button value and set 'data-default-languages' attribute
        var radioValue = $('input[name="languageRadioGroup"]:checked').val();
        var targetGroup = $(
            this.getAffectedTranslationGroup(this.boxBeingEdited)
        );
        // currently 'radioValue' should be one of: 'Auto', 'N1', 'N2', or 'V'
        if (targetGroup) {
            targetGroup.attr("data-default-languages", radioValue);
        }
        // If not Auto, remove style to show hint on all
        if (radioValue != "Auto") {
            targetGroup.removeClass(this.classNameForHintOnEach());
        }
        // Update visibility of hint bubbles controls based on whether Auto
        this.updateHintTabControls();
    }

    private initializeAlignment() {
        var targetGroup = $(
            this.getAffectedTranslationGroup(this.boxBeingEdited)
        );
        if (targetGroup) {
            if (targetGroup.hasClass("bloom-vertical-align-center")) {
                $("#align-center").addClass("selectedIcon");
            } else if (targetGroup.hasClass("bloom-vertical-align-bottom")) {
                $("#align-bottom").addClass("selectedIcon");
            } else {
                $("#align-top").addClass("selectedIcon");
            }
        }
    }

    private changeAlignment() {
        var targetGroup = $(
            this.getAffectedTranslationGroup(this.boxBeingEdited)
        );
        targetGroup.removeClass("bloom-vertical-align-center");
        targetGroup.removeClass("bloom-vertical-align-bottom");
        if (targetGroup) {
            if ($("#align-center").hasClass("selectedIcon")) {
                targetGroup.addClass("bloom-vertical-align-center");
            } else if ($("#align-bottom").hasClass("selectedIcon")) {
                targetGroup.addClass("bloom-vertical-align-bottom");
            } // else leave it missing.
        }
    }

    private initializeBorderStyle() {
        var targetGroup = $(
            this.getAffectedTranslationGroup(this.boxBeingEdited)
        );
        if (targetGroup) {
            if (targetGroup.hasClass("bloom-borderstyle-black")) {
                $("#borderstyle-black").addClass("selectedIcon");
            } else if (targetGroup.hasClass("bloom-borderstyle-black-round")) {
                $("#borderstyle-black-round").addClass("selectedIcon");
            } else if (targetGroup.hasClass("bloom-borderstyle-gray")) {
                $("#borderstyle-gray").addClass("selectedIcon");
            } else if (targetGroup.hasClass("bloom-borderstyle-gray-round")) {
                $("#borderstyle-gray-round").addClass("selectedIcon");
            } else {
                $("#borderstyle-none").addClass("selectedIcon");
            }

            if (!targetGroup.hasClass("bloom-top-border-off")) {
                $("#bordertop").addClass("selectedIcon");
            }
            if (!targetGroup.hasClass("bloom-right-border-off")) {
                $("#borderright").addClass("selectedIcon");
            }
            if (!targetGroup.hasClass("bloom-bottom-border-off")) {
                $("#borderbottom").addClass("selectedIcon");
            }
            if (!targetGroup.hasClass("bloom-left-border-off")) {
                $("#borderleft").addClass("selectedIcon");
            }
        }
    }

    // styleChanged is true if the change was to the style of border, false if it involved one of the border side controls
    private changeBorder(targetGroup, styleChanged: boolean) {
        if (!targetGroup) {
            return;
        }
        targetGroup.removeClass("bloom-borderstyle-black");
        targetGroup.removeClass("bloom-borderstyle-black-round");
        targetGroup.removeClass("bloom-borderstyle-gray");
        targetGroup.removeClass("bloom-borderstyle-gray-round");
        targetGroup.removeClass("bloom-top-border-off");
        targetGroup.removeClass("bloom-right-border-off");
        targetGroup.removeClass("bloom-bottom-border-off");
        targetGroup.removeClass("bloom-left-border-off");

        if (this.borderStyleIsNotNone() && !this.anyBorderSideSelected()) {
            if (styleChanged) {
                // The user selected a border style and no sides are selected; select all sides.
                this.selectAllBorderSideButtons();
            } else {
                // The user deselected the last border side; select no border.
                this.buttonClick($("#borderstyle-none"), true);
            }
        }

        if ($("#borderstyle-black").hasClass("selectedIcon")) {
            targetGroup.addClass("bloom-borderstyle-black");
        } else if ($("#borderstyle-black-round").hasClass("selectedIcon")) {
            targetGroup.addClass("bloom-borderstyle-black-round");
        } else if ($("#borderstyle-gray").hasClass("selectedIcon")) {
            targetGroup.addClass("bloom-borderstyle-gray");
        } else if ($("#borderstyle-gray-round").hasClass("selectedIcon")) {
            targetGroup.addClass("bloom-borderstyle-gray-round");
        } else if (styleChanged) {
            // The user selected no border; deselect all border sides.
            this.deselectAllBorderSideButtons();
        }

        if (!$("#bordertop").hasClass("selectedIcon")) {
            targetGroup.addClass("bloom-top-border-off");
        }
        if (!$("#borderright").hasClass("selectedIcon")) {
            targetGroup.addClass("bloom-right-border-off");
        }
        if (!$("#borderbottom").hasClass("selectedIcon")) {
            targetGroup.addClass("bloom-bottom-border-off");
        }
        if (!$("#borderleft").hasClass("selectedIcon")) {
            targetGroup.addClass("bloom-left-border-off");
        }
    }

    private borderStyleIsNotNone(): boolean {
        return (
            $("#borderstyle-black").hasClass("selectedIcon") ||
            $("#borderstyle-black-round").hasClass("selectedIcon") ||
            $("#borderstyle-gray").hasClass("selectedIcon") ||
            $("#borderstyle-gray-round").hasClass("selectedIcon")
        );
    }

    private anyBorderSideSelected(): boolean {
        return (
            $("#bordertop").hasClass("selectedIcon") ||
            $("#borderbottom").hasClass("selectedIcon") ||
            $("#borderleft").hasClass("selectedIcon") ||
            $("#borderright").hasClass("selectedIcon")
        );
    }

    private selectAllBorderSideButtons() {
        $("#bordertop").addClass("selectedIcon");
        $("#borderbottom").addClass("selectedIcon");
        $("#borderleft").addClass("selectedIcon");
        $("#borderright").addClass("selectedIcon");
    }

    private deselectAllBorderSideButtons() {
        $("#bordertop").removeClass("selectedIcon");
        $("#borderbottom").removeClass("selectedIcon");
        $("#borderleft").removeClass("selectedIcon");
        $("#borderright").removeClass("selectedIcon");
    }

    private initializeBackground() {
        var targetGroup = $(
            this.getAffectedTranslationGroup(this.boxBeingEdited)
        );
        if (targetGroup) {
            if (targetGroup.hasClass("bloom-background-gray")) {
                $("#background-gray").addClass("selectedIcon");
            } else {
                $("#background-none").addClass("selectedIcon");
            }
        }
    }

    private changeBackground(targetGroup) {
        if (targetGroup) {
            targetGroup.removeClass("bloom-background-none");
            targetGroup.removeClass("bloom-background-gray");

            if ($("#background-gray").hasClass("selectedIcon")) {
                targetGroup.addClass("bloom-background-gray");
            } else {
                targetGroup.addClass("bloom-background-none");
            }
        }
    }

    private getTextBoxLanguage(targetBox: HTMLElement): string {
        var targetGroup = $(this.getAffectedTranslationGroup(targetBox));
        if (!targetGroup || !targetGroup.hasAttr("data-default-languages")) {
            return "Auto";
        }
        var result = targetGroup.attr("data-default-languages");
        const acceptable = ["Auto", "N1", "N2", "V"];
        return acceptable.indexOf(result) > -1 ? result : "Auto";
    }

    private getAffectedTranslationGroup(targetBox: HTMLElement): HTMLElement {
        var container = $(targetBox).parent();
        // I'm not sure (gjm) how often another translationGroup with box-header-off shows up,
        // but I found at least one instance, so make sure that's not the one we grab.
        return container.find(
            ".bloom-translationGroup:not(.box-header-off)"
        )[0];
    }

    // The z-index puts the formatButton above the origami-ui stuff so a click will find it.
    private getDialogActivationButton(): string {
        return (
            '<div contenteditable="false" class="bloom-ui formatButton">' +
            '<img  contenteditable="false" src="' +
            this._supportFilesRoot +
            '/img/cogGrey.svg"></div>'
        );
    }

    private makeLanguageSelect() {
        // items comes back as something like languages: [{label: 'English', tag: 'en'},{label: 'French', tag: 'fr'} ]
        BloomApi.get("uiLanguages", result => {
            var items: Array<any> = (<any>result.data).languages;
            this.makeSelectItems(items, "en", "lang-select");
        });
        $("#lang-select").change(e => {
            this.setHintTextForLang($("#lang-select").val());
        });
    }

    // Assumes a <select> with the specified id already exists. Makes the child elements and selects the current one.
    // This version assumes items is an array of objects with label and tag (see example in makeLanguageSelect).
    private makeSelectItems(items: any[], current, id, maxlength?) {
        var result = "";
        // May need this someday to handle missing items.
        // if (current && items.indexOf(current.toString()) === -1) {
        //     //we have a custom point size, so make that an option in addition to the standard ones
        //     items.push(current.toString());
        // }

        items.sort((a, b) => {
            return a.label.toLowerCase().localeCompare(b.label.toLowerCase());
        });

        for (var i = 0; i < items.length; i++) {
            var selected: string = "";
            if (current === items[i].tag) {
                selected = " selected";
            }
            var text = items[i].label;
            text = text.replace(/-/g, " "); //show users a space instead of dashes
            if (maxlength && text.length > maxlength) {
                text = text.substring(0, maxlength) + "...";
            }
            result +=
                '<option value="' +
                items[i].tag +
                '"' +
                selected +
                ">" +
                text +
                "</option>";
        }
        var parent = $("#" + id);
        parent.html(result);
    }

    private preventDragStealingClicksOnHintText() {
        // By default, ui-draggable makes any click in the whole dialog an attempt to drag
        // the dialog around. We need to suppress this so the user can click in the hint
        // box and type.
        $("#hint-content").click((e: Event) => e.stopPropagation());
        $("#hint-content").mousedown((e: Event) => e.stopPropagation());
        $("#hint-content").mouseup((e: Event) => e.stopPropagation());
        $("#hint-content").mousemove((e: Event) => e.stopPropagation());
    }

    private classNameForHintOnEach(): string {
        return "bloom-showHintOnEach";
    }

    private initializeHintTab() {
        if ($(this.boxBeingEdited).closest(".bloom-templateMode").length == 0) {
            // not in template mode: hide the hint tab
            $("#hint-header").hide();
            return;
        }
        this.initializeHintText();
        this.updateHintTabControls();
        $("#hint-scope").change(e => {
            this.changeShowHintOnEach();
        });
    }

    private updateHintTabControls() {
        var groupCanHaveMoreThanOneLanguage =
            $('input[name="languageRadioGroup"]:checked').val() == "Auto";
        var showHintOnEachGroupDiv = $("#show-hint-on-each-group");
        var includeLangLabel = $("#include-lang");
        if (groupCanHaveMoreThanOneLanguage) {
            showHintOnEachGroupDiv.show();
            var showHintOnEach = $(
                this.getAffectedTranslationGroup(this.boxBeingEdited)
            ).hasClass(this.classNameForHintOnEach());
            if (showHintOnEach) {
                $("#hint-scope")
                    .val("show-on-each")
                    .trigger("change");
                includeLangLabel.show();
            } else {
                // first item will be selected by default
                includeLangLabel.hide();
            }
        } else {
            showHintOnEachGroupDiv.hide();
            includeLangLabel.hide();
        }
    }

    private showHintOnEachIsSelected() {
        var selectedItem = $("#hint-scope option:selected");
        return selectedItem.attr("id") === "show-on-each";
    }

    private changeShowHintOnEach() {
        var targetGroup = $(
            this.getAffectedTranslationGroup(this.boxBeingEdited)
        );
        var showOnEach = this.showHintOnEachIsSelected();
        var includeLangLabel = $("#include-lang");
        if (showOnEach) {
            targetGroup.addClass(this.classNameForHintOnEach());
            includeLangLabel.show();
        } else {
            targetGroup.removeClass(this.classNameForHintOnEach());
            includeLangLabel.hide();
        }
        BloomHintBubbles.updateQtipPlacement(
            targetGroup,
            $("#hint-content").text()
        );
    }

    private initializeHintText() {
        this.preventDragStealingClicksOnHintText();
        // it's tempting to go for the current UI language as default, but we REALLY
        // want them to include at least an English hint, as that seems to be the
        // common interchange language among Bloom users.
        this.setHintTextForLang("en");
        $("#hint-content").on("input", e => {
            let lang = $("#lang-select").val();
            let text = $("#hint-content").text();
            let targetGroup = $(
                this.getAffectedTranslationGroup(this.boxBeingEdited)
            );
            var langLabel = targetGroup.find("label[lang=" + lang + "]");
            if (!text) {
                langLabel.remove(); // don't let empty ones hang around
            } else {
                if (langLabel.length === 0) {
                    targetGroup.prepend(
                        '<label class="bubble" lang="' + lang + '"></label>'
                    );
                    langLabel = targetGroup.find("label[lang=" + lang + "]");
                }
                langLabel.text(text);
            }
            BloomHintBubbles.updateQtipPlacement(targetGroup, text);
        });
    }

    private setHintTextForLang(lang: string) {
        var targetGroup = $(
            this.getAffectedTranslationGroup(this.boxBeingEdited)
        );
        var langLabel = targetGroup.find("label[lang=" + lang + "]");
        if (langLabel.length > 0) {
            $("#hint-content").text(langLabel.text());
        } else {
            $("#hint-content").text("");
        }
    }
}
