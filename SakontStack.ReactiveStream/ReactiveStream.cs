using System;
using System.IO;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Threading;
using static SakontStack.ReactiveStream.ReactiveStream;

namespace SakontStack.ReactiveStream;



public class ReactiveStream : Stream, IObservable<StreamProgress>
{
    private readonly Stream                      _stream;
    private readonly IProgress<StreamProgress>   _progress;
    private readonly IObservable<StreamProgress> _observable;
    private readonly StreamOptions               _options = new();
    private event EventHandler<StreamProgress>?  OnProgress;

    public class StreamOptions
    {
        public TimeSpan ReportInterval { get; set; } = TimeSpan.Zero;
    }

    public enum ProgressType
    {
        Read,
        Write,
    }
    public class StreamProgress
    {
        public ProgressType Type       { get; init; }
        public long?        TotalBytes { get; init; }
        public long         Bytes      { get; init; }
        public long         Delta      { get; init; }

        public double Percentage => TotalBytes is null or 0
                                        ? double.NegativeInfinity
                                        : Bytes * 100 / (double)TotalBytes;
    }


    public ReactiveStream(Stream stream, IProgress<StreamProgress> progress = null, Action<StreamOptions> configureStream = null)
    {
        _stream   = stream;
        _progress = progress;
        configureStream?.Invoke(_options);

        _observable = Observable
           .FromEvent<EventHandler<StreamProgress>, StreamProgress>(h => OnProgress += h,
                                                                    h => OnProgress -= h);
    }


    public override int Read(byte[] buffer, int offset, int count)
    {
        int totalBytesRead = 0;
        while (totalBytesRead < count)
        {
            int bytesRead = _stream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
            if (bytesRead == 0) break;
            totalBytesRead += bytesRead;
        }
        OnProgress?.Invoke(this, new StreamProgress()
                                 {
                                     TotalBytes = this.Length,
                                     Delta      = totalBytesRead,
                                     Bytes      = offset + totalBytesRead,
                                     Type       = ProgressType.Read
                                 });
        return totalBytesRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var result = await base.ReadAsync(buffer, offset, count, cancellationToken);
        if (!cancellationToken.IsCancellationRequested) OnProgress?.Invoke(this, new StreamProgress()
                                                                               {
                                                                                   TotalBytes = this.Length,
                                                                                   Delta      = result,
                                                                                   Bytes      = offset + result,
                                                                                   Type       = ProgressType.Read
                                                                               });
        return result;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
    {
        var position = Position;
        var result   = await base.ReadAsync(buffer, cancellationToken);
        if (!cancellationToken.IsCancellationRequested) OnProgress?.Invoke(this, new StreamProgress()
                                                                               {
                                                                                   TotalBytes = this.Length,
                                                                                   Delta      = result,
                                                                                   Bytes      = position + result,
                                                                                   Type       = ProgressType.Read
                                                                               });
        return result;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _stream.Write(buffer, offset, count);
        OnProgress?.Invoke(this, new StreamProgress()
                                 {
                                     TotalBytes = this.Length,
                                     Delta      = count,
                                     Bytes      = offset + count,
                                     Type       = ProgressType.Write
                                 });
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await base.WriteAsync(buffer, offset, count, cancellationToken);
        if (!cancellationToken.IsCancellationRequested) OnProgress?.Invoke(this, new StreamProgress()
                                                                               {
                                                                                   TotalBytes = this.Length,
                                                                                   Delta      = count,
                                                                                   Bytes      = offset + count,
                                                                                   Type       = ProgressType.Read
                                                                               });
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
    {
        var position = Position;
        await base.WriteAsync(buffer, cancellationToken);
        if (!cancellationToken.IsCancellationRequested) OnProgress?.Invoke(this, new StreamProgress()
                                                                               {
                                                                                   TotalBytes = this.Length,
                                                                                   Delta = buffer.Length,
                                                                                   Bytes = position + buffer.Length,
                                                                                   Type = ProgressType.Read
                                                                               });
    }

    public override void Flush()                                   => _stream.Flush();
    public override long Seek(long      offset, SeekOrigin origin) => _stream.Seek(offset, origin);
    public override void SetLength(long value) => _stream.SetLength(value);



    public override bool CanRead  => _stream.CanRead;
    public override bool CanSeek  => _stream.CanSeek;
    public override bool CanWrite => _stream.CanWrite;
    public override long Length   => _stream.Length;
    public override long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    private IObservable<StreamProgress> ApplyOptions(IObservable<StreamProgress> observable)
    {
        return _options?.ReportInterval > TimeSpan.Zero ? _observable.Sample(_options.ReportInterval) : observable;
    }
    public IDisposable Subscribe(IObserver<StreamProgress> observer)
    {
        return ApplyOptions(_observable)
              .Do(x => _progress?.Report(x))
              .Subscribe(observer);
    }

}