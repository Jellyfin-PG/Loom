using System;
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

        private Guid ParseGuid(JToken? idToken)
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
        
        public void Register(JObject payload)
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
                
            var entry = new LoomEntry(
                key,
                "1.0.0",
                targetFilePath,
                100,
                transformDelegate)
            {
                FileNamePattern = fileNamePattern
            };
            
            _registrar.Register(entry);
        }

        public void Update(JObject payload)
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
                
            var entry = new LoomEntry(
                key,
                "1.0.0",
                targetFilePath,
                100,
                transformDelegate)
            {
                FileNamePattern = fileNamePattern
            };
            
            _registrar.Update(entry);
        }
    }
}
