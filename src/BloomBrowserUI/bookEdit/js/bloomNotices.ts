/// <reference path="../../typings/jquery/jquery.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
/// <reference path="bloomQtipUtils.ts" />
/// <reference path="../../typings/jquery.qtip.d.ts" />
/// <reference path="../../typings/jquery.qtipSecondary.d.ts" />

import theOneLocalizationManager from '../../lib/localizationManager/localizationManager';
//import '../../lib/jquery.qtip.js'
//import '../../lib/jquery.qtipSecondary.js'
import bloomQtipUtils from './bloomQtipUtils';

export default class BloomNotices {
    public static addExperimentalNotice(container: HTMLElement): void {
        var experimental = theOneLocalizationManager.getText('EditTab.ExperimentalNotice',
            'This page is an experimental prototype which may have many problems, for which we apologize.');
        $(container).find(".pictureDictionaryPage").each(function () {
            ($(this)).qtipSecondary({
                content: "<div id='experimentNotice'><img src='/bloom/images/experiment.png'/>" + experimental + "<div/>"
                , show: { ready: true }
                , hide: false
                , position: {
                    at: 'right top',
                    my: 'left top'
                },
                style: {
                    classes: 'ui-tooltip-red',
                    tip: { corner: false }
                }
                , container: bloomQtipUtils.qtipZoomContainer()
            });
        });
    }

    public static addEditingNotAllowedMessages(container: HTMLElement): void {
        var notAllowed = theOneLocalizationManager.getText('EditTab.EditNotAllowed',
            'You cannot change these because this is not the original copy.');
        var readOnly = theOneLocalizationManager.getText('EditTab.ReadOnlyInAuthorMode',
            'You cannot put anything in there while making an original book.');
        $(container).find('*[data-hint]').each(function () {
            if ($(this).css('cursor') == 'not-allowed') {
                var whyDisabled = notAllowed;
                if ($(this).hasClass('bloom-ReadOnlyInAuthorMode')) {
                    whyDisabled = readOnly;
                }

                var whatToSay = $(this).attr("data-hint");//don't use .data(), as that will trip over any } in the hint and try to interpret it as json

                whatToSay = theOneLocalizationManager.getLocalizedHint(whatToSay, $(this)) + " <br/>" + whyDisabled;
                var theClasses = 'ui-tooltip-shadow ui-tooltip-red';
                var pos = {
                    at: 'right center',
                    my: 'left center'
                };
                $(this).qtip({
                    content: whatToSay,
                    position: pos,
                    show: {
                        event: 'focusin mouseenter'
                    },
                    style: {
                        classes: theClasses
                    }
                    , container: bloomQtipUtils.qtipZoomContainer()
                });
            }
        });
    }
}
