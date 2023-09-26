/*
 * Copyright 2020 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

namespace GrpcDotNetNamedPipes.Internal;

internal class ServerConnectionContext : TransportMessageHandler, IDisposable
{
    private readonly ServerLogger<ServerConnectionContext> _logger;
    private readonly Dictionary<string, Func<ServerConnectionContext, Task>> _methodHandlers;
    private readonly PayloadQueue _payloadQueue;

    public ServerConnectionContext(NamedPipeServerStream pipeStream, 
        Dictionary<string, Func<ServerConnectionContext, Task>> methodHandlers)
    {
        CallContext = new NamedPipeCallContext(this);
        PipeStream = pipeStream;
        Transport = new NamedPipeTransport(pipeStream, TransportSide.Server);
        _logger = ConnectionLogger<ServerConnectionContext>.Server();
        _methodHandlers = methodHandlers;
        _payloadQueue = new PayloadQueue();
        CancellationTokenSource = new CancellationTokenSource();
    }

    public NamedPipeServerStream PipeStream { get; }

    public NamedPipeTransport Transport { get; }

    public CancellationTokenSource CancellationTokenSource { get; }

    public Deadline Deadline { get; private set; }

    public Metadata RequestHeaders { get; private set; }

    public ServerCallContext CallContext { get; }

    public bool IsCompleted { get; private set; }

    public MessageReader<TRequest> GetMessageReader<TRequest>(Marshaller<TRequest> requestMarshaller)
    {
        return new MessageReader<TRequest>(_payloadQueue, requestMarshaller, CancellationToken.None, Deadline);
    }

    public IServerStreamWriter<TResponse> CreateResponseStream<TResponse>(Marshaller<TResponse> responseMarshaller)
    {
        return new ResponseStreamWriterImpl<TResponse>(Transport, CancellationToken.None, responseMarshaller,
            () => IsCompleted);
    }

    public override void HandleRequestInit(string methodFullName, DateTime? deadline)
    {
        Deadline = new Deadline(deadline);
        Task.Run(async () => await _methodHandlers[methodFullName](this).ConfigureAwait(false));
    }

    public override void HandleHeaders(Metadata headers) => RequestHeaders = headers;

    public override void HandleCancel() => CancellationTokenSource.Cancel();

    public override void HandleStreamEnd() => _payloadQueue.SetCompleted();

    public override void HandlePayload(byte[] payload) => _payloadQueue.AppendPayload(payload);

    public void Error(Exception ex)
    {
        IsCompleted = true;
        if (Deadline != null && Deadline.IsExpired)
        {
            _logger.Error("RPC Warning: Deadline Exceeded");
            WriteTrailers(StatusCode.DeadlineExceeded, "");
        }
        else if (CancellationTokenSource.IsCancellationRequested)
        {
            
            _logger.Error("RPC Warning: Cancelled");
            WriteTrailers(StatusCode.Cancelled, "");
        }
        else if (ex is RpcException rpcException)
        {
            
            _logger.Error($"RPC Exception: {rpcException.Message} at {rpcException.StackTrace}");
            if (rpcException.InnerException != null)
                _logger.Error($"Inner exception: {rpcException.InnerException.Message} at {rpcException.InnerException.StackTrace}");
            WriteTrailers(rpcException.StatusCode, rpcException.Status.Detail);
        }
        else
        {
            WriteTrailers(StatusCode.Unknown, "Exception was thrown by handler.");
        }
    }

    public void Success(byte[] responsePayload = null)
    {
        _logger.Trace("RPC successful");
        IsCompleted = true;
        if (CallContext.Status.StatusCode != StatusCode.OK)
        {
            WriteTrailers(CallContext.Status.StatusCode, CallContext.Status.Detail);
        }
        else if (responsePayload != null)
        {
            Transport.Write()
                .Payload(responsePayload)
                .Trailers(StatusCode.OK, "", CallContext.ResponseTrailers)
                .Commit();
        }
        else
        {
            WriteTrailers(StatusCode.OK, "");
        }
    }

    private void WriteTrailers(StatusCode statusCode, string statusDetail)
    {
        Transport.Write().Trailers(statusCode, statusDetail, CallContext.ResponseTrailers).Commit();
    }

    public void Dispose()
    {
        _logger.Trace("Disposing server context");
        if (!IsCompleted)
        {
            CancellationTokenSource.Cancel();
        }
        _payloadQueue.Dispose();
        PipeStream?.Dispose();
    }
}