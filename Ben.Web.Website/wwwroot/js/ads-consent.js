// Google Ads measurement, only with the visitor's consent (10/19/2026).
//
// Loaded from App.razor ONLY when GoogleAds:TagId is configured; the tag id arrives on this
// script's data-tag-id attribute. Google Consent Mode v2: every purpose starts denied, and
// gtag.js itself is not fetched until the visitor presses Accept (now, or on an earlier visit).
// Decline, or no answer yet, and nothing is requested from Google at all.
//
// The choice is kept in localStorage under "ih-ads-consent" ("granted" or "denied"). The banner
// is server-rendered in AdsConsent.razor with `hidden`; this file shows it when no choice is kept,
// and the footer's "Cookie settings" calls benAdsConsent.open() to show it again.
(function (w) {
    "use strict";
    var d = w.document;
    var KEY = "ih-ads-consent";
    var script = d.currentScript;
    var tagId = script && script.getAttribute ? (script.getAttribute("data-tag-id") || "") : "";
    if (!tagId) return;

    w.dataLayer = w.dataLayer || [];
    function gtag() { w.dataLayer.push(arguments); }
    w.gtag = w.gtag || gtag;

    gtag("consent", "default", {
        ad_storage: "denied",
        ad_user_data: "denied",
        ad_personalization: "denied",
        analytics_storage: "denied"
    });

    function read() {
        try { return w.localStorage.getItem(KEY); } catch (e) { return null; }
    }
    function write(value) {
        try { w.localStorage.setItem(KEY, value); } catch (e) { /* private window: ask again next time */ }
    }

    var loaded = false;
    function load() {
        if (loaded) return;
        loaded = true;
        gtag("consent", "update", {
            ad_storage: "granted",
            ad_user_data: "granted",
            ad_personalization: "granted",
            analytics_storage: "granted"
        });
        var s = d.createElement("script");
        s.async = true;
        s.src = "https://www.googletagmanager.com/gtag/js?id=" + encodeURIComponent(tagId);
        d.head.appendChild(s);
        gtag("js", new Date());
        gtag("config", tagId);
    }

    function banner() { return d.getElementById("ads-consent-banner"); }
    function show(visible) {
        var b = banner();
        if (!b) return;
        if (visible) b.removeAttribute("hidden"); else b.setAttribute("hidden", "");
    }
    function sync() { show(read() === null); }

    function accept() {
        write("granted");
        show(false);
        load();
    }
    function decline() {
        var wasGranted = read() === "granted";
        write("denied");
        show(false);
        if (wasGranted) {
            gtag("consent", "update", {
                ad_storage: "denied",
                ad_user_data: "denied",
                ad_personalization: "denied",
                analytics_storage: "denied"
            });
        }
    }

    w.benAdsConsent = {
        status: function () { return read() || "unset"; },
        accept: accept,
        decline: decline,
        open: function () { show(true); },
        // The subscription conversion. Only with consent, and never anything about the person:
        // send_to is the configured tag id and label and nothing else.
        conversion: function (sendTo) {
            if (!sendTo || read() !== "granted") return false;
            load();
            gtag("event", "conversion", { send_to: sendTo });
            return true;
        }
    };

    d.addEventListener("click", function (e) {
        var t = e.target && e.target.closest ? e.target.closest("[data-ads-consent]") : null;
        if (!t) return;
        var what = t.getAttribute("data-ads-consent");
        if (what === "accept") accept();
        else if (what === "decline") decline();
        else if (what === "open") show(true);
    });

    if (read() === "granted") load();

    if (d.readyState === "loading") d.addEventListener("DOMContentLoaded", sync);
    else sync();
    // Enhanced navigation re-renders the shell from the server, which puts `hidden` back.
    d.addEventListener("enhancedload", sync);
})(window);
