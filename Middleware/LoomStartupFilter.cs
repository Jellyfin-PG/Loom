using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.Loom.Middleware
{
    public class LoomStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return builder =>
            {
                if (Plugin.Instance != null && Plugin.Instance.ServiceProvider == null)
                {
                    Plugin.Instance.ServiceProvider = builder.ApplicationServices;
                }

                builder.UseMiddleware<LoomMiddleware>();
                next(builder);
            };
        }
    }
}
