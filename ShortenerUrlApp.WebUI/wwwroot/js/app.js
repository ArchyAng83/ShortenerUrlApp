// Small JS helpers used from Blazor (kept minimal on purpose).

window.shortenerDownload = function (url, fileName) {
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
};
