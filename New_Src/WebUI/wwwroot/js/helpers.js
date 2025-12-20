// ICP WebUI JavaScript Helpers

// Download file from base64 data
window.downloadFile = function (fileName, contentType, base64Data) {
    const link = document.createElement('a');
    link.href = 'data:' + contentType + ';base64,' + base64Data;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

// Download file from blob
window.downloadBlob = function (fileName, contentType, byteArray) {
    const blob = new Blob([byteArray], { type: contentType });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
};

// Copy text to clipboard
window.copyToClipboard = function (text) {
    return navigator.clipboard.writeText(text).then(function () {
        return true;
    }).catch(function (err) {
        console.error('Could not copy text: ', err);
        return false;
    });
};

// Show browser notification
window.showNotification = function (title, body, icon) {
    if (!("Notification" in window)) {
        return Promise.resolve(false);
    }

    if (Notification.permission === "granted") {
        new Notification(title, { body: body, icon: icon });
        return Promise.resolve(true);
    } else if (Notification.permission !== "denied") {
        return Notification.requestPermission().then(function (permission) {
            if (permission === "granted") {
                new Notification(title, { body: body, icon: icon });
                return true;
            }
            return false;
        });
    }
    return Promise.resolve(false);
};

// Get window dimensions
window.getWindowDimensions = function () {
    return {
        width: window.innerWidth,
        height: window.innerHeight
    };
};

// Scroll to element
window.scrollToElement = function (elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        element.scrollIntoView({ behavior: 'smooth', block: 'start' });
        return true;
    }
    return false;
};

// Print specific element
window.printElement = function (elementId) {
    const element = document.getElementById(elementId);
    if (!element) return false;

    const printWindow = window.open('', '_blank');
    printWindow.document.write('<html><head><title>Print</title>');
    printWindow.document.write('<link rel="stylesheet" href="_content/MudBlazor/MudBlazor.min.css" />');
    printWindow.document.write('</head><body>');
    printWindow.document.write(element.innerHTML);
    printWindow.document.write('</body></html>');
    printWindow.document.close();
    printWindow.print();
    return true;
};

// Save data to local storage
window.saveToLocalStorage = function (key, value) {
    try {
        localStorage.setItem(key, JSON.stringify(value));
        return true;
    } catch (e) {
        console.error('Error saving to localStorage:', e);
        return false;
    }
};

// Load data from local storage
window.loadFromLocalStorage = function (key) {
    try {
        const value = localStorage.getItem(key);
        return value ? JSON.parse(value) : null;
    } catch (e) {
        console.error('Error loading from localStorage:', e);
        return null;
    }
};

// Remove data from local storage
window.removeFromLocalStorage = function (key) {
    try {
        localStorage.removeItem(key);
        return true;
    } catch (e) {
        console.error('Error removing from localStorage:', e);
        return false;
    }
};

// Focus element
window.focusElement = function (elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        element.focus();
        return true;
    }
    return false;
};

// Blur element (remove focus)
window.blurElement = function (elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        element.blur();
        return true;
    }
    return false;
};

// Prevent default drag/drop behavior on dropzone
window.initDropZone = function (elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
            element.addEventListener(eventName, (e) => {
                e.preventDefault();
                e.stopPropagation();
            }, false);
        });
        return true;
    }
    return false;
};

// Trigger file input click
window.triggerFileInput = function (elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        element.click();
        return true;
    }
    return false;
};
