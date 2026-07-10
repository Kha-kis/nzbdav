using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;

namespace backend.Tests.Database;

/// <summary>
/// Guards the delete path behind the SABnzbd `mode=history&amp;name=delete` endpoint.
///
/// The deleteFiles=false branch used to attach stub HistoryItem entities for every requested id
/// without checking existence. EF then emitted a DELETE affecting 0 rows for any id already gone,
/// threw DbUpdateConcurrencyException, and rolled back the WHOLE SaveChangesAsync -- so a batch
/// containing one stale id silently deleted none of them while the endpoint returned status:true.
/// </summary>
public class RemoveHistoryItemsTests
{
    private static HistoryItem NewHistoryItem(Guid id) => new()
    {
        Id = id,
        CreatedAt = DateTime.UtcNow,
        FileName = $"file-{id:N}.mkv",
        JobName = $"job-{id:N}",
        Category = "tv",
        DownloadStatus = HistoryItem.DownloadStatusOption.Completed,
        TotalSegmentBytes = 1024,
        DownloadTimeSeconds = 1,
    };

    private static async Task<Guid> SeedAsync(DavDatabaseContext ctx)
    {
        var id = Guid.NewGuid();
        ctx.HistoryItems.Add(NewHistoryItem(id));
        await ctx.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Delete_of_an_unknown_id_is_a_no_op_and_does_not_throw()
    {
        await using var ctx = TestEnvironment.NewContext();
        var client = new DavDatabaseClient(ctx);

        await client.RemoveHistoryItemsAsync([Guid.NewGuid()], deleteFiles: false);
        var exception = await Record.ExceptionAsync(() => ctx.SaveChangesAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Delete_of_an_existing_id_removes_the_row_and_queues_cleanup()
    {
        await using var ctx = TestEnvironment.NewContext();
        var client = new DavDatabaseClient(ctx);
        var id = await SeedAsync(ctx);

        await client.RemoveHistoryItemsAsync([id], deleteFiles: false);
        await ctx.SaveChangesAsync();

        Assert.False(await ctx.HistoryItems.AnyAsync(x => x.Id == id));
        Assert.True(await ctx.HistoryCleanupItems.AnyAsync(x => x.Id == id));
    }

    /// <summary>The regression. A stale id in the batch must not prevent the live ones from deleting.</summary>
    [Fact]
    public async Task Batch_containing_a_stale_id_still_deletes_the_existing_rows()
    {
        await using var ctx = TestEnvironment.NewContext();
        var client = new DavDatabaseClient(ctx);
        var live = await SeedAsync(ctx);
        var stale = Guid.NewGuid();

        await client.RemoveHistoryItemsAsync([live, stale], deleteFiles: false);
        var exception = await Record.ExceptionAsync(() => ctx.SaveChangesAsync());

        Assert.Null(exception);
        Assert.False(await ctx.HistoryItems.AnyAsync(x => x.Id == live));
        Assert.True(await ctx.HistoryCleanupItems.AnyAsync(x => x.Id == live));
        Assert.False(await ctx.HistoryCleanupItems.AnyAsync(x => x.Id == stale));
    }
}
