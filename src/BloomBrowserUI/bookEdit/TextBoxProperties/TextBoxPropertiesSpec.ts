/// <reference path="./TextBoxProperties.ts" />

import TextBoxProperties from "./TextBoxProperties";
"use strict";

describe("TextBoxProperties", () => {
    var dialog;
    // most perplexingly, jasmine doesn't reset the dom between tests
    beforeEach(() => {
        $("body").html("");
        $("body").append(
            '<div id="testTarget" class="bloom-translationGroup"></div>'
        );
        dialog = new TextBoxProperties("");
    });

    it("changeBackground, to none, css set properly", () => {
        // this div represents the button used to select the 'none' background option
        $("body").append(
            '<div id="background-none" class="selectedIcon"></div>'
        );
        dialog.changeBackground($("#testTarget"));

        expect($("#testTarget").hasClass("bloom-background-none")).toBeTruthy();
    });

    it("changeBackground, to gray, css set properly", () => {
        // this div represents the button used to select the 'gray' background option
        $("body").append(
            '<div id="background-gray" class="selectedIcon"></div>'
        );
        dialog.changeBackground($("#testTarget"));

        expect($("#testTarget").hasClass("bloom-background-gray")).toBeTruthy();
    });

    it("changeBorder, nothing to something, no sides selected, css classes set properly and border side buttons selected", () => {
        $("#testTarget").addClass("bloom-borderstyle-black");
        $("#testTarget").addClass("bloom-top-border-off");
        $("#testTarget").addClass("bloom-right-border-off");
        $("#testTarget").addClass("bloom-bottom-border-off");
        $("#testTarget").addClass("bloom-left-border-off");

        // this div represents the button used to select the 'black' border style
        $("body").append(
            '<div id="borderstyle-black" class="selectedIcon"></div>'
        );
        // these four divs represent the buttons used to select border sides
        $("body").append('<div id="bordertop"></div>');
        $("body").append('<div id="borderright"></div>');
        $("body").append('<div id="borderbottom"></div>');
        $("body").append('<div id="borderleft"></div>');

        // SUT
        dialog.changeBorder($("#testTarget"), true);

        expect(
            $("#testTarget").hasClass("bloom-borderstyle-black")
        ).toBeTruthy();
        expect($("#testTarget").hasClass("bloom-top-border-off")).toBeFalsy();
        expect($("#testTarget").hasClass("bloom-right-border-off")).toBeFalsy();
        expect(
            $("#testTarget").hasClass("bloom-bottom-border-off")
        ).toBeFalsy();
        expect($("#testTarget").hasClass("bloom-left-border-off")).toBeFalsy();

        expect($("#bordertop").hasClass("selectedIcon")).toBeTruthy();
        expect($("#borderright").hasClass("selectedIcon")).toBeTruthy();
        expect($("#borderbottom").hasClass("selectedIcon")).toBeTruthy();
        expect($("#borderleft").hasClass("selectedIcon")).toBeTruthy();

        // We could, in theory test the actual css as below.
        // However, we would first have to shoehorn the tests to use the css files,
        // and notice that we cannot use the shorthand attributes.
        // expect($('#testTarget').css('border-top-width')).toBe('1px');
        // expect($('#testTarget').css('border-right-width')).toBe('1px');
        // expect($('#testTarget').css('border-bottom-width')).toBe('1px');
        // expect($('#testTarget').css('border-left-width')).toBe('1px');
        // expect($('#testTarget').css('border-top-style')).toBe('solid');
        // expect($('#testTarget').css('border-right-style')).toBe('solid');
        // expect($('#testTarget').css('border-bottom-style')).toBe('solid');
        // expect($('#testTarget').css('border-left-style')).toBe('solid');
        // // black, but rgb(0, 0, 0) is how Firefox reports it
        // expect($('#testTarget').css('border-top-color')).toBe('rgb(0, 0, 0)');
        // expect($('#testTarget').css('border-right-color')).toBe('rgb(0, 0, 0)');
        // expect($('#testTarget').css('border-bottom-color')).toBe('rgb(0, 0, 0)');
        // expect($('#testTarget').css('border-left-color')).toBe('rgb(0, 0, 0)');

        // expect($('#testTarget').css('border-top-left-radius')).toBe('0px');
        // expect($('#testTarget').css('border-top-right-radius')).toBe('0px');
        // expect($('#testTarget').css('border-bottom-left-radius')).toBe('0px');
        // expect($('#testTarget').css('border-bottom-right-radius')).toBe('0px');

        // expect($('#testTarget').css('box-sizing')).toBe('border-box');
    });

    it("changeBorder, something to nothing, css classes set properly and border side buttons deselected", () => {
        $("#testTarget").addClass("bloom-borderstyle-black");

        // this div represents the buttons used to select border styles
        $("body").append(
            '<div id="borderstyle-none" class="selectedIcon"></div>'
        );
        // these four divs represent the buttons used to select border sides
        $("body").append('<div id="bordertop" class="selectedIcon"></div>');
        $("body").append('<div id="borderright" class="selectedIcon"></div>');
        $("body").append('<div id="borderbottom" class="selectedIcon"></div>');
        $("body").append('<div id="borderleft" class="selectedIcon"></div>');

        // SUT
        dialog.changeBorder($("#testTarget"), true);

        expect(
            $("#testTarget").hasClass("bloom-borderstyle-black")
        ).toBeFalsy();
        expect($("#testTarget").hasClass("bloom-top-border-off")).toBeTruthy();
        expect(
            $("#testTarget").hasClass("bloom-right-border-off")
        ).toBeTruthy();
        expect(
            $("#testTarget").hasClass("bloom-bottom-border-off")
        ).toBeTruthy();
        expect($("#testTarget").hasClass("bloom-left-border-off")).toBeTruthy();

        expect($("#bordertop").hasClass("selectedIcon")).toBeFalsy();
        expect($("#borderright").hasClass("selectedIcon")).toBeFalsy();
        expect($("#borderbottom").hasClass("selectedIcon")).toBeFalsy();
        expect($("#borderleft").hasClass("selectedIcon")).toBeFalsy();
    });

    it("changeBorder, change border style with only some sides, css classes set properly and border side buttons unchanged", () => {
        $("#testTarget").addClass("bloom-borderstyle-black");
        $("#testTarget").addClass("bloom-right-border-off");
        $("#testTarget").addClass("bloom-left-border-off");

        // these divs represent the buttons used to select border styles
        $("body").append('<div id="borderstyle-none"></div>');
        $("body").append(
            '<div id="borderstyle-gray-round" class="selectedIcon"></div>'
        );
        // these four divs represent the buttons used to select border sides
        $("body").append('<div id="bordertop"></div>');
        $("body").append('<div id="borderright" class="selectedIcon"></div>');
        $("body").append('<div id="borderbottom"></div>');
        $("body").append('<div id="borderleft" class="selectedIcon"></div>');

        // SUT
        dialog.changeBorder($("#testTarget"), true);

        expect(
            $("#testTarget").hasClass("bloom-borderstyle-black")
        ).toBeFalsy();
        expect(
            $("#testTarget").hasClass("bloom-borderstyle-gray-round")
        ).toBeTruthy();

        expect($("#testTarget").hasClass("bloom-top-border-off")).toBeTruthy();
        expect($("#testTarget").hasClass("bloom-right-border-off")).toBeFalsy();
        expect(
            $("#testTarget").hasClass("bloom-bottom-border-off")
        ).toBeTruthy();
        expect($("#testTarget").hasClass("bloom-left-border-off")).toBeFalsy();

        expect($("#bordertop").hasClass("selectedIcon")).toBeFalsy();
        expect($("#borderright").hasClass("selectedIcon")).toBeTruthy();
        expect($("#borderbottom").hasClass("selectedIcon")).toBeFalsy();
        expect($("#borderleft").hasClass("selectedIcon")).toBeTruthy();
    });

    it("changeBorder, deselect bottom side, border style unchanged and bottom border turned off", () => {
        $("#testTarget").addClass("bloom-borderstyle-black-round");

        // these divs represent the buttons used to select border styles
        $("body").append('<div id="borderstyle-none"></div>');
        $("body").append(
            '<div id="borderstyle-black-round" class="selectedIcon"></div>'
        );
        // these four divs represent the buttons used to select border sides
        $("body").append('<div id="bordertop" class="selectedIcon"></div>');
        $("body").append('<div id="borderright" class="selectedIcon"></div>');
        // here we mock that the user clicked the borderbottom button, thereby removing the selectedIcon class
        $("body").append('<div id="borderbottom"></div>');
        $("body").append('<div id="borderleft" class="selectedIcon"></div>');

        // SUT
        dialog.changeBorder($("#testTarget"), false);

        // these all remain the same as expected
        expect(
            $("#testTarget").hasClass("bloom-borderstyle-black-round")
        ).toBeTruthy();
        expect($("#testTarget").hasClass("bloom-top-border-off")).toBeFalsy();
        expect($("#testTarget").hasClass("bloom-right-border-off")).toBeFalsy();
        expect($("#testTarget").hasClass("bloom-left-border-off")).toBeFalsy();
        expect($("#bordertop").hasClass("selectedIcon")).toBeTruthy();
        expect($("#borderright").hasClass("selectedIcon")).toBeTruthy();
        expect($("#borderbottom").hasClass("selectedIcon")).toBeFalsy();
        expect($("#borderleft").hasClass("selectedIcon")).toBeTruthy();

        // this is added by changeBorder
        expect(
            $("#testTarget").hasClass("bloom-bottom-border-off")
        ).toBeTruthy();
    });

    it("changeBorder, deselect last side, css classes set properly and border set to none", () => {
        $("#testTarget").addClass("bloom-borderstyle-black-round");
        $("#testTarget").addClass("bloom-top-border-off");
        $("#testTarget").addClass("bloom-right-border-off");
        $("#testTarget").addClass("bloom-left-border-off");

        // this group is used by the code to know which buttons must only have one in their group selected
        $("body").append('<div id="borderstyle-group">');
        // these divs represent the buttons used to select border styles
        $("#borderstyle-group").append(
            '<div id="borderstyle-none" class="propButton"></div>'
        );
        $("#borderstyle-group").append(
            '<div id="borderstyle-black-round" class="selectedIcon propButton"></div>'
        );
        // these four divs represent the buttons used to select border sides
        $("body").append('<div id="bordertop" class="propButton"></div>');
        $("body").append('<div id="borderright" class="propButton"></div>');
        $("body").append('<div id="borderbottom" class="propButton"></div>');
        $("body").append('<div id="borderleft" class="propButton"></div>');

        // SUT
        dialog.changeBorder($("#testTarget"), false);

        expect(
            $("#testTarget").hasClass("bloom-borderstyle-black-round")
        ).toBeFalsy();

        expect($("#testTarget").hasClass("bloom-top-border-off")).toBeTruthy();
        expect(
            $("#testTarget").hasClass("bloom-right-border-off")
        ).toBeTruthy();
        expect(
            $("#testTarget").hasClass("bloom-bottom-border-off")
        ).toBeTruthy();
        expect($("#testTarget").hasClass("bloom-left-border-off")).toBeTruthy();

        expect(
            $("#borderstyle-black-round").hasClass("selectedIcon")
        ).toBeFalsy();
        expect($("#borderstyle-none").hasClass("selectedIcon")).toBeTruthy();
        expect($("#bordertop").hasClass("selectedIcon")).toBeFalsy();
        expect($("#borderright").hasClass("selectedIcon")).toBeFalsy();
        expect($("#borderbottom").hasClass("selectedIcon")).toBeFalsy();
        expect($("#borderleft").hasClass("selectedIcon")).toBeFalsy();
    });
});
