using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.InspectTypes
{
    public class ParamInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; }
    }

    /// <summary>
    /// Declaration site of a member, set only for members declared in the solution.
    /// </summary>
    public abstract class MemberLocationInfo
    {
        [JsonProperty("line", NullValueHandling = NullValueHandling.Ignore, Order = 100)]
        public int? Line { get; set; }
        [JsonProperty("file_path", NullValueHandling = NullValueHandling.Ignore, Order = 101)]
        public string FilePath { get; set; }
    }

    public class MethodInfo : MemberLocationInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("accessibility")]
        public string Accessibility { get; set; }
        [JsonProperty("is_static", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsStatic { get; set; }
        [JsonProperty("is_extension", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsExtension { get; set; }
        [JsonProperty("is_generic", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsGeneric { get; set; }
        [JsonProperty("return_type", NullValueHandling = NullValueHandling.Ignore)]
        public string ReturnType { get; set; }
        [JsonProperty("type_parameters", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> TypeParameters { get; set; }
        [JsonProperty("parameters")]
        public List<ParamInfo> Parameters { get; set; }
    }

    public class PropertyInfo : MemberLocationInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("accessibility")]
        public string Accessibility { get; set; }
        [JsonProperty("is_static", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsStatic { get; set; }
        [JsonProperty("is_read_only", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsReadOnly { get; set; }
        [JsonProperty("is_write_only", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsWriteOnly { get; set; }
        [JsonProperty("has_getter")]
        public bool HasGetter { get; set; }
        [JsonProperty("has_setter")]
        public bool HasSetter { get; set; }
    }

    public class EventInfo : MemberLocationInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("accessibility")]
        public string Accessibility { get; set; }
        [JsonProperty("is_static", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsStatic { get; set; }
    }

    public class FieldInfo : MemberLocationInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("accessibility")]
        public string Accessibility { get; set; }
        [JsonProperty("is_static", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsStatic { get; set; }
        [JsonProperty("is_read_only", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsReadOnly { get; set; }
        [JsonProperty("is_const", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsConst { get; set; }
        [JsonProperty("constant_value", NullValueHandling = NullValueHandling.Ignore)]
        public string ConstantValue { get; set; }
    }

    public class AttributeInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("constructor_arguments")]
        public List<string> ConstructorArguments { get; set; }
    }

    public class TypeInspectionResult
    {
        [JsonProperty("full_name")]
        public string FullName { get; set; }
        [JsonProperty("metadata_name")]
        public string MetadataName { get; set; }
        [JsonProperty("kind")]
        public string Kind { get; set; }
        [JsonProperty("accessibility")]
        public string Accessibility { get; set; }
        [JsonProperty("containing_namespace")]
        public string ContainingNamespace { get; set; }
        [JsonProperty("assembly_name")]
        public string AssemblyName { get; set; }
        [JsonProperty("project_name")]
        public string ProjectName { get; set; }
        /// <summary>Solution-relative file of the type; null for a type from a referenced assembly.</summary>
        [JsonProperty("file_path", NullValueHandling = NullValueHandling.Ignore)]
        public string FilePath { get; set; }
        [JsonProperty("is_abstract")]
        public bool IsAbstract { get; set; }
        [JsonProperty("is_sealed")]
        public bool IsSealed { get; set; }
        [JsonProperty("is_static")]
        public bool IsStatic { get; set; }
        [JsonProperty("is_generic")]
        public bool IsGenericType { get; set; }
        [JsonProperty("arity")]
        public int Arity { get; set; }
        [JsonProperty("type_parameters")]
        public List<string> TypeParameters { get; set; }
        [JsonProperty("base_type")]
        public string BaseType { get; set; }
        [JsonProperty("interfaces")]
        public List<string> Interfaces { get; set; }
        [JsonProperty("attributes")]
        public List<AttributeInfo> Attributes { get; set; }
        [JsonProperty("constructors")]
        public List<MethodInfo> Constructors { get; set; }
        [JsonProperty("total_constructors")]
        public int TotalConstructors { get; set; }
        [JsonProperty("properties")]
        public List<PropertyInfo> Properties { get; set; }
        [JsonProperty("total_properties")]
        public int TotalProperties { get; set; }
        [JsonProperty("methods")]
        public List<MethodInfo> Methods { get; set; }
        [JsonProperty("total_methods")]
        public int TotalMethods { get; set; }
        [JsonProperty("events")]
        public List<EventInfo> Events { get; set; }
        [JsonProperty("total_events")]
        public int TotalEvents { get; set; }
        [JsonProperty("fields")]
        public List<FieldInfo> Fields { get; set; }
        [JsonProperty("total_fields")]
        public int TotalFields { get; set; }
        [JsonProperty("has_more_results")]
        public bool HasMoreResults { get; set; }
    }
}
