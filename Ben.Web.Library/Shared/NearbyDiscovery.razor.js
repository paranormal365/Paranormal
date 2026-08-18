/**
 * NearbyDiscovery.razor.js
 * ─────────────────────────
 * Colocated ES module for the NearbyDiscovery Blazor component.
 *
 * One job: ask the browser where the visitor is, once, and hand the answer back to Blazor. No map,
 * no globals shared with other components — the discovery map (PublicCaseDiscovery.razor.js) is a
 * heavier dependency this component deliberately does not take on.
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
