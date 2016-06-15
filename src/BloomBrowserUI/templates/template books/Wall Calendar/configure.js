//
// Typescript configuration file for Bloom Wall Calendar
//
// Creates calendar pages for a Bloom book.
//
//   This script is fed a configuration object as a JSON string with the following elements:
//       configuration.calendar.year
//
//       These ones are in the "library" zone because they will be reused in future years/other calendar types
//       configuration.library.calendar.monthNames[0..11]
//       configuration.library.calendar.dayAbbreviations[0..6]
//
//   This script relies on the 2 pages that should be in the DOM it operates on:
//       One with classes 'calendarMonthTop'
//       One with classes 'calendarMonthBottom'
//
/// <reference path="jquery.d.ts" />
//
// This is the main public entry point called by Configurator.ConfigureBookInternal()
// in a context where the current dom is the book.
//
function runUpdate(jsonConfig) {
    var configurator = new CalendarConfigurator(jsonConfig);
    configurator.updateDom();
}
//
// Used to create a calendar in a Wall Calendar book
//
var CalendarConfigurator = (function () {
    function CalendarConfigurator(jsonConfig) {
        if (jsonConfig && jsonConfig['library'])
            this.configObject = new CalendarConfigObject(jsonConfig);
        else
            this.configObject = new CalendarConfigObject(); // shouldn't happen, even in test...
    }
    //
    // Updates the dom to reflect the given configuration settings
    //
    CalendarConfigurator.prototype.updateDom = function () {
        var year = this.configObject.year;
        var originalMonthsPicturePage = $('.calendarMonthTop')[0];
        var pageToInsertAfter = originalMonthsPicturePage;
        for (var month = 0; month < 12; month++) {
            var monthsPicturePage = $(originalMonthsPicturePage).clone()[0];
            $(monthsPicturePage).removeClass('templateOnly').removeAttr('id'); // don't want to copy a Guid!
            $(pageToInsertAfter).after(monthsPicturePage);
            var monthDaysPage = this.generateMonth(year, month, this.configObject.monthNames[month], this.configObject.dayAbbreviations);
            $(monthsPicturePage).after(monthDaysPage);
            pageToInsertAfter = monthDaysPage;
        }
        $('.templateOnly').remove(); // removes 2 template pages (calendarMonthTop and calendarMonthBottom)
    };
    CalendarConfigurator.prototype.generateMonth = function (year, month, monthName, dayAbbreviations) {
        var marginBox = document.createElement('div');
        $(marginBox).addClass('marginBox');
        var monthPage = document.createElement('div');
        $(monthPage).addClass('bloom-page bloom-required A5Landscape calendarMonthBottom');
        $(monthPage).attr('data-page', 'required');
        this.buildCalendarBottomPage(marginBox, year, month, monthName, dayAbbreviations);
        monthPage.appendChild(marginBox);
        return monthPage;
    };
    CalendarConfigurator.prototype.buildCalendarBottomPage = function (bottomPageContainer, year, month, monthName, dayAbbreviations) {
        var header = document.createElement('p');
        $(header).addClass('calendarBottomPageHeader');
        $(header).text(monthName + " " + year);
        $(bottomPageContainer).append(header);
        var table = document.createElement('table');
        this.buildCalendarHeader(table, dayAbbreviations);
        this.buildCalendarBody(table, year, month);
        $(bottomPageContainer).append(table);
    };
    CalendarConfigurator.prototype.buildCalendarHeader = function (containingTable, dayAbbreviations) {
        var thead = document.createElement('thead');
        var row = document.createElement('tr');
        dayAbbreviations.forEach(function (abbr) {
            var thElem = document.createElement('th');
            $(thElem).text(abbr);
            $(row).append(thElem);
        });
        $(thead).append(row);
        $(containingTable).append(thead);
    };
    CalendarConfigurator.prototype.buildCalendarBody = function (containingTable, year, month) {
        var body = document.createElement('tbody');
        var start = new Date(parseInt(year), month, 1);
        start.setDate(1 - start.getDay());
        do {
            start = this.buildWeek(body, start, month);
        } while (start.getMonth() == month);
        $(containingTable).append(body);
    };
    CalendarConfigurator.prototype.buildWeek = function (body, date, month) {
        var row = document.createElement('tr');
        for (var i = 0; i < 7; i++) {
            var dayCell = document.createElement('td');
            if (date.getMonth() == month) {
                var dayNumberElement = document.createElement('p');
                $(dayNumberElement).text(date.getDate());
                $(dayCell).append(dayNumberElement);
                var dayCellTextArea = document.createElement('textarea');
                $(dayCell).append(dayCellTextArea);
            }
            $(row).append(dayCell);
            date.setDate(date.getDate() + 1);
        }
        $(body).append(row);
        return date;
    };
    return CalendarConfigurator;
})();
var CalendarConfigObject = (function () {
    function CalendarConfigObject(jsonConfig) {
        this.defaultConfig = {
            "calendar": { "year": "2015" },
            "library": {
                "calendar": {
                    "monthNames": ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"],
                    "dayAbbreviations": ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]
                }
            }
        };
        if (typeof jsonConfig === "undefined" || !jsonConfig['library'])
            jsonConfig = this.defaultConfig;
        this.year = jsonConfig.calendar.year;
        this.monthNames = jsonConfig.library.calendar.monthNames;
        this.dayAbbreviations = jsonConfig.library.calendar.dayAbbreviations;
    }
    return CalendarConfigObject;
})();
// Test class for manual debugging
// Just load DistFiles\factoryCollections\Templates\Wall Calendar\Wall Calendar.htm into Firefox
// and type "TestCalendar()" in Firefox's Console tab to debug
var TestCalendar = (function () {
    function TestCalendar() {
        // test config is in French, just for fun
        this.testConfig = '{"calendar": { "year": "2015" },' + '"library":  {"calendar": {' + '"monthNames": ["janv", "fév", "mars", "avr", "mai", "juin", "juil", "aôut", "sept", "oct", "nov", "déc"],' + '"dayAbbreviations": ["Dim", "Lun", "Mar", "Mer", "Jeu", "Ven", "Sam"]}}}';
        var configurator = new CalendarConfigurator(this.testConfig);
        configurator.updateDom();
    }
    return TestCalendar;
})();
