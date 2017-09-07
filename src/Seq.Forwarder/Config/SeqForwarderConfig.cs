﻿// Copyright 2016-2017 Datalust Pty Ltd
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
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Seq.Forwarder.Config
{
    class SeqForwarderConfig
    {
        public static JsonSerializerSettings SerializerSettings { get; } = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters =
            {
                new StringEnumConverter()
            }
        };

        public static SeqForwarderConfig Read(string filename)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            var content = File.ReadAllText(filename);
            return JsonConvert.DeserializeObject<SeqForwarderConfig>(content, SerializerSettings);
        }

        public static void Write(string filename, SeqForwarderConfig data)
        {
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            if (data == null) throw new ArgumentNullException(nameof(data));
            var content = JsonConvert.SerializeObject(data, Formatting.Indented, SerializerSettings);
            File.WriteAllText(filename, content);
        }

        public SeqForwarderDiagnosticConfig Diagnostics { get; set; } = new SeqForwarderDiagnosticConfig();
        public SeqForwarderOutputConfig Output { get; set; } = new SeqForwarderOutputConfig();
        public SeqForwarderStorageConfig Storage { get; set; } = new SeqForwarderStorageConfig();
        public SeqForwarderApiConfig Api { get; set; } = new SeqForwarderApiConfig();
    }
}
