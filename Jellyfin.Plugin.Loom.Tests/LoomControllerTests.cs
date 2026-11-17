using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Jellyfin.Plugin.Loom.Controllers;
using Jellyfin.Plugin.Loom.Interfaces;
using Jellyfin.Plugin.Loom.Models;
using Jellyfin.Plugin.Loom.Services;
using Xunit;

namespace Jellyfin.Plugin.Loom.Tests
{
    public class LoomControllerTests
    {
        public static string SampleCallbackMethod(string input) => input + "_Modified";

        [Fact]
        public void Register_MissingPluginId_ReturnsBadRequest()
        {
            var registrar = new LoomRegistrar();
            var controller = new LoomController(registrar);

            var result = controller.Register(new RegisterLoomRequest
            {
                PluginId = "",
                TransformationName = "Transform1"
            });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public void Register_MissingCallback_ReturnsBadRequest()
        {
            var registrar = new LoomRegistrar();
            var controller = new LoomController(registrar);

            var result = controller.Register(new RegisterLoomRequest
            {
                PluginId = "PluginA",
                TransformationName = "Transform1"
            });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("Callback", badRequest.Value?.ToString());
        }

        [Fact]
        public void Register_ValidRequest_ReturnsCreatedAtAction()
        {
            var registrar = new LoomRegistrar();
            var controller = new LoomController(registrar);

            var result = controller.Register(new RegisterLoomRequest
            {
                PluginId = "PluginA",
                TransformationName = "Transform1",
                PluginVersion = "1.0.0",
                CallbackAssembly = typeof(LoomControllerTests).Assembly.GetName().Name,
                CallbackClass = typeof(LoomControllerTests).FullName,
                CallbackMethod = nameof(SampleCallbackMethod)
            });

            var created = Assert.IsType<CreatedAtActionResult>(result);
            Assert.Equal("GetStatus", created.ActionName);
            Assert.Single(registrar.List());
        }

        [Fact]
        public void Update_ValidRequest_ReturnsNoContent()
        {
            var registrar = new LoomRegistrar();
            var controller = new LoomController(registrar);

            var request = new RegisterLoomRequest
            {
                PluginId = "PluginA",
                TransformationName = "Transform1",
                PluginVersion = "1.0.0",
                CallbackAssembly = typeof(LoomControllerTests).Assembly.GetName().Name,
                CallbackClass = typeof(LoomControllerTests).FullName,
                CallbackMethod = nameof(SampleCallbackMethod)
            };

            controller.Register(request);

            request.Priority = 50;
            var updateResult = controller.Update("PluginA", "Transform1", request);

            Assert.IsType<NoContentResult>(updateResult);
            Assert.Equal(50, registrar.List().First().Priority);
        }

        [Fact]
        public void Deregister_ExistingKey_ReturnsNoContent()
        {
            var registrar = new LoomRegistrar();
            var controller = new LoomController(registrar);

            controller.Register(new RegisterLoomRequest
            {
                PluginId = "PluginA",
                TransformationName = "Transform1",
                CallbackAssembly = typeof(LoomControllerTests).Assembly.GetName().Name,
                CallbackClass = typeof(LoomControllerTests).FullName,
                CallbackMethod = nameof(SampleCallbackMethod)
            });

            var result = controller.Deregister("PluginA", "Transform1");
            Assert.IsType<NoContentResult>(result);
            Assert.Empty(registrar.List());
        }

        [Fact]
        public void Deregister_NonExistentKey_ReturnsNotFound()
        {
            var registrar = new LoomRegistrar();
            var controller = new LoomController(registrar);

            var result = controller.Deregister("NonExistent", "NonExistent");
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public void List_ReturnsAllTransformations()
        {
            var registrar = new LoomRegistrar();
            var controller = new LoomController(registrar);

            controller.Register(new RegisterLoomRequest
            {
                PluginId = "PluginA",
                TransformationName = "T1",
                CallbackAssembly = typeof(LoomControllerTests).Assembly.GetName().Name,
                CallbackClass = typeof(LoomControllerTests).FullName,
                CallbackMethod = nameof(SampleCallbackMethod)
            });

            var result = controller.List();
            var okResult = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<object>>(okResult.Value);
            Assert.Single(items);
        }

        [Fact]
        public void GetStatus_ExistingKey_ReturnsOk()
        {
            var registrar = new LoomRegistrar();
            var controller = new LoomController(registrar);

            controller.Register(new RegisterLoomRequest
            {
                PluginId = "PluginA",
                TransformationName = "T1",
                CallbackAssembly = typeof(LoomControllerTests).Assembly.GetName().Name,
                CallbackClass = typeof(LoomControllerTests).FullName,
                CallbackMethod = nameof(SampleCallbackMethod)
            });

            var result = controller.GetStatus("PluginA", "T1");
            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public void GetStatus_MissingKey_ReturnsNotFound()
        {
            var registrar = new LoomRegistrar();
            var controller = new LoomController(registrar);

            var result = controller.GetStatus("Missing", "Missing");
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public void InvalidateCache_ReturnsNoContent()
        {
            var registrar = new LoomRegistrar();
            var controller = new LoomController(registrar);

            var result = controller.InvalidateCache("index.html");
            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public void GetVersion_ReturnsContractVersion()
        {
            var registrar = new LoomRegistrar();
            var controller = new LoomController(registrar);

            var result = controller.GetVersion();
            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(ok.Value);
        }

        [Fact]
        public void GetDiagnostics_ReturnsHealthyCount()
        {
            var registrar = new LoomRegistrar();
            var controller = new LoomController(registrar);

            controller.Register(new RegisterLoomRequest
            {
                PluginId = "PluginA",
                TransformationName = "T1",
                CallbackAssembly = typeof(LoomControllerTests).Assembly.GetName().Name,
                CallbackClass = typeof(LoomControllerTests).FullName,
                CallbackMethod = nameof(SampleCallbackMethod)
            });

            var result = controller.GetDiagnostics();
            var ok = Assert.IsType<OkObjectResult>(result);
            var diag = Assert.IsType<LoomDiagnosticsResult>(ok.Value);
            Assert.True(diag.IsStartupComplete);
            Assert.Equal("1.0.0", diag.ContractVersion);
            Assert.Equal(1, diag.TotalRules);
            Assert.Equal(1, diag.HealthyRules);

            // Verify System.Text.Json serialization succeeds without ambiguous parameter binding exceptions
            var json = System.Text.Json.JsonSerializer.Serialize(diag);
            Assert.Contains("\"IsStartupComplete\":true", json);
        }
    }
}
