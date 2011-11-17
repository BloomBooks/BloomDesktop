// Copyright 2011 SIL International.  License: Academic Free License

/**
* @fileoverview Creates calendar pages for a Bloom book.
*/

function test() {
    var configuration = { "calendar": { "year": "2012"} };
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
    for (var month = 0; month < 12; month++) {
        var monthsPicturePage = $$('.templateMonthPicturePage')[0].clone();
        monthsPicturePage.inject(previous, "after");

        var monthDaysPage = generateMonth(year, month);
        monthDaysPage.inject(monthsPicturePage, "after");
        previous = monthDaysPage;
    }
}

function generateMonth(year, month) {
    var monthPage = new Element("div", {
        "class": "-bloom-page -bloom-required calendarMonthBottom"
    });
    new CalConf(monthPage).draw(year, month);
    return monthPage;
}

var CalConf = new Class({
    Implements: [Options],

    options: {},

    initialize: function (wrapper, options) {
        this.wrapper = wrapper;
        this.setOptions(options);
        this.dayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
        this.ndays = this.dayNames.length;
        this.monthNames = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    },

    draw: function (year, month) {
        var header = new Element("p");
        header.setAttribute("class", "calendarBottomPageHeader");
        header.set("text", this.monthNames[month] + " " + year)
        header.inject(this.wrapper);
        this.table = new Element("table");
        this.drawHeader(year, month);
        this.drawBody(year, month);
        this.table.inject(this.wrapper);
    },

    drawHeader: function (year, month) {
        var thead = new Element("thead");
        var row = new Element("tr");
        this.dayNames.each(function (n) {
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
