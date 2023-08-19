using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using System.Threading;
using static SakontStack.ReactiveStream.ReactiveStream;

namespace SakontStack.ReactiveStream;

public class ReactiveStream : Stream, IObservable<StreamProgress>
{
    private readonly Stream _stream;
    private readonly IProgress<StreamProgress>? _progress;
    private readonly long? _totalLength;
    private readonly IObservable<StreamProgress> _observable;
    private readonly StreamOptions _options = new();
    private event EventHandler<StreamProgress>? OnProgress;
    private ConcurrentBag<TimestampedDelta> _deltas = new();

    private SemaphoreSlim _readLock  = new(1, 1);
    private SemaphoreSlim _writeLock = new(1, 1);

    public class StreamOptions
    {
        public long? WriteSpeedLimit { get; set; } = null;
        public long? ReadSpeedLimit { get; set; } = null;
    }

    public enum ProgressType
    {
        Read,
        Write
    }

    public record struct TimestampedDelta(long Delta, ProgressType Type, DateTimeOffset Timestamp);

    public class StreamProgress
    {
        public ProgressType   Type           { get; init; }
        public long?          TotalBytes     { get; init; }
        public long           Bytes          { get; init; }
        public long           Delta          { get; init; }
        public long           BytesPerSecond { get; init; }
        public long?          SpeedLimit     { get; init; }
        public DateTimeOffset Timestamp      { get; private set; } = DateTimeOffset.Now;

        public double Percentage => TotalBytes is null or 0
                                        ? double.NegativeInfinity
                                        : Bytes * 100 / (double)TotalBytes;
    }

    public void ModifyOptions(Action<StreamOptions> configureOptions) => configureOptions(_options);


    public ReactiveStream(Stream stream, IProgress<StreamProgress>? progress = null,
                          Action<StreamOptions>? configureStream = null,
                          long? totalLength = 0)
    {
        _stream = stream;
        _progress = progress;
        _totalLength = totalLength;
        configureStream?.Invoke(_options);


        _observable = Observable
                     .FromEventPattern<EventHandler<StreamProgress>, StreamProgress>(h => OnProgress += h,
                          h => OnProgress -= h)
                     .Select(x => x.EventArgs);
    }

    private TimeSpan ReachedLimit(ProgressType type)
    {
        switch (type)
        {
            case ProgressType.Read when _options.ReadSpeedLimit is null:
            case ProgressType.Write when _options.WriteSpeedLimit is null:
                return TimeSpan.Zero;
            default:
                {
                    var now = DateTimeOffset.UtcNow;
                    var lastSecond = _deltas
                                    .Where(x => now - x.Timestamp <= TimeSpan.FromSeconds(1)
                                                && x.Type == type).ToList();

                    var deltas = lastSecond.Sum(x => x.Delta);
                    var diff = now - (lastSecond.Count > 0
                                          ? lastSecond.MinBy(x => x.Timestamp).Timestamp
                                          : now);
                    var ct = new CancellationTokenSource(diff);

                    var limit = type == ProgressType.Read ? _options.ReadSpeedLimit : _options.WriteSpeedLimit;
                    return limit <= deltas ? diff : TimeSpan.Zero;
                }
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var limit = ReachedLimit(ProgressType.Read);
        if (limit > TimeSpan.Zero) _readLock.Wait(limit);

        var totalBytesRead = 0;
        while (totalBytesRead < count)
        {
            var bytesRead = _stream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
            if (bytesRead == 0) break;
            totalBytesRead += bytesRead;
        }

        var now = DateTimeOffset.Now;
        OnProgress?.Invoke(this, new StreamProgress()
        {
            TotalBytes = Length,
            Delta = totalBytesRead,
            Bytes = Position,
            Type = ProgressType.Read,
            BytesPerSecond = _deltas
                                                     .Where(x => now - x.Timestamp <= TimeSpan.FromSeconds(1)
                                                                 && x.Type == ProgressType.Read)
                                                     .Sum(x => x.Delta),
            SpeedLimit = _options.ReadSpeedLimit
        });
        return totalBytesRead;
    }


    public override void Write(byte[] buffer, int offset, int count)
    {
        var limit = ReachedLimit(ProgressType.Write);
        if (limit > TimeSpan.Zero) _writeLock.Wait(limit);

        _stream.Write(buffer, offset, count);

        var now = DateTimeOffset.Now;
        OnProgress?.Invoke(this, new StreamProgress()
        {
            TotalBytes = Length,
            Delta = count,
            Bytes = Position,
            Type = ProgressType.Write,
            BytesPerSecond = _deltas
                                                     .Where(x => now - x.Timestamp <= TimeSpan.FromSeconds(1)
                                                                 && x.Type == ProgressType.Write)
                                                     .Sum(x => x.Delta),
            SpeedLimit = _options.WriteSpeedLimit
        });
    }

    public override void Flush()
    {
        _stream.Flush();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _stream.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        _stream.SetLength(value);
    }


    public override bool CanRead => _stream.CanRead;
    public override bool CanSeek => _stream.CanSeek;
    public override bool CanWrite => _stream.CanWrite;
    public override long Length => _totalLength ?? _stream.Length;

    public override long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    public IDisposable Subscribe(IObserver<StreamProgress> observer)
    {
        return _observable.Do(p => _deltas.Add(new TimestampedDelta(p.Delta, p.Type, DateTimeOffset.Now)))
                          .Subscribe(onNext: x =>
                          {
                              _progress?.Report(x);
                              observer.OnNext(x);
                          },
                                     onError: observer.OnError,
                                     onCompleted: observer.OnCompleted);
    }


}
