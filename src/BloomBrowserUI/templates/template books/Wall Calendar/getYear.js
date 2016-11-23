// Get a default year for the Calendar configuration dialog
// If after May use next year, otherwise use the current year.
function getYear() {
    var d = new Date();
    var month = d.getMonth() + 1;
    var year = d.getFullYear();

    var x = document.getElementById("dateInput");
    x.value = month > 5 ? year + 1 : year;
}