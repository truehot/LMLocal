using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace LMLocal.Core.Common
{
    internal static class JsonExtensions
    {
        private static readonly JsonSerializerSettings DefaultSettings = new JsonSerializerSettings
        {
        };

        private static readonly JsonSerializerSettings IndentedSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            
        };

        private static readonly JsonSerializerSettings IndentedSettingsWithEnumValues = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = new JsonConverter[] { new StringEnumConverter() }
        };

        internal static string ToJson(this object obj)
        {
            return JsonConvert.SerializeObject(obj, DefaultSettings);
        }

        internal static string ToJsonIndented(this object obj)
        {
            return JsonConvert.SerializeObject(obj, IndentedSettings);
        }

        internal static string ToJsonIndentedWithEnumValues(this object obj)
        {
            return JsonConvert.SerializeObject(obj, IndentedSettingsWithEnumValues);
        }

        internal static T FromJson<T>(this string json)
        {
            return JsonConvert.DeserializeObject<T>(json, DefaultSettings);
        }
    }
}
