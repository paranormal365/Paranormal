using System.Reflection;
using Ben.Canvas.Core.Formatting;
using Ben.Canvas.Core.Text;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Text
{
    /// <summary>
    /// Every sentence the board shows keeps the site's plain voice: no shouting, no apologies, buttons
    /// short, sentences finished.
    /// </summary>
    public sealed class CanvasCopyTests
    {
        private static IEnumerable<(string Class, string Name, string Value)> Strings(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

            foreach (var field in type.GetFields(flags).Where(f => f.FieldType == typeof(string)))
                yield return (type.Name, field.Name, (string)field.GetValue(null)!);

            foreach (var property in type.GetProperties(flags).Where(p => p.PropertyType == typeof(string)))
                yield return (type.Name, property.Name, (string)property.GetValue(null)!);

            foreach (var method in type.GetMethods(flags).Where(m => m.ReturnType == typeof(string) && !m.IsSpecialName))
            {
                var args = method.GetParameters().Select(p => p.ParameterType switch
                {
                    var t when t == typeof(int) => (object)3,
                    var t when t == typeof(long) => 1L,
                    _ => "x",
                }).ToArray();
                yield return (type.Name, method.Name, (string)method.Invoke(null, args)!);
            }
        }

        private static IEnumerable<(string Class, string Name, string Value)> All() =>
            typeof(CanvasCopy).GetNestedTypes().SelectMany(Strings);

        [Fact]
        public void Every_string_is_non_empty_and_trimmed()
        {
            foreach (var (cls, name, value) in All())
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{cls}.{name} is empty.");
                Assert.True(value == value.Trim(), $"{cls}.{name} has leading or trailing space.");
            }
        }

        [Fact]
        public void Buttons_are_four_words_or_fewer_without_end_punctuation()
        {
            foreach (var (_, name, value) in Strings(typeof(CanvasCopy.Buttons)))
            {
                Assert.True(value.Split(' ').Length <= 4, $"Button {name} is more than four words.");
                Assert.False(value.EndsWith('.') || value.EndsWith('!'), $"Button {name} ends in punctuation.");
            }
        }

        [Fact]
        public void Sentences_end_with_a_full_stop_or_question_mark()
        {
            foreach (var (_, name, value) in Strings(typeof(CanvasCopy.Sentences)))
                Assert.True(value.EndsWith('.') || value.EndsWith('?'), $"Sentence {name} does not end with a full stop: \"{value}\"");
        }

        [Fact]
        public void Nothing_shouts_or_apologises()
        {
            foreach (var (cls, name, value) in All())
            {
                Assert.False(value.Contains('!'), $"{cls}.{name} shouts: \"{value}\"");
                Assert.False(value.Contains("Oops", StringComparison.OrdinalIgnoreCase), $"{cls}.{name} says Oops.");
                Assert.False(value.Contains("successfully", StringComparison.OrdinalIgnoreCase), $"{cls}.{name} says successfully.");
                Assert.False(value.Contains("sorry", StringComparison.OrdinalIgnoreCase), $"{cls}.{name} apologises.");
            }
        }

        [Fact]
        public void The_heic_sentence_says_how_to_fix_it()
        {
            Assert.Contains("HEIC", CanvasCopy.Sentences.Heic);
            Assert.Contains("Share", CanvasCopy.Sentences.Heic);
        }

        [Fact]
        public void Nothing_to_paste_mentions_the_clipboard() =>
            Assert.Contains("nothing on the clipboard", CanvasCopy.Sentences.NothingToPaste);
    }
}

namespace Ben.Canvas.Tests.Formatting
{
    /// <summary>
    /// The browser runs with invariant globalization, so every date and size is formatted with an explicit
    /// pattern - and must still use a dot when the machine running the tests is French or German.
    /// </summary>
    public sealed class BcDateFormatTests
    {
        [Fact]
        public void Message_time_is_formatted_with_invariant_culture_and_explicit_pattern() => TestBoards.InCulture("fr-FR", () =>
        {
            var utc = new DateTime(2026, 9, 14, 18, 5, 0, DateTimeKind.Utc);
            Assert.Equal("Sep 14, 2026 1:05 PM", BcDateFormat.MessageTime(utc, TimeSpan.FromHours(-5)));
        });

        [Fact]
        public void A_card_date_round_trips_as_iso()
        {
            var date = new DateOnly(2026, 9, 14);
            Assert.Equal("2026-09-14", BcDateFormat.ToCardValue(date));
            Assert.True(BcDateFormat.TryParseCardValue("2026-09-14", out var read));
            Assert.Equal(date, read);
        }

        [Theory]
        [InlineData("14/09/2026")]
        [InlineData("")]
        [InlineData(null)]
        public void A_bad_card_date_is_not_a_date(string? value) => Assert.False(BcDateFormat.TryParseCardValue(value, out _));
    }

    public sealed class BcRelativeTimeTests
    {
        [Fact]
        public void Under_a_minute_is_just_now() => Assert.Equal("just now", BcRelativeTime.Format(TimeSpan.FromSeconds(30)));

        [Fact]
        public void Two_minutes_reads_2_min_ago() => Assert.Equal("2 min ago", BcRelativeTime.Format(TimeSpan.FromMinutes(2)));

        [Fact]
        public void A_future_time_reads_just_now() => Assert.Equal("just now", BcRelativeTime.Format(TimeSpan.FromMinutes(-5)));

        [Fact]
        public void Three_hours_reads_3_hr_ago() => Assert.Equal("3 hr ago", BcRelativeTime.Format(TimeSpan.FromHours(3)));

        [Fact]
        public void A_week_or_more_returns_null() => Assert.Null(BcRelativeTime.Format(TimeSpan.FromDays(7)));
    }

    public sealed class BcFileSizeTests
    {
        [Fact]
        public void A_French_culture_still_writes_a_dot() => TestBoards.InCulture("fr-FR", () =>
            Assert.Equal("1.5 KB", BcFileSize.Format(1536)));

        [Fact]
        public void Bytes_under_a_kilobyte_are_whole() => Assert.Equal("512 B", BcFileSize.Format(512));

        [Fact]
        public void Megabytes_have_one_decimal() => Assert.Equal("2.0 MB", BcFileSize.Format(2L * 1024 * 1024));
    }
}
