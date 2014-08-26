$(function() {
    $('div.split-pane').splitPane();
});
$(window).load(function() {
    $('.split-pane-component-inner').append(getTypeSelectors());
    $('.split-pane-component-inner').append(getButtons());
    $('.add').click(addClickHandler);
});

function splitClickHandler() {
    var myInner = $(this).closest('.split-pane-component-inner');
    var newSplitPane;
    if ($(this).hasClass('vertical')) {
        myInner.wrap(getSplitPaneHtml('vertical'));
        myInner.wrap(getSplitPaneComponentHtml('left'));
        newSplitPane = myInner.closest('.split-pane');
        newSplitPane.append(getSplitPaneDividerHtml('vertical'));
        newSplitPane.append(getSplitPaneComponentWithNewContent('right'));
    } else {
        myInner.wrap(getSplitPaneHtml('horizontal'));
        myInner.wrap(getSplitPaneComponentHtml('top'));
        newSplitPane = myInner.closest('.split-pane');
        newSplitPane.append(getSplitPaneDividerHtml('horizontal'));
        newSplitPane.append(getSplitPaneComponentWithNewContent('bottom'));
    }
    newSplitPane.splitPane();
}

function addClickHandler() {
    var topSplitPane = $('.split-pane-frame').children('div').first();
    if ($(this).hasClass('right')) {
        topSplitPane.wrap(getSplitPaneHtml('vertical'));
        topSplitPane.wrap(getSplitPaneComponentHtml('left'));
        var newSplitPane = $('.split-pane-frame').children('div').first();
        newSplitPane.append(getSplitPaneDividerHtml('vertical'));
        newSplitPane.append(getSplitPaneComponentWithNewContent('right'));
        newSplitPane.splitPane();
    } else if ($(this).hasClass('left')) {
        topSplitPane.wrap(getSplitPaneHtml('vertical'));
        topSplitPane.wrap(getSplitPaneComponentHtml('right'));
        var newSplitPane = $('.split-pane-frame').children('div').first();
        newSplitPane.prepend(getSplitPaneDividerHtml('vertical'));
        newSplitPane.prepend(getSplitPaneComponentWithNewContent('left'));
        newSplitPane.splitPane();
    }
}

function closeClickHandler() {
    if (!$('.split-pane'))
        return;

    var myComponent = $(this).closest('.split-pane-component');
    var sibling = myComponent.siblings('.split-pane-component').first('div');
    var toReplace = myComponent.parent().parent();
    var positionClass = toReplace.attr('class');
    toReplace.replaceWith(sibling);

    // The idea here is we need the position-* class from the parent to replace the sibling's position-* class.
    // This is working for now, but should be cleaned up since it could also add other classes.
    sibling.removeClass(function (index, css) {
        return (css.match (/(^|\s)position-\S+/g) || []).join(' ');
    });
    sibling.addClass(positionClass);
}

function getSplitPaneHtml(verticalOrHorizontal) {
    return $('<div class="split-pane ' + verticalOrHorizontal + '-percent"></div>');
}
function getSplitPaneComponentHtml(leftOrRight) {
    return $('<div class="split-pane-component position-' + leftOrRight + '"></div>');
}
function getSplitPaneDividerHtml(verticalOrHorizontal) {
    return $('<div class="split-pane-divider ' + verticalOrHorizontal + '-divider"></div>');
}
function getSplitPaneComponentWithNewContent(rightOrBottom) {
    var spc = $('<div class="split-pane-component position-' + rightOrBottom + '">');
    spc.append(getSplitPaneComponentInner());
    return spc;
}
function getSplitPaneComponentInner() {
    var spci = $('<div class="split-pane-component-inner">');
    spci.append(getTypeSelectors());
    spci.append(getButtons());
    return spci;
}

function getButtons() {
    var buttons = $('<div class="buttons"></div>');
    buttons.append(getVSplitButton())
        .append('&nbsp;')
        .append(getHSplitButton())
        .append('&nbsp;')
        .append(getCloseButton());
    return buttons;
}
function getVSplitButton() {
    var vSplitButton = $('<a class="button bloom-purple splitter vertical bloom-ui">&#124;</a>');
    vSplitButton.click(splitClickHandler);
    return vSplitButton;
}
function getHSplitButton() {
    var hSplitButton = $('<a class="button bloom-purple splitter horizontal bloom-ui">&#8211;&#8211;&#8211;</a>');
    hSplitButton.click(splitClickHandler);
    return hSplitButton;
}
function getCloseButton() {
    var closeButton = $('<a class="button bloom-purple close bloom-ui">&#215;</a>');
    closeButton.click(closeClickHandler);
    return closeButton;
}
function getTypeSelectors() {
    var links = $('<div class="selector-links bloom-ui"><a href="">Picture</a> or <a href="">Text</a></div>');
    return links;
}
