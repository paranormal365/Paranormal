// The browser half of the checkout's payment step (storefront S4.11): Stripe's Payment Element.
//
// Card details are typed into Stripe's own frame and go from the browser to Stripe — they never
// reach our server, which only ever sees the order and the PaymentIntent it made. Stripe.js is
// loaded the first time a buyer reaches the payment step, not on every page: nothing else on the
// site needs it, and a visitor who only browses never downloads it.
//
// Blazor owns everything around the slot; this owns what is inside it. `confirm` answers in codes
// the page can act on (`payment_intent_unexpected_state` means "prepare again"), and Stripe's own
// message for anything a buyer can fix (a declined card, a wrong expiry).

let _stripeLoading = null
let _stripe = null
let _stripeKey = null
let _elements = null
let _payment = null

function loadStripeJs() {
    if (window.Stripe) return Promise.resolve()
    _stripeLoading ??= new Promise((resolve, reject) => {
        const script = document.createElement('script')
        script.src = 'https://js.stripe.com/v3/'
        script.async = true
        script.onload = () => resolve()
        script.onerror = () => { _stripeLoading = null; reject(new Error('Stripe.js did not load')) }
        document.head.appendChild(script)
    })
    return _stripeLoading
}

function isDark() {
    return document.documentElement.getAttribute('data-bs-theme') === 'dark'
}

// Mounts the Payment Element in `slot` for this order's PaymentIntent. Answers null when mounted,
// or a sentence when it could not be (Stripe.js blocked, a network failure).
export async function mount(slot, publishableKey, clientSecret, billing) {
    dispose()
    if (!slot) return 'The payment form has nowhere to go — reload the page.'
    try {
        await loadStripeJs()
    } catch {
        return "The payment form couldn't be loaded. Check your connection (or an ad blocker) and try again."
    }
    if (!_stripe || _stripeKey !== publishableKey) {
        _stripe = window.Stripe(publishableKey)
        _stripeKey = publishableKey
    }
    _elements = _stripe.elements({
        clientSecret,
        appearance: { theme: isDark() ? 'night' : 'stripe', variables: { borderRadius: '6px' } },
    })
    _payment = _elements.create('payment', {
        layout: 'tabs',
        defaultValues: { billingDetails: billing ?? undefined },
    })
    return await new Promise(resolve => {
        _payment.on('ready', () => resolve(null))
        _payment.on('loaderror', e => resolve(e?.error?.message ?? "The payment form couldn't be loaded."))
        _payment.mount(slot)
    })
}

// Confirms the payment. Card payments settle here without leaving the page; methods that need
// the bank's own page leave for it and come back to `returnUrl`.
// Answers { ok: true } or { ok: false, code, message }.
export async function confirm(returnUrl) {
    if (!_stripe || !_elements) return { ok: false, code: 'not_mounted', message: 'The payment form is not ready yet.' }
    const { error, paymentIntent } = await _stripe.confirmPayment({
        elements: _elements,
        confirmParams: { return_url: new URL(returnUrl, window.location.origin).toString() },
        redirect: 'if_required',
    })
    if (error) return { ok: false, code: error.code ?? error.type ?? 'error', message: error.message ?? 'The payment did not go through.' }
    return { ok: true, status: paymentIntent?.status ?? null }
}

export function dispose() {
    try { _payment?.destroy() } catch { /* already gone with its element */ }
    _payment = null
    _elements = null
}
