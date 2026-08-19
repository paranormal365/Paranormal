// Wraps navigator.geolocation.getCurrentPosition as a Promise for Blazor JS interop.
window.benGetCurrentPosition = function () {
    return new Promise(function (resolve, reject) {
        if (!navigator.geolocation) {
            reject('Geolocation is not supported by this browser.');
            return;
        }
        navigator.geolocation.getCurrentPosition(
            function (pos) {
                resolve({ latitude: pos.coords.latitude, longitude: pos.coords.longitude });
            },
            function (err) {
                reject(err.message || 'Location access denied.');
            },
            { enableHighAccuracy: false, timeout: 10000, maximumAge: 60000 }
        );
    });
};
