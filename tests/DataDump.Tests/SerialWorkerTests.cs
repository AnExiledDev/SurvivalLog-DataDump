using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class SerialWorkerTests
{
    /// <summary>A worker whose drain runs only when the test says, so what is queued can be seen before it runs.</summary>
    private sealed class HeldWorker
    {
        private readonly List<Action> _drains = new();

        public HeldWorker() => Worker = new SerialWorker(Failures.Add, _drains.Add);

        public SerialWorker Worker { get; }

        public List<Exception> Failures { get; } = new();

        public int DrainsStarted => _drains.Count;

        public void RunDrains()
        {
            var drains = _drains.ToList();

            _drains.Clear();

            foreach (var drain in drains) drain();
        }
    }

    // A file's lines and a run's outcome records must land in the order they were posted.
    [Fact]
    public void Jobs_run_in_the_order_they_were_posted()
    {
        var held = new HeldWorker();
        var ran = new List<int>();

        for (var i = 0; i < 5; i++)
        {
            var job = i;

            held.Worker.Post(() => ran.Add(job));
        }

        held.RunDrains();

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, ran);
    }

    // A locked file costs only its own write; the files queued behind it are still written.
    [Fact]
    public void A_job_that_throws_is_reported_and_the_next_still_runs()
    {
        var held = new HeldWorker();
        var ran = new List<string>();

        held.Worker.Post(() => throw new IOException("locked"));
        held.Worker.Post(() => ran.Add("after"));
        held.RunDrains();

        Assert.Equal(new[] { "after" }, ran);
        Assert.IsType<IOException>(Assert.Single(held.Failures));
    }

    // One drain at a time keeps the order: a job posted while a drain is going joins it instead of starting another.
    [Fact]
    public void Jobs_posted_while_a_drain_is_pending_join_it()
    {
        var held = new HeldWorker();

        held.Worker.Post(() => { });
        held.Worker.Post(() => { });

        Assert.Equal(1, held.DrainsStarted);
    }

    // Quitting waits for the files still being written, and must know whether they made it.
    [Fact]
    public void Finish_says_whether_every_job_is_done()
    {
        var held = new HeldWorker();

        held.Worker.Post(() => { });

        Assert.False(held.Worker.IsIdle);
        Assert.False(held.Worker.Finish(TimeSpan.Zero));

        held.RunDrains();

        Assert.True(held.Worker.IsIdle);
        Assert.True(held.Worker.Finish(TimeSpan.Zero));
    }

    [Fact]
    public void After_a_drain_ends_the_next_post_starts_a_new_one()
    {
        var held = new HeldWorker();
        var ran = 0;

        held.Worker.Post(() => ran++);
        held.RunDrains();
        held.Worker.Post(() => ran++);
        held.RunDrains();

        Assert.Equal(2, ran);
    }

    [Fact]
    public void The_default_worker_runs_jobs_off_the_posting_thread()
    {
        var worker = new SerialWorker(_ => { });
        var postingThread = Environment.CurrentManagedThreadId;
        var jobThread = postingThread;

        worker.Post(() => jobThread = Environment.CurrentManagedThreadId);

        Assert.True(worker.Finish(TimeSpan.FromSeconds(10)));
        Assert.NotEqual(postingThread, jobThread);
    }
}
