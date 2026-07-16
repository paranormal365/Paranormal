using Newtonsoft.Json;

namespace Ben.Data.Common.Services;

public static class JsonConvertService<T> where T : class
{
    private static Newtonsoft.Json.JsonSerializerSettings _settings = new Newtonsoft.Json.JsonSerializerSettings()
    {
        NullValueHandling = NullValueHandling.Include, //Newtonsoft.Json.NullValueHandling.Ignore,
        ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
        //Formatting = Newtonsoft.Json.Formatting.Indented,
        TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Auto,
        MetadataPropertyHandling = Newtonsoft.Json.MetadataPropertyHandling.ReadAhead,
        TypeNameAssemblyFormatHandling = Newtonsoft.Json.TypeNameAssemblyFormatHandling.Simple,
        PreserveReferencesHandling = Newtonsoft.Json.PreserveReferencesHandling.Objects,
        DateFormatHandling = Newtonsoft.Json.DateFormatHandling.MicrosoftDateFormat,
        //DateFormatString = "yyyy-MM-ddTHH:mm:ss.fffZ",
        DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Utc,
        ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
    };

    public static JsonSerializerSettings GetSettings()
    {
        return _settings;
    }

    public static JsonSerializerSettings GetSettingsForDisplay()
    {
        var settings = _settings;
        settings.Formatting = Newtonsoft.Json.Formatting.Indented;
        settings.DateFormatHandling = Newtonsoft.Json.DateFormatHandling.IsoDateFormat;
        settings.DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Local;
        return settings;
    }

    public static string Serialize(T value)
    {
        return Newtonsoft.Json.JsonConvert.SerializeObject(value, _settings);
    }

    public static T? Deserialize(string serializedValue)
    {
        return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(serializedValue, _settings) ?? null;
    }
}
