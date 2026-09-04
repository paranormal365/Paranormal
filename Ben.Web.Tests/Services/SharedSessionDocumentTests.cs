using Ben.Data.WebApi.Services;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Taking a session document out to a stranger without taking the address with it (item 207).
/// </summary>
/// <remarks>
/// This is the security boundary of the whole share feature. A share link is deliberately handed
/// to somebody the site knows nothing about, and a session recorded in a private residence carries
/// a GPS fix on every reading — which is to say, it carries the client's street address. Every
/// test here exists because a plausible implementation gets that specific case wrong.
/// </remarks>
public sealed class SharedSessionDocumentTests
{
    /// <summary>A document shaped like the ones the app actually uploads.</summary>
    private const string Document = """
    {
      "format_version": "1.0.0",
      "device": { "manufacturer": "Apple", "model": "iPhone17,1" },
      "session": { "started_at": "2026-09-01T22:00:00Z", "location_label": "back bedroom",
                   "trigger": { "mode": "interval", "interval_seconds": 1 } },
      "readings": [
        { "at": "2026-09-01T22:00:01Z",
          "position": { "latitude": 36.1627, "longitude": -86.7816, "accuracy_meters": 32, "floor": 2 },
          "measurements": { "emf": { "value": 4.1, "unit": "uT" } } },
        { "at": "2026-09-01T22:00:02Z",
          "position": { "latitude": 36.1628, "longitude": -86.7815, "accuracy_meters": 30 } }
      ]
    }
    """;

    [Fact]
    public void Coordinates_do_not_travel_by_default()
    {
        var prepared = SharedSessionDocument.Prepare(Document, includePositions: false);

        Assert.NotNull(prepared);
        Assert.True(prepared!.Value.PositionsWithheld);
        // Asserted against the STRING, not against a parsed shape: the string is what is sent, and
        // a test that only inspects a tree it parsed itself would pass on a document that shipped
        // the numbers in a field the parser ignored.
        Assert.DoesNotContain("36.1627", prepared.Value.Document);
        Assert.DoesNotContain("36.1628", prepared.Value.Document);
        Assert.DoesNotContain("-86.7816", prepared.Value.Document);
        Assert.DoesNotContain("-86.7815", prepared.Value.Document);
    }

    [Fact]
    public void The_readings_themselves_survive_the_redaction()
    {
        var prepared = SharedSessionDocument.Prepare(Document, includePositions: false);

        // The point of sharing is the evidence. A redaction that also took the measurements would
        // be safe and useless, and nothing else in this class would notice.
        Assert.Contains("\"emf\"", prepared!.Value.Document);
        Assert.Contains("4.1", prepared.Value.Document);
        Assert.Contains("back bedroom", prepared.Value.Document);
        Assert.Contains("2026-09-01T22:00:01Z", prepared.Value.Document);
    }

    [Fact]
    public void Accuracy_and_floor_stay_because_they_say_nothing_about_where()
    {
        var prepared = SharedSessionDocument.Prepare(Document, includePositions: false);
        var position = JsonNode.Parse(prepared!.Value.Document)!["readings"]![0]!["position"]!;

        // "Second floor, accurate to thirty metres" tells a reviewer how much to trust the numbers
        // around it and where in the building the device was. Neither locates the building.
        Assert.Equal(32, position["accuracy_meters"]!.GetValue<double>());
        Assert.Equal(2, position["floor"]!.GetValue<int>());
    }

    [Fact]
    public void The_position_key_survives_so_the_player_still_recognises_the_shape()
    {
        var prepared = SharedSessionDocument.Prepare(Document, includePositions: false);
        var root = JsonNode.Parse(prepared!.Value.Document)!;

        var position = root["readings"]![0]!["position"]!;
        // Nulled, not deleted. The format permits null for both, and a null reads as "no fix" —
        // a state every indoor session reaches honestly. Removing the keys would hand the player
        // a document structurally unlike every other one it has ever parsed.
        Assert.Null(position["latitude"]);
        Assert.Null(position["longitude"]);
        Assert.NotNull(position["accuracy_meters"]);
    }

    [Fact]
    public void A_coordinate_hidden_somewhere_the_format_never_named_is_still_removed()
    {
        // The format declares additionalProperties: true, so a device is free to write a fix in a
        // vendor block this code has never seen. A redaction that only cleared readings[].position
        // would pass every other test in this class and ship this document's coordinates.
        const string vendor = """
        {
          "format_version": "1.0.0",
          "readings": [
            { "at": "2026-09-01T22:00:01Z",
              "vendor": { "gps": { "lat": 36.1627, "lng": -86.7816 } } }
          ]
        }
        """;

        var prepared = SharedSessionDocument.Prepare(vendor, includePositions: false);

        Assert.True(prepared!.Value.PositionsWithheld);
        Assert.DoesNotContain("36.1627", prepared.Value.Document);
        Assert.DoesNotContain("-86.7816", prepared.Value.Document);
    }

    [Fact]
    public void A_session_that_never_had_a_fix_does_not_claim_one_was_removed()
    {
        const string indoors = """
        {
          "format_version": "1.0.0",
          "readings": [
            { "at": "2026-09-01T22:00:01Z",
              "position": { "latitude": null, "longitude": null, "accuracy_meters": null },
              "measurements": { "emf": { "value": 4.1, "unit": "uT" } } }
          ]
        }
        """;

        var prepared = SharedSessionDocument.Prepare(indoors, includePositions: false);

        // Saying "the locations were not shared" about a night that had none would be a lie in the
        // reassuring direction — the recipient would believe something was withheld from them that
        // never existed, and would read the group as less forthcoming than it was.
        Assert.False(prepared!.Value.PositionsWithheld);
    }

    [Fact]
    public void Choosing_to_include_them_sends_the_document_exactly_as_the_device_wrote_it()
    {
        var prepared = SharedSessionDocument.Prepare(Document, includePositions: true);

        // Byte-identical, not merely equivalent. Everywhere else on the site the document is served
        // verbatim because it is the only copy that is definitely what the instruments recorded;
        // an opt-in share must not quietly become a reformatted copy of it.
        Assert.Equal(Document, prepared!.Value.Document);
        Assert.False(prepared.Value.PositionsWithheld);
    }

    [Fact]
    public void A_document_that_cannot_be_read_is_refused_rather_than_forwarded()
    {
        var prepared = SharedSessionDocument.Prepare("{ this is not json", includePositions: false);

        // The one failure this feature must not have. A document this code cannot parse is one it
        // cannot redact, and forwarding it on the hope that it holds no fix is precisely the bet
        // that loses somebody's address. Null makes the caller answer "the readings cannot be
        // shown", which is a worse page and a correct one.
        Assert.Null(prepared);
    }

    [Fact]
    public void An_unreadable_document_still_goes_out_when_positions_were_opted_in()
    {
        // Nothing needs redacting, so nothing can fail to be redacted. Refusing here would break
        // sharing for a device whose output this server merely cannot parse, for no gain at all.
        var prepared = SharedSessionDocument.Prepare("{ this is not json", includePositions: true);

        Assert.NotNull(prepared);
        Assert.False(prepared!.Value.PositionsWithheld);
    }

    [Fact]
    public void The_result_is_still_valid_json()
    {
        var prepared = SharedSessionDocument.Prepare(Document, includePositions: false);

        // A redaction that produced something the player cannot parse would fail as a chart that
        // silently never draws, which reads as a quiet night rather than a broken document.
        var exception = Record.Exception(() => JsonDocument.Parse(prepared!.Value.Document));
        Assert.Null(exception);
    }
}
