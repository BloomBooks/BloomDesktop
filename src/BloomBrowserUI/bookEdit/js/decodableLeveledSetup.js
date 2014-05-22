// listen for messages sent to the iframe
window.addEventListener('message', processMessage, false);

function processMessage(event) {

    switch(event.data) {
        case 'OK':
            saveClicked();
            return;

        default:
            return;
    }
}

function saveClicked() {
    alert('Save Clicked');
}

function openTextFolder() {
    window.open('file:///C:/Projects/');
}
