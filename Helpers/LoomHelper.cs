using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Jellyfin.Plugin.Loom.Models;

namespace Jellyfin.Plugin.Loom.Helpers
{
    public static class LoomHelper
    {
        public static Func<LoomContext, Task<string>> CreateLoomCallback(
            string assemblyName, string className, string methodName)
        {
            return async context =>
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.FullName == assemblyName || a.GetName().Name == assemblyName);
                    
                if (assembly == null)
                {
                    try
                    {
                        assembly = Assembly.Load(assemblyName);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Could not load assembly '{assemblyName}': {ex.Message}", ex);
                    }
                }
                
                var type = assembly.GetType(className) 
                    ?? throw new InvalidOperationException($"Could not find type '{className}' in assembly '{assemblyName}'");
                    
                var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException($"Could not find static method '{methodName}' in type '{className}'");
                    
                var parameters = method.GetParameters();
                object?[] args;
                if (parameters.Length == 0)
                {
                    args = Array.Empty<object>();
                }
                else if (parameters.Length == 1)
                {
                    var paramType = parameters[0].ParameterType;
                    if (paramType == typeof(string))
                    {
                        args = new object?[] { context.Content };
                    }
                    else
                    {
                        var paramInstance = Activator.CreateInstance(paramType)
                            ?? throw new InvalidOperationException($"Could not instantiate parameter type '{paramType.FullName}'");
                            
                        var prop = paramType.GetProperty("Contents", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                            ?? paramType.GetProperty("contents", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                            
                        if (prop != null && prop.CanWrite)
                        {
                            prop.SetValue(paramInstance, context.Content);
                        }
                        args = new object?[] { paramInstance };
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Callback method '{methodName}' must accept 0 or 1 parameter");
                }
                
                var invokeResult = method.Invoke(null, args);
                if (invokeResult is Task<string> task)
                {
                    return await task;
                }
                if (invokeResult is string str)
                {
                    return str;
                }
                return context.Content;
            };
        }
    }
}
