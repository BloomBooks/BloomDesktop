/// <reference path="./TextBoxProperties.ts" />

import TextBoxProperties from './TextBoxProperties';
'use strict';

describe('TextBoxProperties', function () {
    var dialog;
    // most perplexingly, jasmine doesn't reset the dom between tests
    beforeEach(function () {
        $('body').html('');
        $('body').append('<div id="testTarget" class="bloom-translationGroup"></div>');
        dialog = new TextBoxProperties('');
    });

    it('changeBackground, to none, css set properly', function () {
        $('body').append('<div id="background-none" class="selectedIcon"></div>');
        dialog.AttachToBox($('#testTarget')[0]);
        dialog.changeBackground($('#testTarget'));

        expect($('#testTarget').hasClass('bloom-background-none')).toBeTruthy();
    });

    it('changeBackground, to gray, css set properly', function () {
        $('body').append('<div id="background-gray" class="selectedIcon"></div>');
        dialog.AttachToBox($('#testTarget')[0]);
        dialog.changeBackground($('#testTarget'));

        expect($('#testTarget').hasClass('bloom-background-gray')).toBeTruthy();
    });

    it('changeBorder, nothing to something, no sides selected, css classes set properly and border side buttons selected', function () {
        $('body').append('<div id="border-black" class="selectedIcon"></div>');
        $('body').append('<div id="bordertop"></div>');
        $('body').append('<div id="borderright"></div>');
        $('body').append('<div id="borderbottom"></div>');
        $('body').append('<div id="borderleft"></div>');
        dialog.changeBorder($('#testTarget'), true);

        expect($('#testTarget').hasClass('bloom-border-black')).toBeTruthy();
        expect($('#testTarget').hasClass('bloom-top-border-off')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-right-border-off')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-bottom-border-off')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-left-border-off')).toBeFalsy();

        expect($('#bordertop').hasClass('selectedIcon')).toBeTruthy();
        expect($('#borderright').hasClass('selectedIcon')).toBeTruthy();
        expect($('#borderbottom').hasClass('selectedIcon')).toBeTruthy();
        expect($('#borderleft').hasClass('selectedIcon')).toBeTruthy();

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

    it('changeBorder, something to nothing, css classes set properly and border side buttons deselected', function () {
        $('body').append('<div id="border-none" class="selectedIcon"></div>');
        $('body').append('<div id="bordertop" class="selectedIcon"></div>');
        $('body').append('<div id="borderright" class="selectedIcon"></div>');
        $('body').append('<div id="borderbottom" class="selectedIcon"></div>');
        $('body').append('<div id="borderleft" class="selectedIcon"></div>');
        dialog.changeBorder($('#testTarget'), true);

        expect($('#testTarget').hasClass('bloom-border-black')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-border-black-round')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-border-gray')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-border-gray-round')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-top-border-off')).toBeTruthy();
        expect($('#testTarget').hasClass('bloom-right-border-off')).toBeTruthy();
        expect($('#testTarget').hasClass('bloom-bottom-border-off')).toBeTruthy();
        expect($('#testTarget').hasClass('bloom-left-border-off')).toBeTruthy();

        expect($('#bordertop').hasClass('selectedIcon')).toBeFalsy();
        expect($('#borderright').hasClass('selectedIcon')).toBeFalsy();
        expect($('#borderbottom').hasClass('selectedIcon')).toBeFalsy();
        expect($('#borderleft').hasClass('selectedIcon')).toBeFalsy();
    });

    it('changeBorder, to gray rounded, css classes set properly and border side buttons unchanged', function () {
        $('body').append('<div id="border-none"></div>');
        $('body').append('<div id="border-gray-round" class="selectedIcon"></div>');
        $('body').append('<div id="bordertop"></div>');
        $('body').append('<div id="borderright" class="selectedIcon"></div>');
        $('body').append('<div id="borderbottom"></div>');
        $('body').append('<div id="borderleft" class="selectedIcon"></div>');
        dialog.changeBorder($('#testTarget'), true);

        expect($('#testTarget').hasClass('bloom-border-black')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-border-black-round')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-border-gray')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-border-gray-round')).toBeTruthy();

        expect($('#testTarget').hasClass('bloom-top-border-off')).toBeTruthy();
        expect($('#testTarget').hasClass('bloom-right-border-off')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-bottom-border-off')).toBeTruthy();
        expect($('#testTarget').hasClass('bloom-left-border-off')).toBeFalsy();

        expect($('#bordertop').hasClass('selectedIcon')).toBeFalsy();
        expect($('#borderright').hasClass('selectedIcon')).toBeTruthy();
        expect($('#borderbottom').hasClass('selectedIcon')).toBeFalsy();
        expect($('#borderleft').hasClass('selectedIcon')).toBeTruthy();
    });

    it('changeBorder, deselect side, css classes set properly', function () {
        $('body').append('<div id="border-none"></div>');
        $('body').append('<div id="border-black-round" class="selectedIcon"></div>');
        $('body').append('<div id="bordertop" class="selectedIcon"></div>');
        $('body').append('<div id="borderright" class="selectedIcon"></div>');
        $('body').append('<div id="borderbottom"></div>');
        $('body').append('<div id="borderleft" class="selectedIcon"></div>');
        dialog.changeBorder($('#testTarget'), false);

        expect($('#testTarget').hasClass('bloom-border-black')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-border-black-round')).toBeTruthy();
        expect($('#testTarget').hasClass('bloom-border-gray')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-border-gray-round')).toBeFalsy();

        expect($('#testTarget').hasClass('bloom-top-border-off')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-right-border-off')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-bottom-border-off')).toBeTruthy();
        expect($('#testTarget').hasClass('bloom-left-border-off')).toBeFalsy();

        expect($('#bordertop').hasClass('selectedIcon')).toBeTruthy();
        expect($('#borderright').hasClass('selectedIcon')).toBeTruthy();
        expect($('#borderbottom').hasClass('selectedIcon')).toBeFalsy();
        expect($('#borderleft').hasClass('selectedIcon')).toBeTruthy();
    });

    it('changeBorder, deselect last side, css classes set properly and border set to none', function () {
        $('body').append('<div id="border-none"></div>');
        $('body').append('<div id="border-black-round" class="selectedIcon"></div>');
        $('body').append('<div id="bordertop"></div>');
        $('body').append('<div id="borderright"></div>');
        $('body').append('<div id="borderbottom"></div>');
        $('body').append('<div id="borderleft"></div>');
        dialog.changeBorder($('#testTarget'), false);

        expect($('#testTarget').hasClass('bloom-border-black')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-border-black-round')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-border-gray')).toBeFalsy();
        expect($('#testTarget').hasClass('bloom-border-gray-round')).toBeFalsy();

        expect($('#testTarget').hasClass('bloom-top-border-off')).toBeTruthy();
        expect($('#testTarget').hasClass('bloom-right-border-off')).toBeTruthy();
        expect($('#testTarget').hasClass('bloom-bottom-border-off')).toBeTruthy();
        expect($('#testTarget').hasClass('bloom-left-border-off')).toBeTruthy();

        expect($('#border-black-round').hasClass('selectedIcon')).toBeFalsy();
        expect($('#border-none').hasClass('selectedIcon')).toBeTruthy();
        expect($('#bordertop').hasClass('selectedIcon')).toBeFalsy();
        expect($('#borderright').hasClass('selectedIcon')).toBeFalsy();
        expect($('#borderbottom').hasClass('selectedIcon')).toBeFalsy();
        expect($('#borderleft').hasClass('selectedIcon')).toBeFalsy();
    });
});
