using System.Reflection;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Utils.Json.Converters;
using SPTarkov.Server.Web.Models.Configs;
using SPTarkov.Server.Web.Services;

namespace ItemPropertyBackport;

public class ConfigRegistration : IOnDIConstruct
{
    public static async Task OnDIConstructAsync(
        IServiceCollection serviceCollection,
        CancellationToken cancellationToken
    )
    {
        var modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        var jsonSerializerOptions = new JsonSerializerOptions()
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new StringToMongoIdConverter() }
        };

        var configJson = await File.ReadAllTextAsync(Path.Join(modDir, "config.json"), cancellationToken);
        var config = JsonSerializer.Deserialize<Config>(configJson, jsonSerializerOptions)!;

        // ‘PlayFuzeSound’ is not nullable
        config.ExcludeProperties.Add("PlayFuzeSound");

        if (config.ExcludeProperties.Contains("Durability"))
        {
            config.ExcludeProperties.Add("MaxDurability");
        }

        serviceCollection.AddSingleton(config);
    }
}

[Injectable(InjectionType = InjectionType.Singleton)]
public class ConfigEditorProvider(Config config, ModHelper modHelper) : IConfigEditorConfigProvider
{
    public IEnumerable<ConfigEditorConfigRegistration> GetConfigs()
    {
        var metadata = new ModMetadata();
        var modDir = modHelper.GetAbsolutePathToModFolder();
        yield return ConfigEditorConfigRegistration.Create(
            metadata.ModGuid,
            metadata.Name,
            config,
            Path.Combine(modDir, "config.json")
        );
    }
}
