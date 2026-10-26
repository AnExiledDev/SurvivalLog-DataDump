using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DataDump.Core;

/// <summary>
/// Runs posted jobs one at a time, in the order they were posted, off the caller's thread, so file writes keep their
/// order and the game's frame never waits on the disk. A job that throws is handed to the failure callback (on the
/// worker's thread) and the next job still runs. Jobs must not touch the game or Unity.
/// </summary>
public sealed class SerialWorker
{
    private readonly Queue<Action> _jobs = new();
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _idle = new(initialState: true);
    private readonly Action<Exception> _onFailure;
    private readonly Action<Action> _start;
    private bool _isDraining;

    /// <param name="onFailure">Called on the worker's thread with what a job threw; must not throw itself.</param>
    /// <param name="start">Starts the drain off the caller's thread; the thread pool unless a test gives its own.</param>
    public SerialWorker(Action<Exception> onFailure, Action<Action>? start = null)
    {
        _onFailure = onFailure;
        _start = start ?? (drain => Task.Run(drain));
    }

    /// <summary>Whether no job is waiting or running.</summary>
    public bool IsIdle => _idle.IsSet;

    public void Post(Action job)
    {
        lock (_gate)
        {
            _jobs.Enqueue(job);

            if (_isDraining) return;

            _isDraining = true;
            _idle.Reset();
        }

        _start(Drain);
    }

    /// <summary>Waits for every posted job to finish, at most <paramref name="timeout"/>; true when they all did.</summary>
    public bool Finish(TimeSpan timeout) => _idle.Wait(timeout);

    private void Drain()
    {
        while (true)
        {
            Action job;

            lock (_gate)
            {
                if (_jobs.Count == 0)
                {
                    _isDraining = false;
                    _idle.Set();

                    return;
                }

                job = _jobs.Dequeue();
            }

            Run(job);
        }
    }

    private void Run(Action job)
    {
        try
        {
            job();
        }
        catch (Exception ex)
        {
            ReportQuietly(ex);
        }
    }

    private void ReportQuietly(Exception ex)
    {
        try
        {
            _onFailure(ex);
        }
        catch (Exception)
        {
            // A failing report must not stop the jobs queued behind it.
        }
    }
}
