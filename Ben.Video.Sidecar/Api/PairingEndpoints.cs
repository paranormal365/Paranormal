using Ben.Video.Sidecar.Security;

namespace Ben.Video.Sidecar.Api;

/// <summary>
/// The two halves of 6-digit pairing: the human-facing page that shows the code, and the
/// endpoint the editor exchanges it through.
/// </summary>
/// <remarks>
/// <para><c>GET /pair</c> is a top-level browser page (opened by the installer / pair script), so
/// it arrives with no Origin header — SecurityMiddleware lets it through the same way it does a
/// bare health check. Loading it starts a fresh pairing window. Anything running as this user
/// could read the page, but anything running as this user can also read the token file directly,
/// so the page adds no new exposure.</para>
///
/// <para><c>POST /v1/pair</c> is called by the editor (allowlisted Origin enforced, no token yet —
/// obtaining the token is the point). Wrong codes feed the same <see cref="AuthFailureThrottle"/>
/// as bad tokens, so guessing a 6-digit code runs into 429s almost immediately.</para>
/// </remarks>
public static class PairingEndpoints
{
    public static void MapPairingEndpoints(this WebApplication app)
    {
        app.MapGet("/pair", (PairingTokenStore store) =>
        {
            var code = store.BeginPairing();
            var expiresLocal = store.CodeExpiresUtc.ToLocalTime().ToString("h:mm tt");
            return Results.Content($$"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="utf-8" />
                    <title>Pair the video editor</title>
                    <meta name="viewport" content="width=device-width, initial-scale=1" />
                    <style>
                        body { font-family: -apple-system, 'Segoe UI', sans-serif; background: #141414;
                               color: #e8e8e8; display: grid; place-items: center; min-height: 100vh; margin: 0; }
                        main { text-align: center; padding: 2rem; }
                        .code { font-size: 4rem; letter-spacing: 0.35em; font-weight: 700; color: #fff;
                                margin: 1.5rem 0 0.5rem; font-variant-numeric: tabular-nums; }
                        p  { color: #aaa; max-width: 26rem; line-height: 1.5; }
                        .exp { color: #888; font-size: 0.9rem; }
                    </style>
                </head>
                <body>
                    <main>
                        <h1>Pairing code</h1>
                        <div class="code">{{code}}</div>
                        <p class="exp">Works until {{expiresLocal}}, for one browser. Reload this page for a new code.</p>
                        <p>In the video editor, open <strong>Settings &rarr; Native acceleration</strong> and
                           type this code. Each browser you pair needs its own code &mdash; pairing one
                           does not un-pair another.</p>
                    </main>
                </body>
                </html>
                """, "text/html");
        });

        app.MapPost("/v1/pair", (PairRequest request, PairingTokenStore store, AuthFailureThrottle throttle) =>
        {
            var token = store.TryExchangeCode(request.Code);
            if (token is null)
            {
                // Same ledger as bad tokens: a few wrong codes and the middleware's throttle gate
                // starts answering 429 before this endpoint is ever reached again.
                throttle.RecordFailure();
                return Results.Unauthorized();
            }

            return Results.Ok(new PairResponse(token));
        });
    }

    public sealed record PairRequest(string Code);
    public sealed record PairResponse(string Token);
}
