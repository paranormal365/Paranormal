using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Service.Models.Feed;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Answering a poll (item 233, Ben 2026-09-11).
/// </summary>
/// <remarks>
/// <para>The four properties worth pinning: a second press <b>replaces</b> rather than adds, an
/// empty list <b>takes a vote back</b>, the total counts <b>people</b> and not rows, and the
/// server's own clock decides whether a poll is still open — a page left open all afternoon must
/// not get the last word.</para>
///
/// <para>Nothing here goes through the feed. A poll belongs to a message, and this controller is
/// the half that knows only the poll — which is exactly what makes it reusable by a case comment
/// and a group's own message as well.</para>
/// </remarks>
public sealed class MessagePollControllerTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new BenDataContext(opts));
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static MessagePollController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
        => new(factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = userId == Guid.Empty
                        ? new ClaimsPrincipal(new ClaimsIdentity())
                        : new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
                },
            },
        };

    /// <summary>A poll on a message, with its answers. Returns the poll and its option ids in order.</summary>
    private static async Task<(Guid PollId, Guid[] OptionIds)> SeedPollAsync(
        IDbContextFactory<BenDataContext> factory, bool allowMultiple = false, DateTime? closesAtUtc = null)
    {
        await using var db = factory.CreateDbContext();

        var authorId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        db.OrgMessages.Add(new OrgMessage
        {
            Id = messageId,
            AuthorAppUserId = authorId,
            ChannelType = Ben.Data.Common.Enums.OrgMessageChannel.PublicFeed,
            Body = "Which night?",
            IsPublic = true,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = authorId,
        });

        var pollId = Guid.NewGuid();
        db.MessagePolls.Add(new MessagePoll
        {
            Id = pollId,
            OrgMessageId = messageId,
            Question = "Which night?",
            AllowMultiple = allowMultiple,
            ClosesAtUtc = closesAtUtc,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = authorId,
        });

        var optionIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var texts = new[] { "Friday", "Saturday", "Sunday" };
        for (var i = 0; i < optionIds.Length; i++)
        {
            db.MessagePollOptions.Add(new MessagePollOption
            {
                Id = optionIds[i], MessagePollId = pollId, Text = texts[i], SortOrder = i,
            });
        }

        await db.SaveChangesAsync();
        return (pollId, optionIds);
    }

    private static async Task<MessagePollRecord> VoteAsync(
        MessagePollController controller, Guid pollId, params Guid[] optionIds)
    {
        var result = await controller.Vote(pollId, new CastPollVoteRequest(optionIds), default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return (MessagePollRecord)ok.Value!;
    }

    [Fact]
    public async Task A_vote_is_counted_and_comes_back_as_this_readers_own()
    {
        var factory = CreateFactory();
        var (pollId, options) = await SeedPollAsync(factory);
        var sarah = Guid.NewGuid();

        var poll = await VoteAsync(Build(factory, sarah), pollId, options[0]);

        Assert.Equal(1, poll.TotalVotes);
        Assert.Equal(1, poll.Options[0].Votes);
        Assert.Equal([options[0]], poll.MyOptionIds);
    }

    [Fact]
    public async Task Changing_your_mind_replaces_your_vote_rather_than_adding_one()
    {
        var factory = CreateFactory();
        var (pollId, options) = await SeedPollAsync(factory);
        var controller = Build(factory, Guid.NewGuid());

        await VoteAsync(controller, pollId, options[0]);
        var poll = await VoteAsync(controller, pollId, options[1]);

        Assert.Equal(1, poll.TotalVotes);
        Assert.Equal(0, poll.Options[0].Votes);
        Assert.Equal(1, poll.Options[1].Votes);
    }

    [Fact]
    public async Task An_empty_list_takes_the_vote_back()
    {
        var factory = CreateFactory();
        var (pollId, options) = await SeedPollAsync(factory);
        var controller = Build(factory, Guid.NewGuid());

        await VoteAsync(controller, pollId, options[0]);
        var poll = await VoteAsync(controller, pollId);

        Assert.Equal(0, poll.TotalVotes);
        Assert.Empty(poll.MyOptionIds);
    }

    [Fact]
    public async Task The_total_counts_people_not_rows()
    {
        // The whole reason it is DISTINCT: on a poll that takes several answers, "60% of 14 votes"
        // counted over rows is a percentage of a number nobody recognises.
        var factory = CreateFactory();
        var (pollId, options) = await SeedPollAsync(factory, allowMultiple: true);

        await VoteAsync(Build(factory, Guid.NewGuid()), pollId, options[0], options[1]);
        await VoteAsync(Build(factory, Guid.NewGuid()), pollId, options[1]);

        var poll = await VoteAsync(Build(factory, Guid.NewGuid()), pollId, options[2]);

        Assert.Equal(3, poll.TotalVotes);
        Assert.Equal(4, poll.Options.Sum(o => o.Votes));
    }

    [Fact]
    public async Task A_single_answer_poll_refuses_two_answers_in_words()
    {
        var factory = CreateFactory();
        var (pollId, options) = await SeedPollAsync(factory);

        var result = await Build(factory, Guid.NewGuid())
            .Vote(pollId, new CastPollVoteRequest([options[0], options[1]]), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("This poll takes one answer.", bad.Value);
    }

    [Fact]
    public async Task An_answer_from_another_poll_is_refused()
    {
        var factory = CreateFactory();
        var (pollId, _) = await SeedPollAsync(factory);
        var (_, elsewhere) = await SeedPollAsync(factory);

        var result = await Build(factory, Guid.NewGuid())
            .Vote(pollId, new CastPollVoteRequest([elsewhere[0]]), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("That isn't one of the answers.", bad.Value);
    }

    [Fact]
    public async Task A_closed_poll_refuses_a_vote_from_a_page_that_was_left_open()
    {
        var factory = CreateFactory();
        var (pollId, options) = await SeedPollAsync(factory, closesAtUtc: DateTime.UtcNow.AddMinutes(-1));

        var result = await Build(factory, Guid.NewGuid())
            .Vote(pollId, new CastPollVoteRequest([options[0]]), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("That poll has closed.", bad.Value);
    }

    [Fact]
    public async Task A_visitor_reads_the_counts_and_is_shown_no_vote_of_their_own()
    {
        var factory = CreateFactory();
        var (pollId, options) = await SeedPollAsync(factory);
        await VoteAsync(Build(factory, Guid.NewGuid()), pollId, options[0]);

        var result = await Build(factory, Guid.Empty).Get(pollId, default);
        var poll = (MessagePollRecord)Assert.IsType<OkObjectResult>(result.Result).Value!;

        Assert.Equal(1, poll.TotalVotes);
        Assert.Empty(poll.MyOptionIds);
        Assert.False(poll.IsClosed);
    }

    [Fact]
    public async Task A_poll_that_is_not_there_is_a_404_rather_than_an_empty_poll()
    {
        var factory = CreateFactory();
        Assert.IsType<NotFoundResult>((await Build(factory, Guid.Empty).Get(Guid.NewGuid(), default)).Result);
    }
}
