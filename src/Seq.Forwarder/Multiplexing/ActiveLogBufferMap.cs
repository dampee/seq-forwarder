﻿// Copyright 2017 Datalust Pty Ltd and Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using Seq.Forwarder.Config;
using Seq.Forwarder.Storage;
using Seq.Forwarder.Util;
using Serilog;

namespace Seq.Forwarder.Multiplexing
{
    class ActiveLogBufferMap : IDisposable
    {
        const string DataFileName = "data.mdb", LockFileName = "lock.mdb", ApiKeyFileName = ".apikey";

        readonly ulong _bufferSizeBytes;
        readonly SeqForwarderOutputConfig _outputConfig;
        readonly ILogShipperFactory _shipperFactory;
        readonly string _bufferPath;
        readonly ILogger _log = Log.ForContext<ActiveLogBufferMap>();

        readonly object _sync = new object();
        ActiveLogBuffer _noApiKeyLogBuffer;
        readonly Dictionary<string, ActiveLogBuffer> _buffersByApiKey = new Dictionary<string, ActiveLogBuffer>();

        public ActiveLogBufferMap(string bufferPath, SeqForwarderStorageConfig storageConfig, SeqForwarderOutputConfig outputConfig, ILogShipperFactory logShipperFactory)
        {
            _bufferSizeBytes = storageConfig.BufferSizeBytes;
            _outputConfig = outputConfig ?? throw new ArgumentNullException(nameof(outputConfig));
            _shipperFactory = logShipperFactory ?? throw new ArgumentNullException(nameof(logShipperFactory));
            _bufferPath = bufferPath ?? throw new ArgumentNullException(nameof(bufferPath));
        }

        // The odd three-stage initialization improves our chances of correctly tearing down the `LightningEnvironment`s within
        // `LogBuffer`s in the event of a failure during start-up. See: https://github.com/CoreyKaylor/Lightning.NET/blob/master/src/LightningDB/LightningEnvironment.cs#L252
        public void Load()
        {
            // At startup, we look for buffers and either delete them if they're empty, or load them
            // up if they're not. This garbage collection at start-up is a simplification,
            // we might try cleaning up in the background if the gains are worthwhile, although more synchronization
            // would be required.

            lock (_sync)
            {
                Directory.CreateDirectory(_bufferPath);

                var defaultDataFilePath = Path.Combine(_bufferPath, DataFileName);
                if (File.Exists(defaultDataFilePath))
                {
                    _log.Information("Loading the default log buffer in {Path}", _bufferPath);
                    var buffer = new LogBuffer(_bufferPath, _bufferSizeBytes);
                    if (buffer.Peek(0).Length == 0)
                    {
                        _log.Information("The default buffer is empty and will be removed until more data is received");
                        buffer.Dispose();
                        File.Delete(defaultDataFilePath);
                        var lockFilePath = Path.Combine(_bufferPath, LockFileName);
                        if (File.Exists(lockFilePath))
                            File.Delete(lockFilePath);
                    }
                    else
                    {
                        _noApiKeyLogBuffer = new ActiveLogBuffer(buffer, _shipperFactory.Create(buffer, _outputConfig.ApiKey));
                    }
                }

                foreach (var subfolder in Directory.GetDirectories(_bufferPath))
                {
                    var encodedApiKeyFilePath = Path.Combine(subfolder, ApiKeyFileName);
                    if (!File.Exists(encodedApiKeyFilePath))
                    {
                        _log.Information("Folder {Path} does not appear to be a log buffer; skipping", subfolder);
                        continue;
                    }

                    _log.Information("Loading an API-key specific buffer in {Path}", subfolder);
                    var apiKey = MachineScopeDataProtection.Unprotect(File.ReadAllText(encodedApiKeyFilePath));

                    var buffer = new LogBuffer(subfolder, _bufferSizeBytes);
                    if (buffer.Peek(0).Length == 0)
                    {
                        _log.Information("API key-specific buffer in {Path} is empty and will be removed until more data is received", subfolder);
                        buffer.Dispose();
                        Directory.Delete(subfolder, true);
                    }
                    else
                    {
                        var activeBuffer = new ActiveLogBuffer(buffer, _shipperFactory.Create(buffer, apiKey));
                        _buffersByApiKey.Add(apiKey, activeBuffer);
                    }
                }
            }
        }

        public void Start()
        {
            lock (_sync)
            {
                foreach (var buffer in OpenBuffers)
                {
                    buffer.Shipper.Start();
                }
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                foreach (var buffer in OpenBuffers)
                {
                    buffer.Shipper.Stop();
                }
            }
        }

        public LogBuffer GetLogBuffer(string apiKey)
        {
            lock (_sync)
            {
                if (apiKey == null)
                {
                    if (_noApiKeyLogBuffer == null)
                    {
                        _log.Information("Creating a new default log buffer in {Path}", _bufferPath);
                        var buffer = new LogBuffer(_bufferPath, _bufferSizeBytes);
                        _noApiKeyLogBuffer = new ActiveLogBuffer(buffer, _shipperFactory.Create(buffer, _outputConfig.ApiKey));
                        _noApiKeyLogBuffer.Shipper.Start();
                    }
                    return _noApiKeyLogBuffer.Buffer;
                }

                if (_buffersByApiKey.TryGetValue(apiKey, out var existing))
                    return existing.Buffer;

                var subfolder = Path.Combine(_bufferPath, Guid.NewGuid().ToString("n"));
                _log.Information("Creating a new API key-specific log buffer in {Path}", subfolder);
                Directory.CreateDirectory(subfolder);
                File.WriteAllText(Path.Combine(subfolder, ".apikey"), MachineScopeDataProtection.Protect(apiKey));
                var newBuffer = new LogBuffer(subfolder, _bufferSizeBytes);
                var newActiveBuffer = new ActiveLogBuffer(newBuffer, _shipperFactory.Create(newBuffer, apiKey));
                _buffersByApiKey.Add(apiKey, newActiveBuffer);
                newActiveBuffer.Shipper.Start();
                return newBuffer;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                foreach (var buffer in OpenBuffers)
                {
                    buffer.Dispose();
                }
            }
        }

        public void Enumerate(Action<ulong, byte[]> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            lock (_sync)
            {
                foreach (var buffer in OpenBuffers)
                {
                    buffer.Buffer.Enumerate(action);
                }
            }
        }

        public static void Truncate(string bufferPath)
        {
            DeleteIfExists(Path.Combine(bufferPath, DataFileName));
            DeleteIfExists(Path.Combine(bufferPath, LockFileName));
            foreach (var subdirectory in Directory.GetDirectories(bufferPath))
            {
                if (File.Exists(Path.Combine(subdirectory, ApiKeyFileName)))
                    Directory.Delete(subdirectory, true);
            }
        }

        static void DeleteIfExists(string filePath)
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }

        IEnumerable<ActiveLogBuffer> OpenBuffers
        {
            get
            {
                if (_noApiKeyLogBuffer != null)
                    yield return _noApiKeyLogBuffer;

                foreach (var buffer in _buffersByApiKey.Values)
                    yield return buffer;
            }
        }
    }
}
