# Reactive Stream

a `Stream` wrapper that provides Read/Write progress reporting through `IProgress<StreamProgress>` and `IObservable<StreamProgress>`
with speed throttling for Read/Write streams separately.

| Version                                                                                                                                        | Downloads                                                                    |
| ---------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| [![Latest version](https://img.shields.io/nuget/v/SakontStack.ReactiveStream.svg)](https://www.nuget.org/packages/SakontStack.ReactiveStream/) | ![Downloads](https://img.shields.io/nuget/dt/SakontStack.ReactiveStream.svg) |

## Example Usage:

### Report file download progress:

- progress using `IProgress<StreamProgress>`

```csharp
using System.Reactive.Linq;
using SakontStack.ReactiveStream;
using ByteSizeLib;

var fileLink = new Uri(@"https://releases.ubuntu.com/jammy/ubuntu-22.04.3-desktop-amd64.iso");

var client   = new HttpClient();
var response = await client.GetAsync(fileLink, HttpCompletionOption.ResponseHeadersRead);
var length   = long.Parse(response.Content.Headers
                                  .First(h => h.Key.Equals("Content-Length"))
                                  .Value
                                  .First());

var progress = new Progress<ReactiveStream.StreamProgress>(p => Console
                                                              .WriteLine($"Downloaded {ByteSize.FromBytes(p.Bytes).ToBinaryString()}" +
                                                                         $"/{ByteSize.FromBytes(p.TotalBytes ?? 0).ToBinaryString()} " +
                                                                         $"({p.Percentage:N2}%) " +
                                                                         $"{ByteSize.FromBytes(p.BytesPerSecond).ToBinaryString()}/sec"));

var stream = new ReactiveStream(new MemoryStream(), progress: progress,  totalLength: length);
// stream progress will only start reporting progress after you subscribe to it
stream.Subscribe();
await response.Content.CopyToAsync(stream);
```

_**NOTE:**_ _setting custom report interval while using `IProgress<StreamProgress>` alone, requires using `stream.Sample()` operator._

- progress using `IObservable<StreamProgress>`

```csharp
using System.Reactive.Linq;
using SakontStack.ReactiveStream;
using ByteSizeLib;

var fileLink = new Uri(@"https://releases.ubuntu.com/jammy/ubuntu-22.04.3-desktop-amd64.iso");

var client   = new HttpClient();
var response = await client.GetAsync(fileLink, HttpCompletionOption.ResponseHeadersRead);
var length   = long.Parse(response.Content.Headers
                                  .First(h => h.Key.Equals("Content-Length"))
                                  .Value
                                  .First());

var stream = new ReactiveStream(new MemoryStream(), totalLength: length);
stream
  .Sample(TimeSpan.FromSeconds(0.25)) // Only report progress each 0.25 seconds
  .Do(p => Console
            .WriteLine($"Downloaded {ByteSize.FromBytes(p.Bytes).ToBinaryString()}"+
                       $"/{ByteSize.FromBytes(p.TotalBytes ?? 0).ToBinaryString()}"+
                       $"({p.Percentage:N2}%) "+
                       $"{ByteSize.FromBytes(p.BytesPerSecond).ToBinaryString()}/sec"))
  .Subscribe();
await response.Content.CopyToAsync(stream);
```

- setting download speed limits:

```csharp
using System.Reactive.Linq;
using SakontStack.ReactiveStream;
using ByteSizeLib;

var fileLink = new Uri(@"https://releases.ubuntu.com/jammy/ubuntu-22.04.3-desktop-amd64.iso");

var client   = new HttpClient();
var response = await client.GetAsync(fileLink, HttpCompletionOption.ResponseHeadersRead);
var length   = long.Parse(response.Content.Headers
                                  .First(h => h.Key.Equals("Content-Length"))
                                  .Value
                                  .First());

var stream = new ReactiveStream(new MemoryStream(), totalLength: length,
                                configureStream: s =>
                                                 {
                                                     // 1 MB/sec speed limit for write streams;
                                                     s.WriteSpeedLimit = 1024 * 1024;
                                                 });
stream
  .Sample(TimeSpan.FromSeconds(0.25)) // Only report progress each 0.25 seconds
  .Do(p => Console
            .WriteLine($"Downloaded {ByteSize.FromBytes(p.Bytes).ToBinaryString()}"+
                       $"/{ByteSize.FromBytes(p.TotalBytes ?? 0).ToBinaryString()}"+
                       $"({p.Percentage:N2}%) "+
                       $"{ByteSize.FromBytes(p.BytesPerSecond).ToBinaryString()}/sec"))
  .Subscribe();
await response.Content.CopyToAsync(stream);
```

- changing speed limits in realtime:

```csharp
stream.ModifyOptions(x => x.WriteSpeedLimit = null);
```

## API:

the `ReactiveStream` class implements both `Stream` & `IObservable<ReactiveStream.StreamProgress>`

```csharp
public class ReactiveStream : Stream, IObservable<StreamProgress>
{
    //* ... *//
}
```
