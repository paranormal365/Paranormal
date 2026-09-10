/**
 * PublicCaseDiscovery.razor.js — asks the browser where the person is.
 *
 * That is all that is left here. The map, its tiles, its markers and their clicks moved to
 * Kit/Maps/BenMap with item 228; this module never touches a map.
 */

export function tryGetUserLocation(dotnetRef) {
    if (!navigator.geolocation) {
        dotnetRef.invokeMethodAsync('SetUserLocation', null, null)
        return
    }
    navigator.geolocation.getCurrentPosition(
        pos  => dotnetRef.invokeMethodAsync('SetUserLocation', pos.coords.latitude, pos.coords.longitude),
        _err => dotnetRef.invokeMethodAsync('SetUserLocation', null, null),
        { timeout: 6000, maximumAge: 300000 }
    )
}
