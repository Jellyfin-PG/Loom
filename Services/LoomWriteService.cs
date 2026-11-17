using System;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;
using Jellyfin.Plugin.Loom.Interfaces;
using Jellyfin.Plugin.Loom.Models;
using Jellyfin.Plugin.Loom.Helpers;

namespace Jellyfin.Plugin.Loom.Services
{
    public class LoomWriteService : ILoomWriteService
    {
        private readonly ILoomRegistrar _registrar;

        public LoomWriteService(ILoomRegistrar registrar)
        {
            _registrar = registrar;
        }

        private static Guid ParseGuid(JToken? idToken)
        {
            if (idToken == null)
            {
                return Guid.NewGuid();
            }

            try
            {
                if (idToken.Type == JTokenType.Guid)
                {
                    return idToken.ToObject<Guid>();
                }

                if (Guid.TryParse(idToken.ToString(), out var parsedGuid))
                {
                    return parsedGuid;
                }
            }
            catch
            {
            }

            return Guid.NewGuid();
        }

        private static Guid ParseGuid(JsonNode? idNode)
        {
            if (idNode == null)
            {
                return Guid.NewGuid();
            }

            try
            {
                if (Guid.TryParse(idNode.ToString(), out var parsedGuid))
                {
                    return parsedGuid;
                }
            }
            catch
            {
            }

            return Guid.NewGuid();
        }

        private static LoomEntry CreateEntryFromNewtonsoft(JObject payload)
        {
            var id = ParseGuid(payload["id"]);
            var fileNamePattern = payload["fileNamePattern"]?.ToString() ?? string.Empty;
            var callbackAssembly = payload["callbackAssembly"]?.ToString() ?? string.Empty;
            var callbackClass = payload["callbackClass"]?.ToString() ?? string.Empty;
            var callbackMethod = payload["callbackMethod"]?.ToString() ?? string.Empty;

            string targetFilePath = "index.html";
            if (!string.IsNullOrEmpty(fileNamePattern))
            {
                if (!fileNamePattern.Contains("index.html") && !fileNamePattern.Contains("index\\.html"))
                {
                    targetFilePath = fileNamePattern;
                }
            }

            var key = new LoomKey(callbackAssembly, id.ToString());
            var transformDelegate = LoomHelper.CreateLoomCallback(
                callbackAssembly, callbackClass, callbackMethod);

            return new LoomEntry(
                key,
                "1.0.0",
                targetFilePath,
                100,
                transformDelegate)
            {
                FileNamePattern = string.IsNullOrEmpty(fileNamePattern) ? null : fileNamePattern
            };
        }

        private static LoomEntry CreateEntryFromSystemText(JsonObject payload)
        {
            var id = ParseGuid(payload["id"]);
            var fileNamePattern = payload["fileNamePattern"]?.ToString() ?? string.Empty;
            var callbackAssembly = payload["callbackAssembly"]?.ToString() ?? string.Empty;
            var callbackClass = payload["callbackClass"]?.ToString() ?? string.Empty;
            var callbackMethod = payload["callbackMethod"]?.ToString() ?? string.Empty;

            string targetFilePath = "index.html";
            if (!string.IsNullOrEmpty(fileNamePattern))
            {
                if (!fileNamePattern.Contains("index.html") && !fileNamePattern.Contains("index\\.html"))
                {
                    targetFilePath = fileNamePattern;
                }
            }

            var key = new LoomKey(callbackAssembly, id.ToString());
            var transformDelegate = LoomHelper.CreateLoomCallback(
                callbackAssembly, callbackClass, callbackMethod);

            return new LoomEntry(
                key,
                "1.0.0",
                targetFilePath,
                100,
                transformDelegate)
            {
                FileNamePattern = string.IsNullOrEmpty(fileNamePattern) ? null : fileNamePattern
            };
        }

        public void Register(JObject payload)
        {
            var entry = CreateEntryFromNewtonsoft(payload);
            _registrar.Register(entry);
        }

        public void Update(JObject payload)
        {
            var entry = CreateEntryFromNewtonsoft(payload);
            _registrar.Update(entry);
        }

        public void Register(JsonObject payload)
        {
            var entry = CreateEntryFromSystemText(payload);
            _registrar.Register(entry);
        }

        public void Update(JsonObject payload)
        {
            var entry = CreateEntryFromSystemText(payload);
            _registrar.Update(entry);
        }

        public void Register(LoomEntry entry)
        {
            _registrar.Register(entry);
        }

        public void Update(LoomEntry entry)
        {
            _registrar.Update(entry);
        }
    }
}
