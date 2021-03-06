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
using System.Net.Http;
using Seq.Forwarder.Config;
using Seq.Forwarder.Shipper;
using Seq.Forwarder.Storage;

namespace Seq.Forwarder.Multiplexing
{
    class HttpLogShipperFactory : ILogShipperFactory
    {
        readonly HttpClient _outputHttpClient;
        readonly ServerResponseProxy _serverResponseProxy;
        readonly SeqForwarderOutputConfig _outputConfig;

        public HttpLogShipperFactory(ServerResponseProxy serverResponseProxy, SeqForwarderOutputConfig outputConfig, HttpClient outputHttpClient)
        {
            _outputHttpClient = outputHttpClient;
            _serverResponseProxy = serverResponseProxy ?? throw new ArgumentNullException(nameof(serverResponseProxy));
            _outputConfig = outputConfig ?? throw new ArgumentNullException(nameof(outputConfig));
        }

        public LogShipper Create(LogBuffer logBuffer, string apiKey)
        {
            return new HttpLogShipper(logBuffer, apiKey, _outputConfig, _serverResponseProxy, _outputHttpClient);
        }
    }
}
