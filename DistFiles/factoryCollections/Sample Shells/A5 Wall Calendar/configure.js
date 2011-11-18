// Copyright 2011 SIL International.  License: Academic Free License

/**
* @fileoverview Creates calendar pages for a Bloom book.
*
*   This script is fed a configuration object with elements
*       configuration.calendar.year
*
*       These ones are in the "project" zone because they will be reused in future years/other calendar types
*       configuration.project.calendar.monthNames[0..11]
*       configuration.project.calendar.dayAbbreviations[0..6]
*
*   This script relies on 3 of the pages that should be in the DOM it operates on:
*       One with class 'titlePage'
*       One with classes 'calendarMonthTop'
*       One with classes 'calendarMonthBottom'
*/

function test() {
    var configuration = {   "calendar": { "year": "2012" },
                            "project": { "calendar":
                                                { "monthNames": ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"],
                                                    "dayAbbreviations": ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]
                                                }
                                            }
    };
    updateDom(configuration);
}

/**
* Updates the dom to reflect the given configuration settings
* Called directly by Bloom, in a context where the current dom is the book.
* @param {configuration} members come from the name attributes of the corresponding configuration.htm file.
*       Put a new input control in that file, give it a @name attribute, and the value will be available here.
*/
function updateDom(configuration) {
    var year = Number.from(configuration.calendar.year);
    var previous = $$('.titlePage')[0];
    var originalMonthsPicturePage = $$('.calendarMonthTop')[0];
    for (var month = 0; month < 12; month++) {

        var monthsPicturePage = originalMonthsPicturePage.clone();
        monthsPicturePage.removeClass('templateOnly');

        monthsPicturePage.inject(previous, "after");

        var monthDaysPage = generateMonth(year, month, configuration.project.calendar.monthNames[month], configuration.project.calendar.dayAbbreviations);
        monthDaysPage.inject(monthsPicturePage, "after");
        previous = monthDaysPage;
    }
    $('.templateOnly').remove();
}

function generateMonth(year, month, monthName, dayAbbreviations) {
    var monthPage = new Element("div", {
        "class": "-bloom-page -bloom-required calendarMonthBottom"
    });
    new CalConf(monthPage).draw(year, month, monthName, dayAbbreviations);
    return monthPage;
}

var CalConf = new Class({
    Implements: [Options],

    options: {},

    initialize: function (wrapper, options) {
        this.wrapper = wrapper;
        this.setOptions(options);
        this.ndays = 7;/* TODO*/
    },

    draw: function (year, month, monthName, dayAbbreviations) {
        var header = new Element("p");
        header.setAttribute("class", "calendarBottomPageHeader");
        header.set("text", monthName + " " + year)
        header.inject(this.wrapper);
        this.table = new Element("table");
        this.drawHeader(year, month, dayAbbreviations);
        this.drawBody(year, month);
        this.table.inject(this.wrapper);
    },

    drawHeader: function (year, month, dayAbbreviations) {
        var thead = new Element("thead");
        var row = new Element("tr");
        dayAbbreviations.each(function (n) {
            new Element("th").set("text", n).inject(row);
        });
        row.inject(thead);
        thead.inject(this.table);
    },

    drawBody: function (year, month) {
        var body = new Element("tbody");
        var start = new Date(year, month, 1);
        start.setDate(1 - start.getDay());
        do {
            start = this.drawWeek(body, start, month);
        } while (start.getMonth() == month);
        body.inject(this.table);
    },

    drawWeek: function (body, date, month) {
        var row = new Element("tr");
        for (var i = 0; i < 7; i++) {
            var dayCell = new Element("td");
            if (date.getMonth() == month) {
                var dayNumberElement = new Element("p");
                dayNumberElement.set("text", date.getDate());
                dayNumberElement.inject(dayCell);
                var holidayText = new Element("textarea");
                holidayText.inject(dayCell);
            }
            dayCell.inject(row);
            date.increment();
        }
        row.inject(body);
        return date;
    }
});
