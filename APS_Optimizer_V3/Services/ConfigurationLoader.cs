using APS_Optimizer_V3.Services.Export;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Reflection;

namespace APS_Optimizer_V3.Services;

public static class ConfigurationLoader
{
    private static ExportConfiguration? _loadedExportConfig; // Cache loaded config

    public static ExportConfiguration LoadExportConfiguration()
    {
        // Return cached version if already loaded
        if (_loadedExportConfig != null)
        {
            return _loadedExportConfig;
        }

        var assembly = Assembly.GetExecutingAssembly();
        // --- Determine the resource name ---
        // This assumes the file is in Services/Export folder and default namespace is APS_Optimizer_V3
        // Adjust if your namespace or folder structure is different.
        // Best Practice: Verify using assembly.GetManifestResourceNames() during debug if unsure.
        string resourceName = "";
        if (assembly != null)
        {
            resourceName = assembly!.GetManifestResourceNames()!
                                          .SingleOrDefault(str => str.EndsWith("ExportConfig.json"))!;
        }

        if (string.IsNullOrEmpty(resourceName))
        {
            // List available resources if not found, helps debugging
            var availableResources = string.Join(", ", assembly!.GetManifestResourceNames());
            Debug.WriteLine($"Available resources: {availableResources}");
            throw new FileNotFoundException("Could not find the embedded resource 'ExportConfig.json'. Check build action and resource name.", "ExportConfig.json");
        }

        Debug.WriteLine($"Loading embedded resource: {resourceName}");

        try
        {
            using (Stream? stream = assembly!.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException($"Could not load embedded resource stream for '{resourceName}'.");
                }

                using (var reader = new StreamReader(stream))
                {
                    string jsonContent = reader.ReadToEnd();
                    var config = JsonConvert.DeserializeObject<ExportConfiguration>(jsonContent);

                    if (config == null)
                    {
                        throw new InvalidOperationException("Failed to deserialize embedded export configuration.");
                    }
                    _loadedExportConfig = config; // Cache the loaded config
                    return _loadedExportConfig;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading/deserializing embedded export configuration: {ex}");
            // Re-throw or handle appropriately (e.g., return a default config or throw specific exception)
            throw new InvalidOperationException("Failed to load or parse embedded export configuration.", ex);
        }
    }
}
