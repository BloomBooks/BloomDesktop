/// <reference path="../../typings/jquery/jquery.d.ts" />
/// <reference path="../../lib/localizationManager/localizationManager.ts" />
/// <reference path="bloomQtipUtils.ts" />
/// <reference path="../../typings/jquery.qtip.d.ts" />
/// <reference path="../../typings/jquery.qtipSecondary.d.ts" />

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
//import '../../lib/jquery.qtip.js'
//import '../../lib/jquery.qtipSecondary.js'
import bloomQtipUtils from "./bloomQtipUtils";
import $ from "jquery";

export default class BloomNotices {
    public static addExperimentalNotice(container: HTMLElement): void {
        const experimental = theOneLocalizationManager.getText(
            "EditTab.ExperimentalNotice",
            "This page is an experimental prototype which may have many problems, for which we apologize.",
        );
        $(container)
            .find(".pictureDictionaryPage")
            .each(function () {
                $(this).qtipSecondary({
                    content:
                        "<div id='experimentNotice'><img src='/bloom/images/experiment.png'/>" +
                        experimental +
                        "<div/>",
                    show: { ready: true },
                    hide: false,
                    position: {
                        at: "right top",
                        my: "left top",
                        container: bloomQtipUtils.qtipZoomContainer(),
                    },
                    style: {
                        classes: "ui-tooltip-red",
                        tip: { corner: false },
                    },
                });
            });
    }
}
