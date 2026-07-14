using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Jellyfin.Plugin.Loom.Interfaces;
using Jellyfin.Plugin.Loom.Models;
using Jellyfin.Plugin.Loom.Helpers;

namespace Jellyfin.Plugin.Loom.Controllers
{
    [ApiController]
    [Route("Loom/v1")]
    [Authorize(Policy = "RequiresElevation")]
    public class LoomController : ControllerBase
    {
        private readonly ILoomRegistrar _registrar;

        public LoomController(ILoomRegistrar registrar)
        {
            _registrar = registrar;
        }

        [HttpPost("transformations")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult Register([FromBody] RegisterLoomRequest request)
        {
            if (string.IsNullOrEmpty(request.PluginId) || string.IsNullOrEmpty(request.TransformationName))
            {
                return BadRequest("PluginId and TransformationName are required.");
            }

            var key = new LoomKey(request.PluginId, request.TransformationName);
            
            Func<LoomContext, Task<string>> transformDelegate;
            if (!string.IsNullOrEmpty(request.CallbackAssembly) &&
                !string.IsNullOrEmpty(request.CallbackClass) &&
                !string.IsNullOrEmpty(request.CallbackMethod))
            {
                try
                {
                    transformDelegate = LoomHelper.CreateLoomCallback(
                        request.CallbackAssembly, request.CallbackClass, request.CallbackMethod);
                }
                catch (Exception ex)
                {
                    return BadRequest($"Failed to bind reflection callback: {ex.Message}");
                }
            }
            else
            {
                return BadRequest("Callback information is required for REST registration.");
            }

            var entry = new LoomEntry(
                key,
                request.PluginVersion,
                string.IsNullOrEmpty(request.TargetFilePath) ? "index.html" : request.TargetFilePath,
                request.Priority,
                transformDelegate)
            {
                FileNamePattern = request.FileNamePattern
            };

            _registrar.Register(entry);
            return CreatedAtAction(nameof(GetStatus), new { pluginId = request.PluginId, name = request.TransformationName }, null);
        }

        [HttpPut("transformations/{pluginId}/{name}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult Update(string pluginId, string name, [FromBody] RegisterLoomRequest request)
        {
            var key = new LoomKey(pluginId, name);
            
            Func<LoomContext, Task<string>> transformDelegate;
            if (!string.IsNullOrEmpty(request.CallbackAssembly) &&
                !string.IsNullOrEmpty(request.CallbackClass) &&
                !string.IsNullOrEmpty(request.CallbackMethod))
            {
                try
                {
                    transformDelegate = LoomHelper.CreateLoomCallback(
                        request.CallbackAssembly, request.CallbackClass, request.CallbackMethod);
                }
                catch (Exception ex)
                {
                    return BadRequest($"Failed to bind reflection callback: {ex.Message}");
                }
            }
            else
            {
                return BadRequest("Callback information is required.");
            }

            var entry = new LoomEntry(
                key,
                request.PluginVersion,
                string.IsNullOrEmpty(request.TargetFilePath) ? "index.html" : request.TargetFilePath,
                request.Priority,
                transformDelegate)
            {
                FileNamePattern = request.FileNamePattern
            };

            _registrar.Update(entry);
            return NoContent();
        }

        [HttpDelete("transformations/{pluginId}/{name}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult Deregister(string pluginId, string name)
        {
            var key = new LoomKey(pluginId, name);
            if (_registrar.Deregister(key))
            {
                return NoContent();
            }
            return NotFound();
        }

        [HttpGet("transformations")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult List()
        {
            var list = _registrar.List().Select(e => new
            {
                PluginId = e.Key.PluginId,
                TransformationName = e.Key.TransformationName,
                PluginVersion = e.PluginVersion,
                TargetFilePath = e.TargetFilePath,
                FileNamePattern = e.FileNamePattern,
                Priority = e.Priority,
                RegisteredAt = e.RegisteredAt,
                LastAppliedAt = e.LastAppliedAt,
                LastFailedAt = e.LastFailedAt,
                LastError = e.LastError,
                Status = e.Status.ToString()
            });
            return Ok(list);
        }

        [HttpGet("transformations/{pluginId}/{name}/status")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetStatus(string pluginId, string name)
        {
            var key = new LoomKey(pluginId, name);
            var entry = _registrar.GetStatus(key);
            if (entry == null)
            {
                return NotFound();
            }
            return Ok(new
            {
                PluginId = entry.Key.PluginId,
                TransformationName = entry.Key.TransformationName,
                Status = entry.Status.ToString(),
                LastAppliedAt = entry.LastAppliedAt,
                LastFailedAt = entry.LastFailedAt,
                LastError = entry.LastError
            });
        }

        [HttpPost("cache/invalidate")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public IActionResult InvalidateCache([FromQuery] string path = "index.html")
        {
            _registrar.InvalidateCache(path);
            return NoContent();
        }

        [HttpGet("version")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetVersion()
        {
            return Ok(new { ContractVersion = "1.0.0" });
        }
    }

    public class RegisterLoomRequest
    {
        public string PluginId { get; set; } = string.Empty;
        public string TransformationName { get; set; } = string.Empty;
        public string PluginVersion { get; set; } = string.Empty;
        public string TargetFilePath { get; set; } = string.Empty;
        public string? FileNamePattern { get; set; }
        public int Priority { get; set; } = 100;
        public string? CallbackAssembly { get; set; }
        public string? CallbackClass { get; set; }
        public string? CallbackMethod { get; set; }
    }
}
