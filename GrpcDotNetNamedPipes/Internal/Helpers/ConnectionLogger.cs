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

using Microsoft.Extensions.Logging;

namespace GrpcDotNetNamedPipes.Internal.Helpers
{
    internal abstract class ConnectionLogger<T> 
        where T : class
    {
        private static int _lastId;
        public int ConnectionId { get; set; }

        public static int NextId() => Interlocked.Increment(ref _lastId);
        protected readonly ILogger<T> _logger;
        protected ConnectionLogger(ILogger<T> logger,  int id)
        {
            _logger = logger;
            ConnectionId = id;
        }
        public static ServerLogger<T> Server() 
            => new ServerLogger<T>(NamedPipeServer.LoggerFactory?.CreateLogger<T>() ?? null, 0);
        public static ClientLogger<T> Client() 
            => new ClientLogger<T>(
                NamedPipeChannel.LoggerFactory?.CreateLogger<T>() ?? null, 
                NamedPipeChannel.LoggerFactory != null ? NextId() : 0);

        public static ConnectionLogger<T> Logger(TransportSide transportSide) => 
            transportSide == TransportSide.Server ? 
            Server() : 
            Client();

         public abstract void Trace(string message);
         public abstract void Error(string message);
    }

    internal class ServerLogger<T>  : ConnectionLogger<T> where T : class
    {
        public ServerLogger(ILogger<T> logger,  int id) : base(logger, id)
        {
        }

        public override void Trace(string message)
            => _logger?.LogTrace(string.Format("[SERVER][{0}] {1}", 
                ConnectionId > 0 ? ConnectionId.ToString() : "?",
                message));

        public override void Error(string message)
            => _logger?.LogError(string.Format("[SERVER][{0}] {1}", 
                ConnectionId > 0 ? ConnectionId.ToString() : "?",
                message));
    }

    internal class ClientLogger<T> : ConnectionLogger<T> where T : class
    {
        public ClientLogger(ILogger<T> logger,  int id) : base(logger, id)
        {
        }

        public override void Trace(string message)
            => _logger?.LogTrace(string.Format("[CLIENT][{0}] {1}", 
                ConnectionId > 0 ? ConnectionId.ToString() : "?",
                message));

        public override void Error(string message)
            => _logger?.LogError(string.Format("[CLIENT][{0}] {1}", 
                ConnectionId > 0 ? ConnectionId.ToString() : "?",
                message));
    }
}